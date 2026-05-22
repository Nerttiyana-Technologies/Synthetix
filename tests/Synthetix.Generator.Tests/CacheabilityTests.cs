namespace Synthetix.Generator.Tests;

using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

/// <summary>
/// Checks that the generator's caching works: an edit somewhere unrelated must
/// not re-run the mapping generation.
/// </summary>
/// <remarks>
/// This is the test that protects the incrementality design (design doc 6). It
/// is easy to break caching by accident - for example by putting a value into a
/// model that does not compare by value - and only a test like this catches it.
/// </remarks>
public class CacheabilityTests
{
    private const string MapperSource = """
        public class Source { public int Id { get; set; } public string Name { get; set; } = ""; }
        public class Target { public int Id { get; set; } public string Name { get; set; } = ""; }

        [Synthetix.Mapper]
        public partial class M
        {
            public partial Target Map(Source s);
        }
        """;

    [Fact]
    public void An_unrelated_edit_does_not_rerun_mapping_generation()
    {
        Compilation compilation = GeneratorTestHarness.CreateCompilation(MapperSource);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new SynthetixGenerator().AsSourceGenerator() },
            additionalTexts: null,
            parseOptions: null,
            optionsProvider: null,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        // First run.
        driver = driver.RunGenerators(compilation);

        // Add a completely unrelated class and run again.
        Compilation editedCompilation = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { internal class Unrelated { } }"));
        driver = driver.RunGenerators(editedCompilation);

        GeneratorRunResult result = driver.GetRunResult().Results.Single();

        // The model step and the plan step must both be reused, not recomputed.
        AssertEveryStepWasReused(result, SynthetixGenerator.ModelTrackingName);
        AssertEveryStepWasReused(result, SynthetixGenerator.PlanTrackingName);
    }

    [Fact]
    public void Running_twice_produces_identical_output()
    {
        HarnessResult first = GeneratorTestHarness.Run(MapperSource);
        HarnessResult second = GeneratorTestHarness.Run(MapperSource);

        Assert.Equal(first.AllGeneratedText, second.AllGeneratedText);
    }

    private static void AssertEveryStepWasReused(GeneratorRunResult result, string trackingName)
    {
        Assert.True(
            result.TrackedSteps.ContainsKey(trackingName),
            $"Expected a tracked step named '{trackingName}'.");

        foreach (IncrementalGeneratorRunStep step in result.TrackedSteps[trackingName])
        {
            foreach ((object Value, IncrementalStepRunReason Reason) output in step.Outputs)
            {
                Assert.True(
                    output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                    $"Step '{trackingName}' was re-run (reason: {output.Reason}). Caching is broken.");
            }
        }
    }
}
