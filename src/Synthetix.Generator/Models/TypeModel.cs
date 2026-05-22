namespace Synthetix.Models;

using System;

/// <summary>
/// A plain, value-comparable snapshot of a .NET type.
/// </summary>
/// <remarks>
/// This is the generator's own copy of everything it needs to know about a type:
/// its name, what kind of type it is, and (for classes/structs/records) the
/// members and constructors it exposes. It is built once in Stage 2 from the
/// Roslyn semantic model, and from then on the generator only ever looks at
/// this copy - never at a live Roslyn symbol.
///
/// For nested types the model forms a tree: a complex type lists its members,
/// and each member carries its own <see cref="TypeModel"/>. The tree is always
/// finite - Stage 2 stops at simple types and breaks reference cycles.
/// </remarks>
public sealed record TypeModel(
    // The fully-qualified, "global::"-prefixed name. Safe to drop straight into
    // generated code, for example "global::MyApp.Order" or "int".
    string DisplayName,
    // A stable name used only to test whether two types are the same type.
    // It has no "global::" prefix and no reference-nullability "?" so that, for
    // example, "string" and "string?" share one identity.
    string IdentityKey,
    TypeCategory Category,
    bool IsReferenceType,
    bool IsValueType,
    // For a Nullable<T> this is T. For a collection this is the element type.
    // Null for every other kind of type.
    TypeModel? Inner,
    // The mappable members - only filled in for Complex types.
    EquatableArray<MemberModel> Members,
    // The constructors the generator could use - only for Complex types.
    EquatableArray<ConstructorModel> Constructors,
    // The member names of an enum - only for Enum types.
    EquatableArray<string> EnumMemberNames,
    // For a Collection type, which collection family it belongs to. For a
    // dictionary, Inner above holds the VALUE type and KeyType below holds the
    // key type. These default so older construction sites keep working.
    CollectionKind CollectionKind = CollectionKind.None,
    TypeModel? KeyType = null)
{
    /// <summary>True when this is a class/struct/record we can map member by member.</summary>
    public bool IsComplex => Category == TypeCategory.Complex;

    /// <summary>True when this is a collection (array, list, set, dictionary, immutable).</summary>
    public bool IsCollection => Category == TypeCategory.Collection;

    /// <summary>True when this is a dictionary-family collection.</summary>
    public bool IsDictionary =>
        CollectionKind == CollectionKind.Dictionary || CollectionKind == CollectionKind.ImmutableDictionary;

    /// <summary>True when this is a simple value that is just copied across.</summary>
    public bool IsSimple => Category == TypeCategory.Simple;

    /// <summary>True when this is an enum type.</summary>
    public bool IsEnum => Category == TypeCategory.Enum;

    /// <summary>True when this is a Nullable&lt;T&gt; value type such as int?.</summary>
    public bool IsNullableValueType => Category == TypeCategory.Nullable;

    /// <summary>
    /// Finds a readable/writable member by name, or returns null if there is none.
    /// </summary>
    public MemberModel? FindMember(string name, bool ignoreCase)
    {
        StringComparison comparison = ignoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (MemberModel member in Members)
        {
            if (string.Equals(member.Name, name, comparison))
            {
                return member;
            }
        }

        return null;
    }

    /// <summary>Builds a model for a leaf type (simple value, enum, or collection).</summary>
    public static TypeModel Leaf(
        string displayName,
        string identityKey,
        TypeCategory category,
        bool isReferenceType,
        TypeModel? inner = null,
        EquatableArray<string> enumMembers = default)
        => new(
            displayName,
            identityKey,
            category,
            isReferenceType,
            !isReferenceType,
            inner,
            EquatableArray<MemberModel>.Empty,
            EquatableArray<ConstructorModel>.Empty,
            enumMembers);
}

/// <summary>One constructor on a complex type that Synthetix might call.</summary>
public sealed record ConstructorModel(
    EquatableArray<ParameterModel> Parameters,
    bool IsAccessible);

/// <summary>One parameter of a constructor.</summary>
public sealed record ParameterModel(
    string Name,
    TypeModel Type,
    bool IsNullableReference,
    bool IsOptional);
