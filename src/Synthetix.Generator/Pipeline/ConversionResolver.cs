namespace Synthetix.Pipeline;

using System.Collections.Generic;
using Synthetix.Diagnostics;
using Synthetix.Models;

/// <summary>How one value was turned into another.</summary>
internal enum ConversionKind
{
    /// <summary>A plain assignment or a built-in conversion. No method was called.</summary>
    Direct,

    /// <summary>The value went through a user-written mapping method.</summary>
    UserMapping,

    /// <summary>The value went through another mapping method on the same mapper.</summary>
    NestedMapping,
}

/// <summary>The outcome of trying to convert one value into another.</summary>
internal sealed class ConversionResult
{
    private ConversionResult(string? expression, ConversionKind kind, DiagnosticInfo? diagnostic)
    {
        Expression = expression;
        Kind = kind;
        Diagnostic = diagnostic;
    }

    /// <summary>The C# expression that produces the converted value, or null if it failed.</summary>
    public string? Expression { get; }

    /// <summary>How the conversion was done.</summary>
    public ConversionKind Kind { get; }

    /// <summary>A warning (if it succeeded) or an error (if it failed). May be null.</summary>
    public DiagnosticInfo? Diagnostic { get; }

    /// <summary>True when a usable expression was produced.</summary>
    public bool Succeeded => Expression is not null;

    public static ConversionResult Ok(string expression, ConversionKind kind, DiagnosticInfo? warning = null)
        => new(expression, kind, warning);

    public static ConversionResult Fail(DiagnosticInfo error)
        => new(null, ConversionKind.Direct, error);
}

/// <summary>
/// Works out whether a value of one type can be assigned to a member of another
/// type, and if so, builds the exact C# expression that does it.
/// </summary>
/// <remarks>
/// This follows the order of preference from design doc 7.1:
/// identity / implicit conversion, then nullable adjustment, then a user-written
/// mapping, then a sibling mapping method. Anything left over is a real mismatch.
/// </remarks>
internal static class ConversionResolver
{
    /// <summary>
    /// Converts <paramref name="sourceExpression"/> (which has type
    /// <paramref name="sourceType"/>) into a value of <paramref name="targetType"/>.
    /// </summary>
    public static ConversionResult Convert(
        string sourceExpression,
        TypeModel sourceType,
        bool sourceCanBeNull,
        TypeModel targetType,
        bool targetAllowsNull,
        MapperModel mapper,
        LocationInfo? location,
        string sourceLabel,
        string targetLabel)
    {
        // --- 1. Exactly the same type ---
        if (sourceType.IdentityKey == targetType.IdentityKey)
        {
            // A nullable value flowing into a member that cannot be null is risky.
            if (sourceCanBeNull && !targetAllowsNull)
            {
                return ConversionResult.Ok(
                    sourceExpression,
                    ConversionKind.Direct,
                    NullableWarning(location, sourceLabel, targetLabel));
            }

            return ConversionResult.Ok(sourceExpression, ConversionKind.Direct);
        }

        // --- 2. Nullable value type adjustment (for example int? and int) ---
        ConversionResult? nullableAdjusted = TryNullableAdjust(
            sourceExpression, sourceType, targetType, mapper, location, sourceLabel, targetLabel);
        if (nullableAdjusted is not null)
        {
            return nullableAdjusted;
        }

        // --- 3. Built-in implicit numeric conversion (for example int to long) ---
        if (sourceType.Category == TypeCategory.Simple &&
            targetType.Category == TypeCategory.Simple &&
            HasImplicitNumericConversion(sourceType.IdentityKey, targetType.IdentityKey))
        {
            return ConversionResult.Ok(sourceExpression, ConversionKind.Direct);
        }

        // --- String <-> primitive conversions, when the mapper opted in (7.9) ---
        if (mapper.Options.EnableStringConversions)
        {
            string? stringConversion = TryStringConversion(sourceExpression, sourceType, targetType);
            if (stringConversion is not null)
            {
                return ConversionResult.Ok(stringConversion, ConversionKind.Direct);
            }
        }

        // --- 4. Anything can be assigned to object ---
        if (targetType.IdentityKey == "object")
        {
            return ConversionResult.Ok(sourceExpression, ConversionKind.Direct);
        }

        // --- 5. Enum to enum, matched by member name ---
        if (sourceType.Category == TypeCategory.Enum && targetType.Category == TypeCategory.Enum)
        {
            return BuildEnumConversion(sourceExpression, sourceType, targetType, mapper);
        }

        // --- 6. A user-written or sibling mapping method that fits ---
        string? methodCall = FindMappingMethod(
            sourceType, targetType, mapper, out ConversionKind methodKind, out bool methodIsAsync);
        if (methodCall is not null)
        {
            // An async user mapping returns a Task - its result must be awaited,
            // which only works inside an async mapping method (design doc 7.11).
            string call = methodCall + "(" + sourceExpression + ")";
            return ConversionResult.Ok(methodIsAsync ? "await " + call : call, methodKind);
        }

        // --- Nothing worked: this is a real type mismatch ---
        return ConversionResult.Fail(DiagnosticInfo.Create(
            DiagnosticDescriptors.TypeMismatch,
            location,
            sourceLabel,
            sourceType.DisplayName,
            targetLabel,
            targetType.DisplayName));
    }

    /// <summary>Handles the int / int? family of adjustments described in design doc 7.1.</summary>
    private static ConversionResult? TryNullableAdjust(
        string sourceExpression,
        TypeModel sourceType,
        TypeModel targetType,
        MapperModel mapper,
        LocationInfo? location,
        string sourceLabel,
        string targetLabel)
    {
        bool sourceIsNullable = sourceType.Category == TypeCategory.Nullable;
        bool targetIsNullable = targetType.Category == TypeCategory.Nullable;

        // Compare the "inside" types once the nullable wrapper is removed.
        TypeModel sourceCore = sourceIsNullable ? sourceType.Inner! : sourceType;
        TypeModel targetCore = targetIsNullable ? targetType.Inner! : targetType;

        bool coresMatch =
            sourceCore.IdentityKey == targetCore.IdentityKey ||
            HasImplicitNumericConversion(sourceCore.IdentityKey, targetCore.IdentityKey);

        if (!coresMatch || (!sourceIsNullable && !targetIsNullable))
        {
            // Either the inner types do not line up, or neither side is nullable
            // (so there is nothing here for this method to do).
            return null;
        }

        // T?  ->  T?   and   T  ->  T?   are always safe; the compiler lifts them.
        if (targetIsNullable)
        {
            return ConversionResult.Ok(sourceExpression, ConversionKind.Direct);
        }

        // T?  ->  T   needs a .Value access and can throw if the source is null.
        return ConversionResult.Ok(
            sourceExpression + ".Value",
            ConversionKind.Direct,
            NullableWarning(location, sourceLabel, targetLabel));
    }

    /// <summary>Builds a "source switch { ... }" expression that maps an enum by member name.</summary>
    private static ConversionResult BuildEnumConversion(
        string sourceExpression, TypeModel sourceType, TypeModel targetType, MapperModel mapper)
    {
        bool ignoreCase = mapper.Options.EnumNameMatching == PropertyNameMatching.IgnoreCase;
        var arms = new List<string>();

        foreach (string sourceMember in sourceType.EnumMemberNames)
        {
            string? targetMember = FindEnumMember(targetType, sourceMember, ignoreCase);
            if (targetMember is not null)
            {
                arms.Add(sourceType.DisplayName + "." + sourceMember +
                         " => " + targetType.DisplayName + "." + targetMember);
            }
        }

        // A member with no counterpart on the other enum throws at runtime.
        arms.Add("_ => throw new global::System.ArgumentOutOfRangeException(nameof(" + sourceExpression + "))");

        string expression = sourceExpression + " switch { " + string.Join(", ", arms) + " }";
        return ConversionResult.Ok(expression, ConversionKind.Direct);
    }

    private static string? FindEnumMember(TypeModel enumType, string name, bool ignoreCase)
    {
        foreach (string member in enumType.EnumMemberNames)
        {
            if (string.Equals(member, name,
                ignoreCase ? System.StringComparison.OrdinalIgnoreCase : System.StringComparison.Ordinal))
            {
                return member;
            }
        }

        return null;
    }

    /// <summary>
    /// Looks for a mapping method - either user-written or another method on the
    /// same mapper - that turns the source type into the target type.
    /// </summary>
    private static string? FindMappingMethod(
        TypeModel sourceType, TypeModel targetType, MapperModel mapper,
        out ConversionKind kind, out bool isAsync)
    {
        // First preference: a method the developer wrote themselves. An async
        // user mapping is matched on its unwrapped (Task<T> -> T) result type.
        foreach (UserMappingModel user in mapper.UserMappings)
        {
            if (!user.IsIgnored &&
                user.ParameterTypeKey == sourceType.IdentityKey &&
                user.ReturnTypeKey == targetType.IdentityKey)
            {
                kind = ConversionKind.UserMapping;
                isAsync = user.IsAsync;
                return user.MethodName;
            }
        }

        // Second preference: another partial mapping method on this mapper.
        // A sibling that is itself async returns a Task, so its result must be
        // awaited just like an async user mapping (design doc 7.11).
        foreach (MappingMethodModel sibling in mapper.Methods)
        {
            if (sibling.SourceType.IdentityKey == sourceType.IdentityKey &&
                sibling.TargetType.IdentityKey == targetType.IdentityKey)
            {
                kind = ConversionKind.NestedMapping;
                isAsync = sibling.IsAsyncMethod;
                return sibling.MethodName;
            }
        }

        kind = ConversionKind.Direct;
        isAsync = false;
        return null;
    }

    /// <summary>Builds the standard SYNTX012 "nullable to non-nullable" warning.</summary>
    private static DiagnosticInfo NullableWarning(LocationInfo? location, string sourceLabel, string targetLabel)
        => DiagnosticInfo.Create(
            DiagnosticDescriptors.NullableToNonNullable, location, sourceLabel, targetLabel);

    // The invariant culture, so a conversion behaves the same on every machine.
    private const string Invariant = "global::System.Globalization.CultureInfo.InvariantCulture";

    /// <summary>
    /// Builds a string-to-primitive or primitive-to-string conversion expression,
    /// or returns null when the pair is not a string conversion (design doc 7.9).
    /// </summary>
    private static string? TryStringConversion(string expression, TypeModel source, TypeModel target)
    {
        bool sourceIsString = source.IdentityKey == "string";
        bool targetIsString = target.IdentityKey == "string";

        // primitive -> string
        if (targetIsString && !sourceIsString)
        {
            bool nullable = source.Category == TypeCategory.Nullable;
            TypeModel core = nullable ? source.Inner! : source;
            string access = nullable ? "?." : ".";

            if (IsCultureFormatted(core.IdentityKey))
            {
                return expression + access + "ToString(" + Invariant + ")";
            }

            if (IsPlainParsed(core.IdentityKey) || core.Category == TypeCategory.Enum)
            {
                return expression + access + "ToString()";
            }

            return null;
        }

        // string -> primitive
        if (sourceIsString && !targetIsString)
        {
            bool nullable = target.Category == TypeCategory.Nullable;
            TypeModel core = nullable ? target.Inner! : target;

            string? parsed = null;
            if (IsCultureFormatted(core.IdentityKey))
            {
                parsed = core.IdentityKey + ".Parse(" + expression + ", " + Invariant + ")";
            }
            else if (IsPlainParsed(core.IdentityKey))
            {
                parsed = core.IdentityKey + ".Parse(" + expression + ")";
            }
            else if (core.Category == TypeCategory.Enum)
            {
                parsed = "global::System.Enum.Parse<" + core.DisplayName + ">(" + expression + ")";
            }

            if (parsed is null)
            {
                return null;
            }

            // A nullable target tolerates a null string; a non-nullable one parses
            // directly and throws on bad input, as design doc 7.9 specifies.
            return nullable
                ? "(" + expression + " is null ? (" + target.DisplayName + ")null : " + parsed + ")"
                : parsed;
        }

        return null;
    }

    /// <summary>Number and date/time types whose parsing and formatting take a culture.</summary>
    private static bool IsCultureFormatted(string identityKey) => identityKey is
        "int" or "long" or "short" or "byte" or "sbyte" or "uint" or "ulong" or "ushort" or
        "decimal" or "double" or "float" or
        "global::System.DateTime" or "global::System.DateTimeOffset" or "global::System.TimeSpan";

    /// <summary>Types parsed without a culture argument.</summary>
    private static bool IsPlainParsed(string identityKey) => identityKey is
        "bool" or "char" or "global::System.Guid";

    /// <summary>
    /// True when C# allows an implicit conversion from one number type to another
    /// (for example int to long). This is the list from the C# language spec.
    /// </summary>
    internal static bool HasImplicitNumericConversion(string from, string to)
    {
        if (!Widenings.TryGetValue(from, out HashSet<string>? targets))
        {
            return false;
        }

        return targets.Contains(to);
    }

    // For each number type, the set of number types it widens into without a cast.
    private static readonly Dictionary<string, HashSet<string>> Widenings = new()
    {
        ["sbyte"] = new HashSet<string> { "short", "int", "long", "float", "double", "decimal" },
        ["byte"] = new HashSet<string> { "short", "ushort", "int", "uint", "long", "ulong", "float", "double", "decimal" },
        ["short"] = new HashSet<string> { "int", "long", "float", "double", "decimal" },
        ["ushort"] = new HashSet<string> { "int", "uint", "long", "ulong", "float", "double", "decimal" },
        ["int"] = new HashSet<string> { "long", "float", "double", "decimal" },
        ["uint"] = new HashSet<string> { "long", "ulong", "float", "double", "decimal" },
        ["long"] = new HashSet<string> { "float", "double", "decimal" },
        ["ulong"] = new HashSet<string> { "float", "double", "decimal" },
        ["char"] = new HashSet<string> { "ushort", "int", "uint", "long", "ulong", "float", "double", "decimal" },
        ["float"] = new HashSet<string> { "double" },
    };
}
