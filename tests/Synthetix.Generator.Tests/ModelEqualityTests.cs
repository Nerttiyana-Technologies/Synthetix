namespace Synthetix.Generator.Tests;

using Synthetix.Diagnostics;
using Synthetix.Models;
using Xunit;

/// <summary>
/// Checks the value-equality contract of the model types.
/// </summary>
/// <remarks>
/// The generator's caching only works if two models with the same content count
/// as equal (design doc 6.2). If a model ever stops comparing by value, caching
/// silently breaks and the generator gets slow. These tests guard against that.
/// </remarks>
public class ModelEqualityTests
{
    [Fact]
    public void EquatableArray_is_equal_when_the_items_match()
    {
        var first = new EquatableArray<string>(new[] { "a", "b", "c" });
        var second = new EquatableArray<string>(new[] { "a", "b", "c" });

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void EquatableArray_is_not_equal_when_an_item_differs()
    {
        var first = new EquatableArray<string>(new[] { "a", "b", "c" });
        var second = new EquatableArray<string>(new[] { "a", "b", "X" });

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void EquatableArray_is_not_equal_when_the_length_differs()
    {
        var first = new EquatableArray<string>(new[] { "a", "b" });
        var second = new EquatableArray<string>(new[] { "a", "b", "c" });

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void MemberModel_is_equal_when_every_field_matches()
    {
        TypeModel type = TypeModel.Leaf("int", "int", TypeCategory.Simple, isReferenceType: false);

        var first = new MemberModel("Id", type, false, true, true, false, false, false);
        var second = new MemberModel("Id", type, false, true, true, false, false, false);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void MemberModel_is_not_equal_when_one_field_differs()
    {
        TypeModel type = TypeModel.Leaf("int", "int", TypeCategory.Simple, isReferenceType: false);

        var writable = new MemberModel("Id", type, false, true, true, false, false, false);
        var readOnly = new MemberModel("Id", type, false, true, false, false, false, false);

        Assert.NotEqual(writable, readOnly);
    }

    [Fact]
    public void TypeModel_is_equal_when_the_members_match()
    {
        TypeModel intType = TypeModel.Leaf("int", "int", TypeCategory.Simple, isReferenceType: false);
        MemberModel member = new("Id", intType, false, true, true, false, false, false);

        var first = new TypeModel(
            "global::T", "global::T", TypeCategory.Complex, true, false, null,
            new EquatableArray<MemberModel>(new[] { member }),
            EquatableArray<ConstructorModel>.Empty,
            EquatableArray<string>.Empty);

        var second = new TypeModel(
            "global::T", "global::T", TypeCategory.Complex, true, false, null,
            new EquatableArray<MemberModel>(new[] { member }),
            EquatableArray<ConstructorModel>.Empty,
            EquatableArray<string>.Empty);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Assignment_compares_by_value()
    {
        var first = new Assignment("X", "s.X", MappingRuleKind.SameName, "s.X");
        var second = new Assignment("X", "s.X", MappingRuleKind.SameName, "s.X");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, first with { ValueExpression = "s.Y" });
    }

    [Fact]
    public void MapperOptions_distinguishes_the_string_conversion_flag()
    {
        // EnableStringConversions is a v0.6 field; it must take part in equality
        // or the generator's cache would not notice the [Mapper] setting change.
        Assert.Equal(MapperOptions.Default, MapperOptions.Default with { });
        Assert.NotEqual(
            MapperOptions.Default,
            MapperOptions.Default with { EnableStringConversions = true });
    }

    [Fact]
    public void UserMappingModel_distinguishes_the_async_flag()
    {
        var sync = new UserMappingModel(
            "Convert", true, "int", "int", "string", "string", false, false, null, false);

        Assert.Equal(sync, sync with { });
        Assert.NotEqual(sync, sync with { IsAsync = true });
    }

    [Fact]
    public void MethodPlan_distinguishes_the_v06_flags()
    {
        MethodPlan baseline = SampleMethodPlan();

        // An identical copy is equal; a single changed v0.6 flag is not.
        Assert.Equal(baseline, baseline with { });
        Assert.NotEqual(baseline, baseline with { IsProjection = true });
        Assert.NotEqual(baseline, baseline with { IsAsyncMethod = true });
        Assert.NotEqual(baseline, baseline with { IsUpdate = true });
    }

    /// <summary>A minimal, valid MethodPlan used as a baseline for "with" tests.</summary>
    private static MethodPlan SampleMethodPlan() => new(
        MethodName: "Map",
        Accessibility: "public",
        IsStatic: false,
        ReturnTypeDisplay: "global::T",
        SourceTypeDisplay: "global::S",
        SourceParameterName: "s",
        TargetTypeDisplay: "global::T",
        HasValidBody: true,
        ConstructorArguments: EquatableArray<Assignment>.Empty,
        InitializerAssignments: EquatableArray<Assignment>.Empty,
        PostAssignments: EquatableArray<Assignment>.Empty,
        IgnoredTargets: EquatableArray<IgnoredMember>.Empty,
        IgnoredSources: EquatableArray<IgnoredMember>.Empty,
        CoverageMapped: 1,
        CoverageIgnored: 0,
        CoverageUnmapped: 0,
        Diagnostics: EquatableArray<DiagnosticInfo>.Empty);
}
