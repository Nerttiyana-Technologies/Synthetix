namespace Synthetix.Generator.Tests;

using Xunit;

/// <summary>
/// Checks that the generator reports the right SYNTX#### diagnostic for each
/// kind of broken mapper. This is what makes a mapping mistake fail the build.
/// </summary>
public class DiagnosticsTests
{
    [Fact]
    public void SYNTX001_is_reported_when_the_mapper_class_is_not_partial()
    {
        const string source = """
            public class Source { public int Id { get; set; } }
            public class Target { public int Id { get; set; } }

            [Synthetix.Mapper]
            public class M
            {
                public partial Target Map(Source s);
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        Assert.Contains("SYNTX001", result.DiagnosticIds);
    }

    [Fact]
    public void SYNTX002_is_reported_when_a_mapping_method_is_not_partial()
    {
        const string source = """
            public class Source { public int A { get; set; } }
            public class Target { public int A { get; set; } public int B { get; set; } }

            [Synthetix.Mapper]
            public partial class M
            {
                public partial Target Good(Source s);

                [Synthetix.MapProperty("A", "B")]
                public Target Bad(Source s) => new Target();
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        Assert.Contains("SYNTX002", result.DiagnosticIds);
    }

    [Fact]
    public void SYNTX004_is_reported_for_a_target_member_with_no_source()
    {
        const string source = """
            public class Source { public int Id { get; set; } }
            public class Target { public int Id { get; set; } public string Extra { get; set; } = ""; }

            [Synthetix.Mapper]
            public partial class M
            {
                public partial Target Map(Source s);
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        Assert.Contains("SYNTX004", result.DiagnosticIds);
    }

    [Fact]
    public void SYNTX006_is_reported_when_a_config_names_a_missing_target()
    {
        const string source = """
            public class Source { public int Id { get; set; } }
            public class Target { public int Id { get; set; } }

            [Synthetix.Mapper]
            public partial class M
            {
                [Synthetix.MapProperty("Id", "DoesNotExist")]
                public partial Target Map(Source s);
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        Assert.Contains("SYNTX006", result.DiagnosticIds);
    }

    [Fact]
    public void SYNTX016_is_reported_for_a_mapper_with_no_mapping_methods()
    {
        const string source = """
            [Synthetix.Mapper]
            public partial class M
            {
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        Assert.Contains("SYNTX016", result.DiagnosticIds);
    }

    [Fact]
    public void SYNTX003_is_reported_when_the_source_is_not_a_mappable_type()
    {
        const string source = """
            public class Target { public int Id { get; set; } }

            [Synthetix.Mapper]
            public partial class M
            {
                public partial Target Map(int value);
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        Assert.Contains("SYNTX003", result.DiagnosticIds);
    }

    [Fact]
    public void SYNTX005_is_reported_for_an_unused_source_member_when_enabled()
    {
        const string source = """
            public class Source { public int Id { get; set; } public int Extra { get; set; } }
            public class Target { public int Id { get; set; } }

            [Synthetix.Mapper(UnmappedSourceMember = Synthetix.MemberMappingSeverity.Warn)]
            public partial class M
            {
                public partial Target Map(Source s);
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        Assert.Contains("SYNTX005", result.DiagnosticIds);
    }

    [Fact]
    public void SYNTX007_is_reported_when_a_config_names_a_missing_source_path()
    {
        const string source = """
            public class Source { public int Id { get; set; } }
            public class Target { public int Id { get; set; } }

            [Synthetix.Mapper]
            public partial class M
            {
                [Synthetix.MapProperty("Missing.Path", "Id")]
                public partial Target Map(Source s);
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        Assert.Contains("SYNTX007", result.DiagnosticIds);
    }

    [Fact]
    public void SYNTX008_is_reported_for_an_ambiguous_flatten_path()
    {
        const string source = """
            public class A { public int LineTotal { get; set; } }
            public class B { public int Total { get; set; } }
            public class Source
            {
                public A Order { get; set; } = new();
                public B OrderLine { get; set; } = new();
            }
            public class Target { public int OrderLineTotal { get; set; } }

            [Synthetix.Mapper]
            public partial class M
            {
                public partial Target Map(Source s);
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        // Both Order.LineTotal and OrderLine.Total fold to OrderLineTotal.
        Assert.Contains("SYNTX008", result.DiagnosticIds);
    }

    [Fact]
    public void SYNTX011_is_reported_when_the_target_has_no_usable_constructor()
    {
        const string source = """
            public class Source { public int Id { get; set; } }
            public class Target { private Target() { } public int Id { get; set; } }

            [Synthetix.Mapper]
            public partial class M
            {
                public partial Target Map(Source s);
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        Assert.Contains("SYNTX011", result.DiagnosticIds);
    }

    [Fact]
    public void SYNTX012_is_reported_for_a_nullable_source_into_a_non_nullable_target()
    {
        const string source = """
            public class Source { public int? Value { get; set; } }
            public class Target { public int Value { get; set; } }

            [Synthetix.Mapper]
            public partial class M
            {
                public partial Target Map(Source s);
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        Assert.Contains("SYNTX012", result.DiagnosticIds);
    }

    [Fact]
    public void SYNTX013_is_reported_in_strict_mode_for_an_unaccounted_member()
    {
        const string source = """
            public class Source { public int Id { get; set; } }
            public class Target { public int Id { get; set; } public int Extra { get; set; } }

            [Synthetix.Mapper(RequireExplicitMapping = true)]
            public partial class M
            {
                public partial Target Map(Source s);
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        Assert.Contains("SYNTX013", result.DiagnosticIds);
    }

    [Fact]
    public void SYNTX014_is_reported_when_two_attributes_configure_one_member()
    {
        const string source = """
            public class Source { public int A { get; set; } public int B { get; set; } }
            public class Target { public int X { get; set; } }

            [Synthetix.Mapper]
            public partial class M
            {
                [Synthetix.MapProperty("A", "X")]
                [Synthetix.MapProperty("B", "X")]
                public partial Target Map(Source s);
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        Assert.Contains("SYNTX014", result.DiagnosticIds);
    }

    [Fact]
    public void SYNTX015_is_reported_when_a_Use_converter_method_is_missing()
    {
        const string source = """
            public class Source { public int Id { get; set; } }
            public class Target { public string Id { get; set; } = ""; }

            [Synthetix.Mapper]
            public partial class M
            {
                [Synthetix.MapProperty("Id", "Id", Use = "NoSuchMethod")]
                public partial Target Map(Source s);
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        Assert.Contains("SYNTX015", result.DiagnosticIds);
    }

    [Fact]
    public void A_correct_mapper_reports_no_diagnostics()
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

        Assert.Empty(result.DiagnosticIds);
    }
}
