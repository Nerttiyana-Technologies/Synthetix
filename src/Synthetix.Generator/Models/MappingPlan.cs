namespace Synthetix.Models;

/// <summary>
/// One finished decision: a target member, and the C# expression that fills it.
/// </summary>
/// <remarks>
/// Stage 3 (the plan resolver) does all the thinking and writes the result here
/// as a ready-to-use string. Stage 4 (the emitter) does no thinking at all - it
/// just drops <see cref="ValueExpression"/> into the generated code. Keeping the
/// thinking and the writing apart is what makes both stages easy to test.
/// </remarks>
public sealed record Assignment(
    // The target member (or constructor parameter) being filled.
    string TargetName,
    // The exact C# expression that produces the value, for example
    // "order.Id" or "order.Customer?.Name" or "FormatMoney(order.Total)".
    string ValueExpression,
    // Which rule decided this assignment - shown in the manifest.
    MappingRuleKind Rule,
    // A short human-readable description of where the value came from,
    // shown in the manifest "Source" column.
    string SourceDescription);

/// <summary>A target or source member that was deliberately left out, and why.</summary>
public sealed record IgnoredMember(string Name, string Reason);

/// <summary>
/// The finished plan for one mapping method: how to build the target object and
/// every value that goes into it.
/// </summary>
public sealed record MethodPlan(
    string MethodName,
    string Accessibility,
    bool IsStatic,
    string ReturnTypeDisplay,
    string SourceTypeDisplay,
    string SourceParameterName,
    string TargetTypeDisplay,
    // True when a real mapping body could be built. False means something was
    // wrong (an error diagnostic was raised) and the emitter writes a stub that
    // throws, so the only error the developer sees is the clear SYNTX one.
    bool HasValidBody,
    // Values passed straight to the target's constructor.
    EquatableArray<Assignment> ConstructorArguments,
    // Values set inside the "new T { ... }" initializer (init-only / required).
    EquatableArray<Assignment> InitializerAssignments,
    // Values set with "target.X = ...;" after the object exists.
    EquatableArray<Assignment> PostAssignments,
    EquatableArray<IgnoredMember> IgnoredTargets,
    EquatableArray<IgnoredMember> IgnoredSources,
    // Coverage counts for the manifest: how many target members were mapped,
    // how many were ignored, and how many were left unmapped.
    int CoverageMapped,
    int CoverageIgnored,
    int CoverageUnmapped,
    EquatableArray<DiagnosticInfo> Diagnostics,
    // When true, this method is a polymorphic dispatcher: its body is a switch
    // over the runtime type, built from DerivedDispatches below, and the
    // assignment lists above are unused.
    bool IsPolymorphic = false,
    EquatableArray<DerivedDispatch> DerivedDispatches = default,
    // True for a void Update(source, target) method. The body assigns onto the
    // existing target parameter instead of constructing a new object (7.8).
    bool IsUpdate = false,
    // The name of the target parameter, for an update method only.
    string TargetParameterName = "",
    // True when this method returns Task<T> or ValueTask<T> (design doc 7.11).
    bool IsAsyncMethod = false,
    // True when the async return type is ValueTask<T> rather than Task<T>.
    bool ReturnsValueTask = false,
    // True when the body actually awaits something, so it needs the `async` keyword.
    bool UsesAwait = false,
    // The name of a trailing CancellationToken parameter, or "" if there is none.
    string CancellationTokenName = "",
    // True when this is an IQueryable projection property (design doc 7.10). Its
    // body is a single object-initializer lambda held in ProjectionBody below,
    // and the assignment lists above are unused.
    bool IsProjection = false,
    // The C# text of the projection lambda's body - a "new T { ... }" expression
    // - or "" when this is not a projection. Used only by the emitter.
    string ProjectionBody = "")
{
    /// <summary>Target members that needed a value (mapped ones plus unmapped ones).</summary>
    public int CoverageRequired => CoverageMapped + CoverageUnmapped;
}

/// <summary>One arm of a polymorphic dispatch: a runtime type and the mapping to call.</summary>
public sealed record DerivedDispatch(string SourceTypeDisplay, string MappingMethodName);

/// <summary>
/// A generated helper method that maps one collection into another, element by
/// element. The emitter turns each of these into a private method on the mapper.
/// </summary>
public sealed record CollectionPlan(
    string MethodName,
    // The source collection type - the helper's parameter type.
    string SourceTypeDisplay,
    // The target collection type - the helper's return type.
    string TargetTypeDisplay,
    CollectionKind TargetKind,
    // The concrete type the helper actually constructs (for example List<T> even
    // when the declared target is IList<T>).
    string ConcreteTargetTypeDisplay,
    // The element type (for a dictionary, the value type).
    string ElementTypeDisplay,
    bool IsDictionary,
    // The key type, for a dictionary helper. Null otherwise.
    string? KeyTypeDisplay,
    // The C# expression that converts one element. It refers to a variable named
    // "element" (or "pair.Value" for a dictionary).
    string ElementConversion,
    // The C# expression that converts a dictionary key. Refers to "pair.Key".
    string? KeyConversion);

/// <summary>
/// The finished plan for one whole [Mapper] class: every method plan plus any
/// mapper-wide diagnostics. This is the value Stage 4 turns into source code.
/// </summary>
public sealed record MapperPlan(
    string Namespace,
    string Name,
    string TypeKeyword,
    string Accessibility,
    bool IsStatic,
    EquatableArray<ContainingType> ContainingTypes,
    string HintName,
    // False when the mapper class itself cannot be used (for example, it is not
    // partial). The emitter then reports diagnostics only and writes no code.
    bool CanEmit,
    EquatableArray<MethodPlan> Methods,
    // Diagnostics about the mapper as a whole: SYNTX001, SYNTX016, and so on.
    EquatableArray<DiagnosticInfo> MapperDiagnostics,
    // Where the mapper class is declared - used to point the drift diagnostic
    // (SYNTX017) at the right place.
    LocationInfo? Location,
    // The collection helper methods this mapper needs, de-duplicated across all
    // of its mapping methods. The emitter writes one private method for each.
    EquatableArray<CollectionPlan> CollectionHelpers = default);
