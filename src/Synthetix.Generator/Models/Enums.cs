namespace Synthetix.Models;

// This file holds the small enums the generator uses internally while it works
// out a mapping. They are separate from the enums that get injected into the
// user's project (those live in the injected attribute source).

/// <summary>How property names are compared when looking for a same-name match.</summary>
public enum PropertyNameMatching
{
    /// <summary>Names must match exactly, including upper/lower case.</summary>
    Exact = 0,

    /// <summary>Names match even if the upper/lower case is different.</summary>
    IgnoreCase = 1,
}

/// <summary>How loudly the generator complains about an unmapped member.</summary>
public enum MemberMappingSeverity
{
    /// <summary>Report it as a build error.</summary>
    Error = 0,

    /// <summary>Report it as a build warning.</summary>
    Warn = 1,

    /// <summary>Say nothing.</summary>
    Ignore = 2,
}

/// <summary>
/// What the generated code does when a step along a nested path is null
/// (used by flattening and unflattening).
/// </summary>
public enum NullHandling
{
    /// <summary>Let the null flow through, so the target member becomes null.</summary>
    Propagate = 0,

    /// <summary>Throw an exception that names the null step.</summary>
    Throw = 1,

    /// <summary>Use the default value for the target member's type.</summary>
    UseDefault = 2,
}

/// <summary>A rough grouping of a type, so the generator knows how to handle it.</summary>
public enum TypeCategory
{
    /// <summary>A simple value such as int, string, decimal, DateTime, Guid. Copied directly.</summary>
    Simple = 0,

    /// <summary>An enum type. Mapped to another enum by member name.</summary>
    Enum = 1,

    /// <summary>A class, struct, or record we can look inside and map member by member.</summary>
    Complex = 2,

    /// <summary>An array or generic collection. In v0.1 a same-typed collection is copied by reference.</summary>
    Collection = 3,

    /// <summary>A Nullable&lt;T&gt; value type, such as int?.</summary>
    Nullable = 4,

    /// <summary>Something the generator does not recognise.</summary>
    Unknown = 5,
}

/// <summary>Which member-level attribute a piece of configuration came from.</summary>
public enum ConfigKind
{
    /// <summary>[MapProperty] - rename, explicit path, or a Use converter.</summary>
    MapProperty = 0,

    /// <summary>[MapperIgnoreTarget] - leave a target member unset.</summary>
    IgnoreTarget = 1,

    /// <summary>[MapperIgnoreSource] - mark a source member as intentionally unused.</summary>
    IgnoreSource = 2,

    /// <summary>[MapValue] - assign a constant or method-supplied value.</summary>
    MapValue = 3,
}

/// <summary>
/// The rule that decided how one target member got its value. This is what the
/// mapping manifest shows in its "Rule" column.
/// </summary>
public enum MappingRuleKind
{
    /// <summary>Source and target had the same name.</summary>
    SameName = 0,

    /// <summary>A nested source path was flattened into a flat target name.</summary>
    FlattenPath = 1,

    /// <summary>A flat source name was unflattened into a nested target path.</summary>
    UnflattenPath = 2,

    /// <summary>An explicit [MapProperty] rename or path.</summary>
    MapProperty = 3,

    /// <summary>A [MapProperty] that routes the value through a Use converter.</summary>
    Converter = 4,

    /// <summary>A [MapValue] constant.</summary>
    ConstantValue = 5,

    /// <summary>A [MapValue] that calls a method.</summary>
    ValueMethod = 6,

    /// <summary>A user-written mapping method picked up automatically.</summary>
    UserMapping = 7,

    /// <summary>A nested complex member mapped by another mapping method.</summary>
    NestedMapping = 8,

    /// <summary>The value was passed to the target type's constructor.</summary>
    ConstructorParameter = 9,

    /// <summary>The members were copied element by element into a new collection.</summary>
    CollectionMapping = 10,

    /// <summary>The mapping dispatched on the runtime type to a derived mapping.</summary>
    PolymorphicDispatch = 11,
}

/// <summary>
/// The family a collection type belongs to. This decides how the generated code
/// constructs the target collection.
/// </summary>
public enum CollectionKind
{
    /// <summary>Not a collection.</summary>
    None = 0,

    /// <summary>A plain array, T[].</summary>
    Array = 1,

    /// <summary>List&lt;T&gt; and the sequence interfaces (IEnumerable, IList, ICollection, IReadOnly*).</summary>
    List = 2,

    /// <summary>HashSet&lt;T&gt;, ISet&lt;T&gt;, IReadOnlySet&lt;T&gt;.</summary>
    Set = 3,

    /// <summary>Dictionary&lt;TKey, TValue&gt; and the dictionary interfaces.</summary>
    Dictionary = 4,

    /// <summary>System.Collections.Immutable.ImmutableArray&lt;T&gt;.</summary>
    ImmutableArray = 5,

    /// <summary>ImmutableList&lt;T&gt; / IImmutableList&lt;T&gt;.</summary>
    ImmutableList = 6,

    /// <summary>ImmutableHashSet&lt;T&gt; / IImmutableSet&lt;T&gt;.</summary>
    ImmutableHashSet = 7,

    /// <summary>ImmutableDictionary&lt;TKey, TValue&gt; / IImmutableDictionary&lt;TKey, TValue&gt;.</summary>
    ImmutableDictionary = 8,
}
