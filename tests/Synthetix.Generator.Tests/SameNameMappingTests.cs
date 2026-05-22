namespace Synthetix.Generator.Tests;

using Xunit;

/// <summary>
/// Checks that the generator produces the expected mapping code for the basic
/// cases: same-name members, flattening, and customization attributes.
/// </summary>
public class SameNameMappingTests
{
    [Fact]
    public void Members_with_the_same_name_are_mapped()
    {
        const string source = """
            public class Source { public int Id { get; set; } public string Name { get; set; } = ""; }
            public class Target { public int Id { get; set; } public string Name { get; set; } = ""; }

            [Synthetix.Mapper]
            public partial class M
            {
                public partial Target Map(Source s);
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        Assert.Contains("target.Id = s.Id;", result.AllGeneratedText);
        Assert.Contains("target.Name = s.Name;", result.AllGeneratedText);
        Assert.Contains("return target;", result.AllGeneratedText);
    }

    [Fact]
    public void Nested_members_are_flattened_by_naming_convention()
    {
        const string source = """
            public class Customer { public string Name { get; set; } = ""; }
            public class Source { public Customer Customer { get; set; } = new(); }
            public class Target { public string CustomerName { get; set; } = ""; }

            [Synthetix.Mapper]
            public partial class M
            {
                public partial Target Map(Source s);
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        Assert.Contains("target.CustomerName = s.Customer.Name;", result.AllGeneratedText);
    }

    [Fact]
    public void MapProperty_renames_a_member()
    {
        const string source = """
            public class Source { public string FullName { get; set; } = ""; }
            public class Target { public string Name { get; set; } = ""; }

            [Synthetix.Mapper]
            public partial class M
            {
                [Synthetix.MapProperty("FullName", "Name")]
                public partial Target Map(Source s);
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        Assert.Contains("target.Name = s.FullName;", result.AllGeneratedText);
    }

    [Fact]
    public void MapValue_assigns_a_constant()
    {
        const string source = """
            public class Source { public int Id { get; set; } }
            public class Target { public int Id { get; set; } public string Channel { get; set; } = ""; }

            [Synthetix.Mapper]
            public partial class M
            {
                [Synthetix.MapValue("Channel", Value = "web")]
                public partial Target Map(Source s);
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        Assert.Contains("target.Channel = \"web\";", result.AllGeneratedText);
    }

    [Fact]
    public void A_partial_class_named_correctly_is_generated()
    {
        const string source = """
            public class Source { public int Id { get; set; } }
            public class Target { public int Id { get; set; } }

            [Synthetix.Mapper]
            public partial class OrderMapper
            {
                public partial Target Map(Source s);
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        Assert.Contains("OrderMapper.g.cs", string.Join(",", result.GeneratedSources.Keys));
        Assert.Contains("partial class OrderMapper", result.AllGeneratedText);
    }
}
