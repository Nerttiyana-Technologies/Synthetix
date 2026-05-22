namespace Synthetix.Pipeline;

using System;
using System.Collections.Generic;
using System.Linq;
using Synthetix.Models;

/// <summary>A flatten-path expression and whether the value it produces can be null.</summary>
internal sealed class FlattenExpression
{
    public FlattenExpression(string expression, bool mightBeNull, MemberModel finalMember)
    {
        Expression = expression;
        MightBeNull = mightBeNull;
        FinalMember = finalMember;
    }

    /// <summary>The C# expression that reads the value, for example "order.Customer?.Name".</summary>
    public string Expression { get; }

    /// <summary>True when the expression can produce null.</summary>
    public bool MightBeNull { get; }

    /// <summary>The last member on the path - the one that holds the actual value.</summary>
    public MemberModel FinalMember { get; }
}

/// <summary>The result of unflattening: a "new T { ... }" expression and what it consumed.</summary>
internal sealed class UnflattenResult
{
    public UnflattenResult(string expression, List<string> consumedSourceNames)
    {
        Expression = expression;
        ConsumedSourceNames = consumedSourceNames;
    }

    /// <summary>The "new TargetType { ... }" expression that builds the nested object.</summary>
    public string Expression { get; }

    /// <summary>The flat source member names this unflattening used up.</summary>
    public List<string> ConsumedSourceNames { get; }
}

/// <summary>
/// Handles the flattening and unflattening conventions from design doc 7.2.
/// </summary>
/// <remarks>
/// Flattening collapses a nested object into a flat one: "Order.Customer.Name"
/// becomes "OrderCustomerName". Unflattening does the opposite. Segment
/// boundaries are recognised by PascalCase only - the start of each segment is
/// an uppercase letter.
/// </remarks>
internal static class FlatteningResolver
{
    /// <summary>
    /// Finds every nested source path whose member names, joined together, equal
    /// <paramref name="targetName"/>. Only multi-step paths are returned, because
    /// a one-step path is just a same-name match.
    /// </summary>
    public static List<List<MemberModel>> FindFlattenPaths(
        TypeModel sourceType, string targetName, PropertyNameMatching matching)
    {
        var found = new List<List<MemberModel>>();
        Search(sourceType, targetName, new List<MemberModel>(), found, matching);

        // Drop one-step paths - those are handled by same-name matching, not flattening.
        return found.Where(path => path.Count >= 2).ToList();
    }

    private static void Search(
        TypeModel type,
        string remaining,
        List<MemberModel> prefix,
        List<List<MemberModel>> found,
        PropertyNameMatching matching)
    {
        foreach (MemberModel member in type.Members)
        {
            if (!member.CanRead)
            {
                continue;
            }

            // The whole remaining name is this member: the path ends here.
            if (NameEquals(member.Name, remaining, matching))
            {
                var completed = new List<MemberModel>(prefix) { member };
                found.Add(completed);
                continue;
            }

            // This member is the start of the remaining name. Step into it and
            // keep matching - but only if the cut lands on a PascalCase boundary
            // (the next character is uppercase) and the member is something we
            // can look inside.
            if (remaining.Length > member.Name.Length &&
                StartsWith(remaining, member.Name, matching) &&
                char.IsUpper(remaining[member.Name.Length]) &&
                member.Type.IsComplex)
            {
                string rest = remaining.Substring(member.Name.Length);
                var deeperPrefix = new List<MemberModel>(prefix) { member };
                Search(member.Type, rest, deeperPrefix, found, matching);
            }
        }
    }

    /// <summary>
    /// Builds the C# expression that walks a flatten path, applying the chosen
    /// null handling for any step along the way that could be null.
    /// </summary>
    public static FlattenExpression BuildExpression(
        string rootExpression,
        IReadOnlyList<MemberModel> path,
        NullHandling nullHandling,
        string mappingDescription)
    {
        MemberModel finalMember = path[path.Count - 1];

        if (nullHandling == NullHandling.Throw)
        {
            return BuildThrowingExpression(rootExpression, path, finalMember, mappingDescription);
        }

        // Propagate and UseDefault both build the same "?." chain. The caller
        // decides afterwards whether to coalesce a null into a default value.
        return BuildNullSafeChain(rootExpression, path, finalMember);
    }

    /// <summary>Builds a "a?.b?.c" chain - if any step is null, the whole thing is null.</summary>
    private static FlattenExpression BuildNullSafeChain(
        string rootExpression, IReadOnlyList<MemberModel> path, MemberModel finalMember)
    {
        string expression = rootExpression;
        bool canBeNull = false;

        foreach (MemberModel step in path)
        {
            // Use "?." when the value we are reading from might already be null.
            expression += (canBeNull ? "?." : ".") + step.Name;
            canBeNull = canBeNull || CanBeNull(step);
        }

        return new FlattenExpression(expression, canBeNull, finalMember);
    }

    /// <summary>Builds a chain that throws a clear exception the moment a step is null.</summary>
    private static FlattenExpression BuildThrowingExpression(
        string rootExpression,
        IReadOnlyList<MemberModel> path,
        MemberModel finalMember,
        string mappingDescription)
    {
        string expression = rootExpression;
        bool receiverCanBeNull = false;

        for (int i = 0; i < path.Count; i++)
        {
            MemberModel step = path[i];

            // Before reading the next step, make sure the thing we read it from
            // is not null - throw a descriptive exception if it is.
            if (receiverCanBeNull)
            {
                string previousName = path[i - 1].Name;
                expression =
                    "(" + expression + " ?? throw new global::System.InvalidOperationException(\"" +
                    "Synthetix: '" + previousName + "' was null while mapping " + mappingDescription +
                    ".\"))";
            }

            expression += "." + step.Name;
            receiverCanBeNull = CanBeNull(step);
        }

        // With throwing handling, the only remaining way to get null is if the
        // final value itself is a nullable type.
        return new FlattenExpression(expression, CanBeNull(finalMember), finalMember);
    }

    /// <summary>
    /// Tries to unflatten: builds a nested "new T { ... }" object by spreading
    /// flat source members into it. Returns null when nothing could be filled.
    /// </summary>
    public static UnflattenResult? TryUnflatten(
        TypeModel complexTarget,
        string prefix,
        TypeModel sourceType,
        string sourceRootExpression,
        PropertyNameMatching matching)
    {
        // For v0.1, unflattening builds the nested object with an object
        // initializer, so the target type needs an accessible parameterless
        // constructor.
        if (!HasParameterlessConstructor(complexTarget))
        {
            return null;
        }

        var assignments = new List<string>();
        var consumed = new List<string>();

        foreach (MemberModel member in complexTarget.Members)
        {
            if (!member.CanWrite)
            {
                continue;
            }

            string flatName = prefix + member.Name;
            MemberModel? flatSource = FindReadableMember(sourceType, flatName, matching);

            if (flatSource is not null && TypesLineUp(flatSource.Type, member.Type))
            {
                // A flat source member maps straight onto this nested member.
                assignments.Add(member.Name + " = " + sourceRootExpression + "." + flatSource.Name);
                consumed.Add(flatSource.Name);
            }
            else if (member.Type.IsComplex)
            {
                // The nested member is itself complex - try to unflatten it too.
                UnflattenResult? deeper = TryUnflatten(
                    member.Type, flatName, sourceType, sourceRootExpression, matching);
                if (deeper is not null)
                {
                    assignments.Add(member.Name + " = " + deeper.Expression);
                    consumed.AddRange(deeper.ConsumedSourceNames);
                }
            }
        }

        if (assignments.Count == 0)
        {
            return null;
        }

        string expression = "new " + complexTarget.DisplayName + " { " + string.Join(", ", assignments) + " }";
        return new UnflattenResult(expression, consumed);
    }

    // ===================== small shared helpers =====================

    /// <summary>
    /// True when reading this member could give back null. This follows the
    /// project's nullable-reference-type annotations: a reference type written
    /// without a "?" is treated as never-null, so a flatten path through it
    /// needs no guard.
    /// </summary>
    private static bool CanBeNull(MemberModel member)
        => member.IsNullableReference || member.Type.IsNullableValueType;

    private static bool HasParameterlessConstructor(TypeModel type)
        => type.Constructors.Any(c => c.IsAccessible && c.Parameters.Count == 0);

    private static MemberModel? FindReadableMember(TypeModel type, string name, PropertyNameMatching matching)
    {
        foreach (MemberModel member in type.Members)
        {
            if (member.CanRead && NameEquals(member.Name, name, matching))
            {
                return member;
            }
        }

        return null;
    }

    /// <summary>A simple "are these types directly compatible" check for unflattening.</summary>
    private static bool TypesLineUp(TypeModel source, TypeModel target)
        => source.IdentityKey == target.IdentityKey;

    private static bool NameEquals(string a, string b, PropertyNameMatching matching)
        => string.Equals(a, b, Comparison(matching));

    private static bool StartsWith(string text, string prefix, PropertyNameMatching matching)
        => text.StartsWith(prefix, Comparison(matching));

    private static StringComparison Comparison(PropertyNameMatching matching)
        => matching == PropertyNameMatching.IgnoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
