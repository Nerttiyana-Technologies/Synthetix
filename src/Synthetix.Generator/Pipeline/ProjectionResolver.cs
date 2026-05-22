namespace Synthetix.Pipeline;

using System.Collections.Generic;
using System.Linq;
using Synthetix.Diagnostics;
using Synthetix.Models;

/// <summary>
/// Stage 3 helper for IQueryable projection (design doc 7.10). It turns a
/// resolved source/target pair into a single object-initializer expression -
/// the body of the projection lambda - that EF Core can translate to SQL.
/// </summary>
/// <remarks>
/// An expression tree can only carry an *expression*. So this resolver inlines
/// everything: a nested object becomes a nested "new T { ... }", a collection
/// becomes ".Select(...).ToList()". Anything that would need a statement - a
/// converter or user-mapping method call, a switch over an enum, polymorphic
/// dispatch - cannot be projected, and is reported as SYNTX023 instead of
/// quietly producing code that fails deep inside EF Core's query translator.
/// </remarks>
internal static class ProjectionResolver
{
    // A generous depth limit so a strange, deeply nested graph cannot loop forever.
    private const int MaxDepth = 12;

    /// <summary>
    /// Builds the body of a projection lambda - a "new TTarget { ... }"
    /// expression - or returns null (and adds SYNTX023) when the mapping needs
    /// something an expression tree cannot carry.
    /// </summary>
    public static string? Resolve(
        TypeModel source,
        TypeModel target,
        string sourceExpression,
        MapperModel mapper,
        EquatableArray<MemberConfig> configs,
        List<DiagnosticInfo> diagnostics,
        LocationInfo? location,
        string mappingName)
    {
        return BuildObject(
            source, target, sourceExpression, mapper, configs,
            diagnostics, location, mappingName, depth: 0);
    }

    /// <summary>
    /// Builds one "new T(ctorArgs) { Member = ..., ... }" expression. Nested
    /// objects are projected by convention only - member configuration applies
    /// at the top level (depth 0).
    /// </summary>
    private static string? BuildObject(
        TypeModel source,
        TypeModel target,
        string sourceExpr,
        MapperModel mapper,
        EquatableArray<MemberConfig> configs,
        List<DiagnosticInfo> diagnostics,
        LocationInfo? location,
        string mappingName,
        int depth)
    {
        if (depth > MaxDepth)
        {
            Fail(diagnostics, location, mappingName, "the object graph is too deeply nested to project");
            return null;
        }

        if (!target.IsComplex || !source.IsComplex)
        {
            Fail(diagnostics, location, mappingName, "both sides must be a class, struct, or record");
            return null;
        }

        // ----- Sort the member configuration into quick lookups -----
        var mapValues = new Dictionary<string, MemberConfig>(System.StringComparer.OrdinalIgnoreCase);
        var mapProperties = new Dictionary<string, MemberConfig>(System.StringComparer.OrdinalIgnoreCase);
        var ignoredTargets = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (MemberConfig config in configs)
        {
            if (config.Kind == ConfigKind.MapValue && config.Target is not null)
            {
                mapValues[config.Target] = config;
            }
            else if (config.Kind == ConfigKind.MapProperty && config.Target is not null)
            {
                mapProperties[config.Target] = config;
            }
            else if (config.Kind == ConfigKind.IgnoreTarget && config.Target is not null)
            {
                ignoredTargets.Add(config.Target);
            }
        }

        // ----- Choose a constructor -----
        ConstructorModel? constructor = ChooseConstructor(target, source);
        if (constructor is null)
        {
            Fail(diagnostics, location, mappingName,
                "the target type '" + target.DisplayName + "' has no constructor a projection can use");
            return null;
        }

        var constructorFilled = new HashSet<string>(
            constructor.Parameters.Select(p => p.Name), System.StringComparer.OrdinalIgnoreCase);

        // ----- Constructor arguments (matched to source members by name) -----
        var constructorArguments = new List<string>();
        foreach (ParameterModel parameter in constructor.Parameters)
        {
            MemberModel? sourceMember = source.FindMember(parameter.Name, ignoreCase: true);
            if (sourceMember is null || !sourceMember.CanRead)
            {
                Fail(diagnostics, location, mappingName,
                    "constructor parameter '" + parameter.Name + "' has no matching source member");
                return null;
            }

            string? value = BuildValue(
                sourceMember.Type, parameter.Type, sourceExpr + "." + sourceMember.Name,
                mapper, diagnostics, location, mappingName, depth);
            if (value is null)
            {
                return null;
            }

            constructorArguments.Add(value);
        }

        // ----- Initializer members: every writable member the constructor missed -----
        var initializerMembers = new List<string>();
        foreach (MemberModel targetMember in target.Members)
        {
            if (!targetMember.CanWrite ||
                constructorFilled.Contains(targetMember.Name) ||
                ignoredTargets.Contains(targetMember.Name))
            {
                continue;
            }

            // A [MapValue] constant. A value method cannot be called in a tree.
            if (mapValues.TryGetValue(targetMember.Name, out MemberConfig? mapValue))
            {
                if (mapValue.HasConstant && mapValue.ConstantExpression is not null)
                {
                    initializerMembers.Add(targetMember.Name + " = " + mapValue.ConstantExpression);
                    continue;
                }

                Fail(diagnostics, location, mappingName,
                    "member '" + targetMember.Name + "' uses a value method, which a projection cannot call");
                return null;
            }

            // Work out where this member's value comes from.
            string? valueSourceExpr;
            TypeModel? valueSourceType;

            if (mapProperties.TryGetValue(targetMember.Name, out MemberConfig? mapProperty))
            {
                // A [MapProperty] with a "Use" converter is a method call.
                if (mapProperty.Use is not null)
                {
                    Fail(diagnostics, location, mappingName,
                        "member '" + targetMember.Name +
                        "' uses a converter method, which a projection cannot call");
                    return null;
                }

                if (mapProperty.Source is null ||
                    !TryResolvePath(source, mapProperty.Source, sourceExpr,
                        out valueSourceExpr, out valueSourceType))
                {
                    Fail(diagnostics, location, mappingName,
                        "source path '" + (mapProperty.Source ?? string.Empty) +
                        "' for member '" + targetMember.Name + "' was not found");
                    return null;
                }
            }
            else
            {
                ResolveByConvention(
                    source, targetMember.Name, sourceExpr, mapper.Options,
                    out valueSourceExpr, out valueSourceType);
            }

            // No source for this member - a projection may leave it at its
            // default value, so simply skip it.
            if (valueSourceExpr is null || valueSourceType is null)
            {
                continue;
            }

            string? memberValue = BuildValue(
                valueSourceType, targetMember.Type, valueSourceExpr,
                mapper, diagnostics, location, mappingName, depth);
            if (memberValue is null)
            {
                return null;
            }

            initializerMembers.Add(targetMember.Name + " = " + memberValue);
        }

        string construction = "new " + target.DisplayName +
            (constructorArguments.Count > 0 ? "(" + string.Join(", ", constructorArguments) + ")" : "()");

        return initializerMembers.Count == 0
            ? construction
            : construction + " { " + string.Join(", ", initializerMembers) + " }";
    }

    /// <summary>
    /// Builds the projectable expression that converts one source value into the
    /// target member's type. Returns null (with SYNTX023) when no expression-tree
    /// form exists.
    /// </summary>
    private static string? BuildValue(
        TypeModel sourceType,
        TypeModel targetType,
        string sourceExpr,
        MapperModel mapper,
        List<DiagnosticInfo> diagnostics,
        LocationInfo? location,
        string mappingName,
        int depth)
    {
        // Exactly the same type, or anything assigned to object: copy straight.
        if (sourceType.IdentityKey == targetType.IdentityKey || targetType.IdentityKey == "object")
        {
            return sourceExpr;
        }

        // Nullable value-type adjustment (for example int? and int).
        TypeModel sourceCore = sourceType.IsNullableValueType ? sourceType.Inner! : sourceType;
        TypeModel targetCore = targetType.IsNullableValueType ? targetType.Inner! : targetType;
        bool coresMatch =
            sourceCore.IdentityKey == targetCore.IdentityKey ||
            ConversionResolver.HasImplicitNumericConversion(sourceCore.IdentityKey, targetCore.IdentityKey);

        if (coresMatch && (sourceType.IsNullableValueType || targetType.IsNullableValueType))
        {
            // T -> T?  and  T? -> T?  are lifted by the compiler. T? -> T needs
            // a ".Value" access, which an expression tree carries fine.
            return targetType.IsNullableValueType ? sourceExpr : sourceExpr + ".Value";
        }

        // Built-in implicit numeric widening (for example int to long).
        if (sourceType.IsSimple && targetType.IsSimple &&
            ConversionResolver.HasImplicitNumericConversion(sourceType.IdentityKey, targetType.IdentityKey))
        {
            return sourceExpr;
        }

        // A nested complex member - recurse into an inline "new T { ... }".
        if (sourceType.IsComplex && targetType.IsComplex)
        {
            return BuildObject(
                sourceType, targetType, sourceExpr, mapper,
                EquatableArray<MemberConfig>.Empty, diagnostics, location, mappingName, depth + 1);
        }

        // A collection member - project element by element with ".Select(...)".
        if (sourceType.IsCollection && targetType.IsCollection)
        {
            return BuildCollection(
                sourceType, targetType, sourceExpr, mapper, diagnostics, location, mappingName, depth);
        }

        // Everything else - a different enum (which would need a switch), a
        // string conversion (a Parse call), or a real mismatch - cannot live in
        // an expression tree.
        Fail(diagnostics, location, mappingName,
            "there is no projectable conversion from '" + sourceType.DisplayName +
            "' to '" + targetType.DisplayName + "'");
        return null;
    }

    /// <summary>
    /// Builds a ".Select(x => ...).ToList()" style expression for a collection
    /// member. Only arrays and lists are projectable - EF Core can translate
    /// those; sets, dictionaries, and immutable collections cannot be projected.
    /// </summary>
    private static string? BuildCollection(
        TypeModel sourceType,
        TypeModel targetType,
        string sourceExpr,
        MapperModel mapper,
        List<DiagnosticInfo> diagnostics,
        LocationInfo? location,
        string mappingName,
        int depth)
    {
        if (targetType.CollectionKind != CollectionKind.Array &&
            targetType.CollectionKind != CollectionKind.List)
        {
            Fail(diagnostics, location, mappingName,
                "only array and List<T> members can be projected, not '" + targetType.DisplayName + "'");
            return null;
        }

        TypeModel? sourceElement = sourceType.Inner;
        TypeModel? targetElement = targetType.Inner;
        if (sourceElement is null || targetElement is null)
        {
            Fail(diagnostics, location, mappingName, "the collection element type is unknown");
            return null;
        }

        // Each nesting level gets its own lambda variable so a Select inside a
        // Select never shadows the outer one.
        string elementVariable = "x" + depth.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string? elementValue = BuildValue(
            sourceElement, targetElement, elementVariable,
            mapper, diagnostics, location, mappingName, depth + 1);
        if (elementValue is null)
        {
            return null;
        }

        // When the element needs no change at all, a Select would be pointless.
        string projected = elementValue == elementVariable
            ? sourceExpr
            : "global::System.Linq.Enumerable.Select(" + sourceExpr +
              ", " + elementVariable + " => " + elementValue + ")";

        return targetType.CollectionKind == CollectionKind.Array
            ? "global::System.Linq.Enumerable.ToArray(" + projected + ")"
            : "global::System.Linq.Enumerable.ToList(" + projected + ")";
    }

    /// <summary>
    /// Finds a value for a target member by convention: first a same-name source
    /// member, then a single unambiguous flatten path.
    /// </summary>
    private static void ResolveByConvention(
        TypeModel source,
        string targetName,
        string sourceExpr,
        MapperOptions options,
        out string? valueExpression,
        out TypeModel? valueType)
    {
        valueExpression = null;
        valueType = null;

        // A source member with the same name.
        MemberModel? sameName = source.FindMember(
            targetName, options.NameMatching == PropertyNameMatching.IgnoreCase);
        if (sameName is not null && sameName.CanRead)
        {
            valueExpression = sourceExpr + "." + sameName.Name;
            valueType = sameName.Type;
            return;
        }

        // A flatten path, but only when flattening is enabled and exactly one
        // path exists (an ambiguous one is left unmapped, like the imperative path).
        if (options.EnableFlattening)
        {
            List<List<MemberModel>> paths = FlatteningResolver.FindFlattenPaths(
                source, targetName, options.NameMatching);
            if (paths.Count == 1)
            {
                List<MemberModel> path = paths[0];
                valueExpression = sourceExpr + "." + string.Join(".", path.Select(p => p.Name));
                valueType = path[path.Count - 1].Type;
            }
        }
    }

    /// <summary>
    /// Picks the constructor a projection should use: a parameterless one when
    /// there is one, otherwise the smallest whose parameters all match a source
    /// member by name.
    /// </summary>
    private static ConstructorModel? ChooseConstructor(TypeModel target, TypeModel source)
    {
        var accessible = target.Constructors.Where(c => c.IsAccessible).ToList();

        ConstructorModel? parameterless = accessible.FirstOrDefault(c => c.Parameters.Count == 0);
        if (parameterless is not null)
        {
            return parameterless;
        }

        foreach (ConstructorModel candidate in accessible.OrderBy(c => c.Parameters.Count))
        {
            if (candidate.Parameters.All(p => source.FindMember(p.Name, ignoreCase: true) is not null))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves a source path string ("Customer.Name") into the C# expression
    /// that reads it and the type of the value at the end. Returns false if any
    /// step is missing.
    /// </summary>
    private static bool TryResolvePath(
        TypeModel source,
        string path,
        string sourceExpr,
        out string? expression,
        out TypeModel? finalType)
    {
        expression = null;
        finalType = null;

        TypeModel current = source;
        var segments = new List<string>();
        foreach (string segment in path.Split('.'))
        {
            MemberModel? member = current.FindMember(segment, ignoreCase: true);
            if (member is null || !member.CanRead)
            {
                return false;
            }

            segments.Add(member.Name);
            current = member.Type;
        }

        if (segments.Count == 0)
        {
            return false;
        }

        expression = sourceExpr + "." + string.Join(".", segments);
        finalType = current;
        return true;
    }

    /// <summary>Records a SYNTX023 "this mapping cannot be projected" diagnostic.</summary>
    private static void Fail(
        List<DiagnosticInfo> diagnostics, LocationInfo? location, string mappingName, string reason)
    {
        diagnostics.Add(DiagnosticInfo.Create(
            DiagnosticDescriptors.NotProjectable, location, mappingName, reason));
    }
}
