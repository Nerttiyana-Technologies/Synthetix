namespace Synthetix.Diagnostics;

using Microsoft.CodeAnalysis;

/// <summary>
/// The full catalog of diagnostics Synthetix can report - SYNTX001 through
/// SYNTX017, exactly as listed in the design doc section 11.
/// </summary>
/// <remarks>
/// Every diagnostic has a permanent id. Ids are never reused or renumbered, so a
/// developer's .editorconfig rules and "#pragma warning" suppressions keep
/// working across versions. The list here is mirrored in the
/// AnalyzerReleases.Unshipped.md file, and Roslyn checks the two stay in sync.
/// </remarks>
internal static class DiagnosticDescriptors
{
    // One category for the whole library, matching AnalyzerReleases.Unshipped.md.
    private const string Category = "Synthetix";

    // Each diagnostic links to its own docs page.
    private const string HelpBase =
        "https://github.com/isureshsubramanian/Synthetix/blob/main/docs/diagnostics/";

    /// <summary>Builds one descriptor. Keeps the list below short and consistent.</summary>
    private static DiagnosticDescriptor Make(
        string id,
        string title,
        string messageFormat,
        DiagnosticSeverity severity)
        => new(
            id: id,
            title: title,
            messageFormat: messageFormat,
            category: Category,
            defaultSeverity: severity,
            isEnabledByDefault: true,
            description: title,
            helpLinkUri: HelpBase + id + ".md");

    // SYNTX001 - the mapper class is missing the "partial" keyword, so the
    // generator has nowhere to add the generated method bodies.
    public static readonly DiagnosticDescriptor MapperMustBePartial = Make(
        "SYNTX001",
        "Mapper class must be partial",
        "Mapper class '{0}' must be declared 'partial' so Synthetix can add the generated mapping code",
        DiagnosticSeverity.Error);

    // SYNTX002 - a mapping method is not "partial", or it already has a body.
    public static readonly DiagnosticDescriptor MappingMethodMustBePartial = Make(
        "SYNTX002",
        "Mapping method must be partial",
        "Mapping method '{0}' must be declared 'partial' and have no body",
        DiagnosticSeverity.Error);

    // SYNTX003 - the two types simply cannot be connected at all.
    public static readonly DiagnosticDescriptor NoMappingPossible = Make(
        "SYNTX003",
        "No mapping could be created",
        "No mapping could be created from '{0}' to '{1}'",
        DiagnosticSeverity.Error);

    // SYNTX004 - a target member has no source feeding it. Severity is
    // configurable through [Mapper(UnmappedTargetMember = ...)].
    public static readonly DiagnosticDescriptor UnmappedTargetMember = Make(
        "SYNTX004",
        "Target member has no source",
        "Target member '{0}' on '{1}' has no corresponding source member",
        DiagnosticSeverity.Warning);

    // SYNTX005 - a source member is never read. Off by default; turned on with
    // [Mapper(UnmappedSourceMember = ...)].
    public static readonly DiagnosticDescriptor UnusedSourceMember = Make(
        "SYNTX005",
        "Source member is unused",
        "Source member '{0}' on '{1}' is not used by any mapping",
        DiagnosticSeverity.Info);

    // SYNTX006 - a [MapProperty]/[MapperIgnoreTarget] names a target that is not there.
    public static readonly DiagnosticDescriptor TargetMemberNotFound = Make(
        "SYNTX006",
        "Target member not found",
        "Configuration references target member '{0}', which does not exist on '{1}'",
        DiagnosticSeverity.Error);

    // SYNTX007 - a [MapProperty] names a source path that cannot be walked.
    public static readonly DiagnosticDescriptor SourcePathNotFound = Make(
        "SYNTX007",
        "Source path not found",
        "Configuration references source path '{0}', which cannot be resolved on '{1}'",
        DiagnosticSeverity.Error);

    // SYNTX008 - two different source paths both fold down to the same target name.
    public static readonly DiagnosticDescriptor AmbiguousFlattening = Make(
        "SYNTX008",
        "Ambiguous flattening",
        "Ambiguous flattening: more than one source path resolves to target member '{0}'. Add an explicit [MapProperty] to choose one.",
        DiagnosticSeverity.Error);

    // SYNTX009 - the type graph loops back on itself and no handling was chosen.
    public static readonly DiagnosticDescriptor CircularReference = Make(
        "SYNTX009",
        "Circular reference detected",
        "A circular reference was detected while mapping '{0}', and there is no configured way to handle it",
        DiagnosticSeverity.Error);

    // SYNTX010 - the source and target member types do not line up.
    public static readonly DiagnosticDescriptor TypeMismatch = Make(
        "SYNTX010",
        "Member type mismatch",
        "Cannot map source member '{0}' of type '{1}' to target member '{2}' of type '{3}': there is no implicit conversion and no 'Use' converter was supplied",
        DiagnosticSeverity.Error);

    // SYNTX011 - the target type cannot be constructed by Synthetix.
    public static readonly DiagnosticDescriptor NoUsableConstructor = Make(
        "SYNTX011",
        "No usable constructor",
        "Target type '{0}' has no accessible constructor that Synthetix can satisfy",
        DiagnosticSeverity.Error);

    // SYNTX012 - a nullable value is being put into a member that cannot be null.
    public static readonly DiagnosticDescriptor NullableToNonNullable = Make(
        "SYNTX012",
        "Nullable mapped to non-nullable",
        "Nullable source member '{0}' is mapped to non-nullable target member '{1}'; this can fail at runtime if the source is null",
        DiagnosticSeverity.Warning);

    // SYNTX013 - strict mode is on and a target member was left unaccounted for.
    public static readonly DiagnosticDescriptor ExplicitMappingRequired = Make(
        "SYNTX013",
        "Explicit mapping required",
        "RequireExplicitMapping is enabled, but target member '{0}' on '{1}' is neither mapped nor ignored",
        DiagnosticSeverity.Error);

    // SYNTX014 - two attributes of equal precedence both try to set one member.
    public static readonly DiagnosticDescriptor ConflictingConfiguration = Make(
        "SYNTX014",
        "Conflicting configuration",
        "Conflicting configuration: target member '{0}' is configured by more than one attribute of equal precedence",
        DiagnosticSeverity.Error);

    // SYNTX015 - a named converter / user-mapping method is missing or wrong.
    public static readonly DiagnosticDescriptor ConverterMethodNotFound = Make(
        "SYNTX015",
        "Converter method not found",
        "Method '{0}' referenced by 'Use' or [UserMapping] was not found, or its signature does not accept '{1}' and return '{2}'",
        DiagnosticSeverity.Error);

    // SYNTX016 - a [Mapper] class with no mapping methods is almost always a mistake.
    public static readonly DiagnosticDescriptor EmptyMapper = Make(
        "SYNTX016",
        "Mapper has no mapping methods",
        "Mapper class '{0}' declares no partial mapping methods; add at least one, or remove the [Mapper] attribute",
        DiagnosticSeverity.Error);

    // SYNTX017 - the current mapping no longer matches the committed manifest.
    public static readonly DiagnosticDescriptor ManifestDrift = Make(
        "SYNTX017",
        "Mapping has drifted from the manifest",
        "Mapping '{0}' has drifted from the committed manifest: {1}. Run 'dotnet build -t:SynthetixUpdateManifest' to refresh it.",
        DiagnosticSeverity.Warning);

    // SYNTX018 - a collection's element type cannot be converted.
    public static readonly DiagnosticDescriptor CollectionElementNotMappable = Make(
        "SYNTX018",
        "Collection element cannot be mapped",
        "Cannot map collection member '{0}': there is no conversion from element type '{1}' to '{2}'",
        DiagnosticSeverity.Error);

    // SYNTX019 - the target collection type is one Synthetix cannot build.
    public static readonly DiagnosticDescriptor UnsupportedCollectionType = Make(
        "SYNTX019",
        "Unsupported collection type",
        "Synthetix cannot construct the target collection type '{0}'; supply a 'Use' converter or a user mapping instead",
        DiagnosticSeverity.Error);

    // SYNTX020 - a polymorphic mapping does not cover every derived type.
    public static readonly DiagnosticDescriptor NonExhaustivePolymorphicMapping = Make(
        "SYNTX020",
        "Polymorphic mapping is not exhaustive",
        "Polymorphic mapping '{0}' has no [MapDerivedType] entry for derived type '{1}'; add one so every subtype is covered",
        DiagnosticSeverity.Error);

    // SYNTX021 - a [MapDerivedType] entry is invalid or cannot be mapped.
    public static readonly DiagnosticDescriptor InvalidDerivedTypeMapping = Make(
        "SYNTX021",
        "Invalid derived-type mapping",
        "[MapDerivedType] on '{0}' is invalid: {1}",
        DiagnosticSeverity.Error);

    // SYNTX022 - a member cannot be set on an object that already exists.
    public static readonly DiagnosticDescriptor MemberNotUpdatable = Make(
        "SYNTX022",
        "Member cannot be updated",
        "Member '{0}' cannot be updated on an existing instance because it is init-only or required; it is left unchanged",
        DiagnosticSeverity.Warning);

    // SYNTX023 - a mapping cannot be turned into an expression-tree projection.
    public static readonly DiagnosticDescriptor NotProjectable = Make(
        "SYNTX023",
        "Mapping is not projectable",
        "Mapping '{0}' cannot be expressed as an IQueryable projection: {1}",
        DiagnosticSeverity.Error);

    // SYNTX024 - an async user mapping is needed by a synchronous method.
    public static readonly DiagnosticDescriptor AsyncMappingInSyncMethod = Make(
        "SYNTX024",
        "Async mapping in a synchronous method",
        "Mapping method '{0}' is synchronous but needs an async mapping. Make the method return Task<T> or ValueTask<T>.",
        DiagnosticSeverity.Error);
}
