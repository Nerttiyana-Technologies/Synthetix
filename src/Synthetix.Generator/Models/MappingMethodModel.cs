namespace Synthetix.Models;

/// <summary>
/// One piece of member-level configuration, taken from a [MapProperty],
/// [MapperIgnoreTarget], [MapperIgnoreSource], or [MapValue] attribute that sits
/// on a mapping method.
/// </summary>
public sealed record MemberConfig(
    ConfigKind Kind,
    // The source member name or dotted path. Null when the attribute has no source.
    string? Source,
    // The target member name. Null when the attribute has no target.
    string? Target,
    // The name of a converter / value method, from "Use = ...". Null if unused.
    string? Use,
    // The C# text of a constant value, from [MapValue(Value = ...)]. Null if unused.
    string? ConstantExpression,
    // True when [MapValue] supplied a constant (as opposed to a Use method).
    bool HasConstant,
    LocationInfo? Location);

/// <summary>
/// A plain, value-comparable snapshot of one partial mapping method - the thing
/// the generator has to write a body for.
/// </summary>
public sealed record MappingMethodModel(
    string MethodName,
    string Accessibility,
    bool IsStatic,
    // False -> SYNTX002 (a mapping method must be partial and have no body).
    bool IsPartialNoBody,
    // The name of the single parameter, for example "order".
    string SourceParameterName,
    TypeModel SourceType,
    TypeModel TargetType,
    // The parameter's type, written out for the method signature. Includes the
    // nullable "?" so the generated signature matches the one the user declared.
    string SourceParameterTypeDisplay,
    // The return type exactly as written, used when emitting the method signature.
    string ReturnTypeDisplay,
    EquatableArray<MemberConfig> Configurations,
    LocationInfo? Location,
    // The [MapDerivedType] entries on this method. Empty for a normal mapping.
    EquatableArray<DerivedTypeMapping> DerivedTypes = default,
    // Every concrete type in the compilation that derives from this method's
    // source type. Used for the SYNTX020 exhaustiveness check. Only filled in
    // when the method has [MapDerivedType] entries.
    EquatableArray<DerivedTypeRef> DiscoveredDerivedTypes = default,
    // True for a void Update(source, target) method that fills an existing
    // instance instead of creating one (design doc 7.8).
    bool IsUpdate = false,
    // The name of the target parameter, for an update method only.
    string TargetParameterName = "",
    // True when the method returns Task<T> or ValueTask<T> (design doc 7.11).
    bool IsAsyncMethod = false,
    // True when the async method returns ValueTask<T> rather than Task<T>.
    bool ReturnsValueTask = false,
    // The name of a trailing CancellationToken parameter, or "" if there is none.
    string CancellationTokenName = "",
    // True when this "method" is really an IQueryable projection property -
    // a partial property of type Expression<Func<TSource, TTarget>> whose body
    // the generator fills with an object-initializer lambda (design doc 7.10).
    bool IsProjection = false)
{
    /// <summary>True when this method dispatches polymorphically on the runtime type.</summary>
    public bool IsPolymorphic => DerivedTypes.Count > 0;
}

/// <summary>One [MapDerivedType] entry: a derived source type and the derived target it maps to.</summary>
public sealed record DerivedTypeMapping(
    string SourceTypeKey,
    string SourceTypeDisplay,
    string TargetTypeKey,
    string TargetTypeDisplay,
    LocationInfo? Location);

/// <summary>A concrete type discovered to derive from a polymorphic mapping's source type.</summary>
public sealed record DerivedTypeRef(string TypeKey, string DisplayName);

/// <summary>
/// A user-written (non-partial) method inside the mapper class that the
/// generator can call to convert one type into another - see design doc 4.4.
/// </summary>
public sealed record UserMappingModel(
    string MethodName,
    bool IsStatic,
    // Identity key of the single parameter's type (what it converts FROM).
    string ParameterTypeKey,
    string ParameterTypeDisplay,
    // Identity key of the return type (what it converts TO).
    string ReturnTypeKey,
    string ReturnTypeDisplay,
    // True when the method carries an explicit [UserMapping] attribute.
    bool IsExplicitlyMarked,
    // True when the method carries [UserMapping(Ignore = true)].
    bool IsIgnored,
    LocationInfo? Location,
    // True when the user method returns Task<T> or ValueTask<T> - its result
    // must be awaited, so only an async mapping method can use it (7.11).
    bool IsAsync = false);
