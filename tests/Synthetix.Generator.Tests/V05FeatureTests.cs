namespace Synthetix.Generator.Tests;

using Xunit;

/// <summary>
/// Unit tests for the v0.5 features: collection-element mapping (design doc 7.6)
/// and polymorphic dispatch (7.7).
/// </summary>
public class V05FeatureTests
{
    [Fact]
    public void A_collection_member_generates_a_helper_method()
    {
        const string source = """
            public class S { public System.Collections.Generic.List<int> Items { get; set; } = new(); }
            public class T { public System.Collections.Generic.List<long> Items { get; set; } = new(); }

            [Synthetix.Mapper]
            public partial class M
            {
                public partial T Map(S s);
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        Assert.Empty(result.DiagnosticIds);
        Assert.Contains("__MapCollection0", result.AllGeneratedText);
        Assert.Contains("foreach", result.AllGeneratedText);
    }

    [Fact]
    public void An_element_with_no_conversion_reports_SYNTX018()
    {
        const string source = """
            public class Widget { }
            public class Gadget { }
            public class S { public System.Collections.Generic.List<Widget> Items { get; set; } = new(); }
            public class T { public System.Collections.Generic.List<Gadget> Items { get; set; } = new(); }

            [Synthetix.Mapper]
            public partial class M
            {
                public partial T Map(S s);
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        Assert.Contains("SYNTX018", result.DiagnosticIds);
    }

    [Fact]
    public void A_non_exhaustive_polymorphic_mapping_reports_SYNTX020()
    {
        const string source = """
            public abstract class Animal { public string Name { get; set; } = ""; }
            public sealed class Dog : Animal { }
            public sealed class Cat : Animal { }
            public abstract class AnimalDto { public string Name { get; set; } = ""; }
            public sealed class DogDto : AnimalDto { }
            public sealed class CatDto : AnimalDto { }

            [Synthetix.Mapper]
            public partial class M
            {
                [Synthetix.MapDerivedType(typeof(Dog), typeof(DogDto))]
                public partial AnimalDto ToDto(Animal a);

                public partial DogDto ToDto(Dog d);
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        // Cat derives from Animal but has no [MapDerivedType] entry.
        Assert.Contains("SYNTX020", result.DiagnosticIds);
    }

    [Fact]
    public void A_fully_covered_polymorphic_mapping_dispatches_on_the_runtime_type()
    {
        const string source = """
            public abstract class Animal { public string Name { get; set; } = ""; }
            public sealed class Dog : Animal { }
            public sealed class Cat : Animal { }
            public abstract class AnimalDto { public string Name { get; set; } = ""; }
            public sealed class DogDto : AnimalDto { }
            public sealed class CatDto : AnimalDto { }

            [Synthetix.Mapper]
            public partial class M
            {
                [Synthetix.MapDerivedType(typeof(Dog), typeof(DogDto))]
                [Synthetix.MapDerivedType(typeof(Cat), typeof(CatDto))]
                public partial AnimalDto ToDto(Animal a);

                public partial DogDto ToDto(Dog d);
                public partial CatDto ToDto(Cat c);
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        Assert.DoesNotContain("SYNTX020", result.DiagnosticIds);
        Assert.Contains("GetType()", result.AllGeneratedText);
        Assert.Contains("typeof(", result.AllGeneratedText);
    }
}
