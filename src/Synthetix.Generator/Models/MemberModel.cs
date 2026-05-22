namespace Synthetix.Models;

/// <summary>
/// A plain, value-comparable snapshot of one property or field on a type.
/// </summary>
public sealed record MemberModel(
    string Name,
    TypeModel Type,
    // True when the member is a reference type marked as nullable ("string?").
    bool IsNullableReference,
    // True when the member has a getter we are allowed to read.
    bool CanRead,
    // True when the member has a setter we are allowed to write (set or init).
    bool CanWrite,
    // True when the only setter is an "init" setter.
    bool IsInitOnly,
    // True when the member is marked "required".
    bool IsRequired,
    // True for a field, false for a property. Only affects wording in messages.
    bool IsField)
{
    /// <summary>
    /// True when the member can be assigned with a normal "target.X = value;"
    /// statement after the object already exists. Init-only members cannot.
    /// </summary>
    public bool CanAssignAfterConstruction => CanWrite && !IsInitOnly;

    /// <summary>
    /// True when the member must be set inside the "new T { ... }" initializer:
    /// that is the only place an init-only or required member can be assigned.
    /// </summary>
    public bool MustAssignInInitializer => (IsInitOnly || IsRequired) && CanWrite;
}
