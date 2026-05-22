namespace Synthetix.Generator.Tests;

using Xunit;

/// <summary>
/// Unit tests for the v0.6 features: existing-instance update (design doc 7.8),
/// string and primitive conversions (7.9), IQueryable projection (7.10), and
/// async mappers (7.11) - plus the diagnostics SYNTX022, SYNTX023, and SYNTX024.
/// </summary>
public class V06FeatureTests
{
    // ===================== existing-instance update (7.8) =====================

    [Fact]
    public void An_update_method_assigns_onto_the_target_parameter()
    {
        const string source = """
            public class S { public int Value { get; set; } }
            public class T { public int Value { get; set; } }

            [Synthetix.Mapper]
            public partial class M
            {
                public partial void Update(S s, T t);
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        Assert.Empty(result.DiagnosticIds);
        // An update writes straight onto the target parameter "t".
        Assert.Contains("t.Value = s.Value", result.AllGeneratedText);
    }

    [Fact]
    public void An_update_with_an_init_only_member_reports_SYNTX022()
    {
        const string source = """
            public class S { public int Value { get; set; } }
            public class T { public int Value { get; set; } public string Tag { get; init; } = ""; }

            [Synthetix.Mapper]
            public partial class M
            {
                public partial void Update(S s, T t);
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        // "Tag" is init-only, so it cannot be set on an existing instance.
        Assert.Contains("SYNTX022", result.DiagnosticIds);
    }

    // ===================== string / primitive conversions (7.9) =====================

    [Fact]
    public void A_string_conversion_is_used_when_the_mapper_opts_in()
    {
        const string source = """
            public class S { public int Count { get; set; } }
            public class T { public string Count { get; set; } = ""; }

            [Synthetix.Mapper(EnableStringConversions = true)]
            public partial class M
            {
                public partial T Map(S s);
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        Assert.Empty(result.DiagnosticIds);
        Assert.Contains("ToString(", result.AllGeneratedText);
    }

    [Fact]
    public void A_string_conversion_is_not_applied_unless_opted_in()
    {
        const string source = """
            public class S { public int Count { get; set; } }
            public class T { public string Count { get; set; } = ""; }

            [Synthetix.Mapper]
            public partial class M
            {
                public partial T Map(S s);
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        // Without EnableStringConversions, int -> string is a type mismatch.
        Assert.Contains("SYNTX010", result.DiagnosticIds);
    }

    // ===================== IQueryable projection (7.10) =====================

    [Fact]
    public void A_projection_property_generates_an_object_initializer_lambda()
    {
        const string source = """
            public class Person { public int Id { get; set; } public string Name { get; set; } = ""; }
            public class PersonDto { public int Id { get; set; } public string Name { get; set; } = ""; }

            [Synthetix.Mapper]
            public partial class M
            {
                public static partial System.Linq.Expressions.Expression<
                    System.Func<Person, PersonDto>> Projection { get; }
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        Assert.Empty(result.DiagnosticIds);
        // The lambda parameter is named after the source type.
        Assert.Contains("static person =>", result.AllGeneratedText);
        Assert.Contains("Id = person.Id", result.AllGeneratedText);
        Assert.Contains("Name = person.Name", result.AllGeneratedText);
    }

    [Fact]
    public void A_projection_flattens_a_nested_path()
    {
        const string source = """
            public class Inner { public string City { get; set; } = ""; }
            public class Person { public Inner Home { get; set; } = new(); }
            public class PersonDto { public string HomeCity { get; set; } = ""; }

            [Synthetix.Mapper]
            public partial class M
            {
                public static partial System.Linq.Expressions.Expression<
                    System.Func<Person, PersonDto>> Projection { get; }
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        Assert.Empty(result.DiagnosticIds);
        Assert.Contains(".Home.City", result.AllGeneratedText);
    }

    [Fact]
    public void A_projection_needing_a_non_projectable_conversion_reports_SYNTX023()
    {
        const string source = """
            public class S { public int Value { get; set; } }
            public class T { public string Value { get; set; } = ""; }

            [Synthetix.Mapper]
            public partial class M
            {
                public static partial System.Linq.Expressions.Expression<
                    System.Func<S, T>> Projection { get; }
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        // int -> string has no expression-tree form, so the projection fails.
        Assert.Contains("SYNTX023", result.DiagnosticIds);
    }

    // ===================== async mappers (7.11) =====================

    [Fact]
    public void An_async_method_with_no_async_conversion_returns_a_completed_task()
    {
        const string source = """
            public class S { public int Id { get; set; } }
            public class T { public int Id { get; set; } }

            [Synthetix.Mapper]
            public partial class M
            {
                public partial System.Threading.Tasks.Task<T> MapAsync(S s);
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        Assert.Empty(result.DiagnosticIds);
        // No await is needed, so the result is wrapped in a completed task.
        Assert.Contains("Task.FromResult(", result.AllGeneratedText);
    }

    [Fact]
    public void An_async_method_with_an_async_user_mapping_uses_await()
    {
        const string source = """
            public class S { public int Id { get; set; } }
            public class T { public string Id { get; set; } = ""; }

            [Synthetix.Mapper]
            public partial class M
            {
                public partial System.Threading.Tasks.Task<T> MapAsync(S s);

                private static System.Threading.Tasks.Task<string> Render(int id)
                    => System.Threading.Tasks.Task.FromResult("id");
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        Assert.Empty(result.DiagnosticIds);
        Assert.Contains("async", result.AllGeneratedText);
        Assert.Contains("await Render(", result.AllGeneratedText);
    }

    [Fact]
    public void A_sync_method_needing_an_async_mapping_reports_SYNTX024()
    {
        const string source = """
            public class S { public int Id { get; set; } }
            public class T { public string Id { get; set; } = ""; }

            [Synthetix.Mapper]
            public partial class M
            {
                public partial T Map(S s);

                private static System.Threading.Tasks.Task<string> Render(int id)
                    => System.Threading.Tasks.Task.FromResult("id");
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(source);

        // A synchronous method cannot await the async user mapping.
        Assert.Contains("SYNTX024", result.DiagnosticIds);
    }
}
