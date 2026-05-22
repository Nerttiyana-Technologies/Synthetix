namespace Synthetix.Models;

/// <summary>
/// The mapper-wide settings, taken from the [Mapper] attribute. Every value has
/// a sensible default, so a bare [Mapper] still works.
/// </summary>
public sealed record MapperOptions(
    bool RequireExplicitMapping,
    PropertyNameMatching NameMatching,
    bool EnableFlattening,
    MemberMappingSeverity UnmappedTargetMember,
    MemberMappingSeverity UnmappedSourceMember,
    PropertyNameMatching EnumNameMatching,
    NullHandling NullPathHandling,
    // True when opt-in string/primitive conversions are allowed (design doc 7.9).
    bool EnableStringConversions = false)
{
    /// <summary>The defaults documented in the design doc, section 4.2.</summary>
    public static MapperOptions Default => new(
        RequireExplicitMapping: false,
        NameMatching: PropertyNameMatching.Exact,
        EnableFlattening: true,
        UnmappedTargetMember: MemberMappingSeverity.Warn,
        UnmappedSourceMember: MemberMappingSeverity.Ignore,
        EnumNameMatching: PropertyNameMatching.Exact,
        NullPathHandling: NullHandling.Propagate,
        EnableStringConversions: false);
}

/// <summary>
/// One step of the type "nesting" around a mapper class. If a mapper is declared
/// inside another class, we need to re-open that outer class in the generated
/// file. This records just enough to do that.
/// </summary>
public sealed record ContainingType(string Keyword, string Name);

/// <summary>
/// A plain, value-comparable snapshot of one [Mapper] class and everything the
/// generator needs to know about it. Built in Stage 2.
/// </summary>
public sealed record MapperModel(
    string Namespace,
    string Name,
    // "class" or "struct".
    string TypeKeyword,
    // "public", "internal", and so on.
    string Accessibility,
    bool IsStatic,
    // False -> SYNTX001 (the mapper class must be partial).
    bool IsPartial,
    // The outer types this mapper is nested inside, outermost first. Usually empty.
    EquatableArray<ContainingType> ContainingTypes,
    // The base file name used for the generated source file.
    string HintName,
    MapperOptions Options,
    EquatableArray<MappingMethodModel> Methods,
    EquatableArray<UserMappingModel> UserMappings,
    // The names of every ordinary method on the mapper. Used to check that a
    // method named in a "Use = ..." setting actually exists (SYNTX015).
    EquatableArray<string> DeclaredMethodNames,
    // Diagnostics about the shape of the mapper found while building this model
    // in Stage 2 - for example SYNTX001 (not partial) or SYNTX016 (no methods).
    EquatableArray<DiagnosticInfo> StructuralDiagnostics,
    LocationInfo? Location);
