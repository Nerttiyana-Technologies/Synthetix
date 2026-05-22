namespace Synthetix.Models;

/// <summary>
/// The handful of build settings that change how the generator behaves. They
/// come from MSBuild properties defined in Synthetix.targets.
/// </summary>
public sealed record GeneratorSettings(
    // True when the mapping manifest should be produced and drift checked.
    bool ManifestEnabled,
    // True when manifest drift (SYNTX017) should be a build error, not a warning.
    bool TreatDriftAsError)
{
    /// <summary>The defaults used when no MSBuild property is set.</summary>
    public static GeneratorSettings Default => new(ManifestEnabled: true, TreatDriftAsError: false);
}

/// <summary>
/// The committed manifest for one mapper, read back from a mapping/*.manifest.json
/// file. The generator compares this against the freshly computed mapping to find
/// drift (SYNTX017).
/// </summary>
public sealed record CommittedManifest(
    // The mapper name taken from the file name, for example "OrderMapper".
    string MapperName,
    // The raw JSON text of the committed file.
    string Json);
