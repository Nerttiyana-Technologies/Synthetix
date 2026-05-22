namespace Synthetix.Generator.Tests;

using System.Text;
using Xunit;

/// <summary>
/// Checks the mapping manifest: the carrier file is produced, a matching
/// committed manifest is quiet, and a stale one reports drift (SYNTX017).
/// </summary>
public class ManifestTests
{
    private const string MapperSource = """
        public class Source { public int Id { get; set; } public string Name { get; set; } = ""; }
        public class Target { public int Id { get; set; } public string Name { get; set; } = ""; }

        [Synthetix.Mapper]
        public partial class OrderMapper
        {
            public partial Target Map(Source s);
        }
        """;

    [Fact]
    public void A_manifest_carrier_file_is_generated()
    {
        HarnessResult result = GeneratorTestHarness.Run(MapperSource);

        string carrier = result.GeneratedFile("Manifest.g.cs");

        Assert.Contains("SYNTHETIX-MANIFEST-BEGIN", carrier);
        Assert.Contains("SYNTHETIX-MANIFEST-END", carrier);
        Assert.Contains("OrderMapper", carrier);
    }

    [Fact]
    public void A_committed_manifest_that_matches_reports_no_drift()
    {
        // First run: read back the manifest the generator currently produces.
        HarnessResult first = GeneratorTestHarness.Run(MapperSource);
        string currentJson = ExtractJsonFromCarrier(first.GeneratedFile("Manifest.g.cs"));

        // Second run: commit that exact manifest. There should be no drift.
        HarnessResult second = GeneratorTestHarness.Run(
            MapperSource,
            additionalFiles: new[] { ("mapping/OrderMapper.manifest.json", currentJson) });

        Assert.DoesNotContain("SYNTX017", second.DiagnosticIds);
    }

    [Fact]
    public void A_committed_manifest_that_is_stale_reports_SYNTX017()
    {
        // A manifest that clearly does not match the current mapping.
        const string staleManifest = """
            {
              "mapper": "OrderMapper",
              "methods": []
            }
            """;

        HarnessResult result = GeneratorTestHarness.Run(
            MapperSource,
            additionalFiles: new[] { ("mapping/OrderMapper.manifest.json", staleManifest) });

        Assert.Contains("SYNTX017", result.DiagnosticIds);
    }

    /// <summary>Pulls the JSON text back out of a manifest carrier file.</summary>
    private static string ExtractJsonFromCarrier(string carrier)
    {
        var json = new StringBuilder();
        bool inside = false;

        foreach (string line in carrier.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith("//SYNTHETIX-MANIFEST-BEGIN", System.StringComparison.Ordinal) &&
                line.Contains("format=json"))
            {
                inside = true;
                continue;
            }

            if (line.StartsWith("//SYNTHETIX-MANIFEST-END", System.StringComparison.Ordinal))
            {
                if (inside)
                {
                    break;
                }

                continue;
            }

            if (inside)
            {
                // Every content line is prefixed with "//"; strip it.
                json.Append(line.Length >= 2 ? line.Substring(2) : string.Empty).Append('\n');
            }
        }

        return json.ToString();
    }
}
