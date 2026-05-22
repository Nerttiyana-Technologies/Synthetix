namespace Synthetix;

using System;

// These are the PUBLIC versions of the Synthetix attributes.
//
// They are identical in shape to the attributes the generator injects into a
// project (see InjectedAttributeSource in Synthetix.Generator). The only
// difference is accessibility: these are public, so they can be referenced
// across project boundaries. Most projects should use the injected, internal
// attributes instead - see design doc section 10.2.

/// <summary>How property names are compared during same-name matching.</summary>
public enum PropertyNameMatching
{
    /// <summary>Names must match exactly, including upper/lower case.</summary>
    Exact = 0,

    /// <summary>Names match even when the upper/lower case is different.</summary>
    IgnoreCase = 1,
}

/// <summary>How loudly the generator reports an unmapped member.</summary>
public enum MemberMappingSeverity
{
    /// <summary>Report it as a build error.</summary>
    Error = 0,

    /// <summary>Report it as a build warning.</summary>
    Warn = 1,

    /// <summary>Say nothing about it.</summary>
    Ignore = 2,
}

/// <summary>What generated code does when a step on a nested path is null.</summary>
public enum NullHandling
{
    /// <summary>Let the null flow through, so the target member becomes null.</summary>
    Propagate = 0,

    /// <summary>Throw an exception that names the null step.</summary>
    Throw = 1,

    /// <summary>Use the default value for the target member's type.</summary>
    UseDefault = 2,
}

/// <summary>Marks a partial class or struct as a Synthetix mapper.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class MapperAttribute : Attribute
{
    /// <summary>When true, every target member must be explicitly mapped or ignored.</summary>
    public bool RequireExplicitMapping { get; set; }

    /// <summary>How source and target property names are compared.</summary>
    public PropertyNameMatching NameMatching { get; set; } = PropertyNameMatching.Exact;

    /// <summary>Whether the flattening / unflattening convention is on.</summary>
    public bool EnableFlattening { get; set; } = true;

    /// <summary>Severity for a target member that has no source (SYNTX004).</summary>
    public MemberMappingSeverity UnmappedTargetMember { get; set; } = MemberMappingSeverity.Warn;

    /// <summary>Severity for a source member that is never used (SYNTX005).</summary>
    public MemberMappingSeverity UnmappedSourceMember { get; set; } = MemberMappingSeverity.Ignore;

    /// <summary>How enum member names are compared when mapping enum to enum.</summary>
    public PropertyNameMatching EnumNameMatching { get; set; } = PropertyNameMatching.Exact;

    /// <summary>How a null along a flatten / unflatten path is handled.</summary>
    public NullHandling NullPathHandling { get; set; } = NullHandling.Propagate;

    /// <summary>When true, allows opt-in conversions between string and primitive types.</summary>
    public bool EnableStringConversions { get; set; }
}

/// <summary>Maps one source member to one target member (rename, path, or converter).</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class MapPropertyAttribute : Attribute
{
    /// <param name="source">The source member name, or a dotted path such as "Customer.Name".</param>
    /// <param name="target">The target member name.</param>
    public MapPropertyAttribute(string source, string target)
    {
        Source = source;
        Target = target;
    }

    /// <summary>The source member name or dotted path.</summary>
    public string Source { get; }

    /// <summary>The target member name.</summary>
    public string Target { get; }

    /// <summary>Optional. The name of a method that converts the source value.</summary>
    public string? Use { get; set; }
}

/// <summary>Leaves a target member unset, suppressing SYNTX004 and SYNTX013 for it.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class MapperIgnoreTargetAttribute : Attribute
{
    /// <param name="target">The target member to leave unset.</param>
    public MapperIgnoreTargetAttribute(string target) => Target = target;

    /// <summary>The target member that is deliberately not mapped.</summary>
    public string Target { get; }
}

/// <summary>Marks a source member as intentionally unused, suppressing SYNTX005 for it.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class MapperIgnoreSourceAttribute : Attribute
{
    /// <param name="source">The source member to mark as unused.</param>
    public MapperIgnoreSourceAttribute(string source) => Source = source;

    /// <summary>The source member that is deliberately not read.</summary>
    public string Source { get; }
}

/// <summary>Assigns a fixed value to a target member, independent of the source.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class MapValueAttribute : Attribute
{
    /// <param name="target">The target member to fill.</param>
    public MapValueAttribute(string target) => Target = target;

    /// <summary>The target member that receives the fixed value.</summary>
    public string Target { get; }

    /// <summary>A compile-time constant value to assign.</summary>
    public object? Value { get; set; }

    /// <summary>The name of a method that produces the value to assign.</summary>
    public string? Use { get; set; }
}

/// <summary>Registers one derived-type pair for a polymorphic mapping (design doc 7.7).</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class MapDerivedTypeAttribute : Attribute
{
    /// <param name="sourceType">A type that derives from the mapping's source type.</param>
    /// <param name="targetType">The matching type that derives from the mapping's target type.</param>
    public MapDerivedTypeAttribute(Type sourceType, Type targetType)
    {
        SourceType = sourceType;
        TargetType = targetType;
    }

    /// <summary>The derived source type.</summary>
    public Type SourceType { get; }

    /// <summary>The derived target type it is mapped to.</summary>
    public Type TargetType { get; }
}

/// <summary>Marks a normal method inside the mapper as a conversion the generator may call.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class UserMappingAttribute : Attribute
{
    /// <summary>When true, the generator never uses this method as a mapping.</summary>
    public bool Ignore { get; set; }
}
