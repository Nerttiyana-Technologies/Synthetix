namespace Synthetix;

using System.Collections.Immutable;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Synthetix.Attributes;
using Synthetix.Manifest;
using Synthetix.Models;
using Synthetix.Pipeline;

/// <summary>
/// The Synthetix source generator. This class is the entry point - it wires the
/// four pipeline stages together and runs inside the C# compiler.
/// </summary>
/// <remarks>
/// The pipeline, end to end:
///   Stage 0  inject the Synthetix attribute definitions.
///   Stage 1  find every class marked with [Mapper].
///   Stage 2  build a value-comparable model of each mapper (ModelBuilder).
///   Stage 3  resolve each model into a finished mapping plan (PlanResolver).
///   Stage 4  emit the generated C#, the manifest, and the diagnostics (Emitter).
///
/// Each stage hands the next one small, value-comparable data, so the compiler
/// can cache aggressively and an unrelated edit does not re-run everything.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class SynthetixGenerator : IIncrementalGenerator
{
    // The attribute the generator looks for. It is the only thing that turns the
    // generator on for a given class.
    private const string MapperAttributeName = "Synthetix.MapperAttribute";

    // MSBuild properties become "build_property.<Name>" in the analyzer options.
    private const string ManifestProperty = "build_property.SynthetixManifest";
    private const string DriftAsErrorProperty = "build_property.SynthetixTreatDriftAsError";

    // Committed manifest files have this ending.
    private const string ManifestFileSuffix = ".manifest.json";

    /// <summary>Tracking name for the Stage 2 model step (used by cacheability tests).</summary>
    public const string ModelTrackingName = "SynthetixModel";

    /// <summary>Tracking name for the Stage 3 plan step (used by cacheability tests).</summary>
    public const string PlanTrackingName = "SynthetixPlan";

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // ----- Stage 0: inject the attribute definitions -----
        context.RegisterPostInitializationOutput(static postInit =>
            postInit.AddSource(
                InjectedAttributeSource.HintName,
                SourceText.From(InjectedAttributeSource.Source, Encoding.UTF8)));

        // ----- Stages 1 and 2: find mappers and build their models -----
        // The tracking names let the cacheability tests check that this step is
        // reused when an unrelated edit happens.
        IncrementalValuesProvider<MapperModel> mapperModels = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                MapperAttributeName,
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (attributeContext, ct) => ModelBuilder.Build(attributeContext, ct))
            .WithTrackingName(ModelTrackingName);

        // ----- Stage 3: resolve each model into a plan -----
        IncrementalValuesProvider<MapperPlan> plans = mapperModels
            .Select(static (model, ct) => PlanResolver.Resolve(model, ct))
            .WithTrackingName(PlanTrackingName);

        // The build settings, read once from MSBuild properties.
        IncrementalValueProvider<GeneratorSettings> settings = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) => ReadSettings(options));

        // The committed manifest files, collected so the drift check can see them.
        IncrementalValueProvider<ImmutableArray<CommittedManifest>> committedManifests = context.AdditionalTextsProvider
            .Where(static text => text.Path.EndsWith(ManifestFileSuffix, System.StringComparison.OrdinalIgnoreCase))
            .Select(static (text, ct) => ReadCommittedManifest(text, ct))
            .Where(static manifest => manifest is not null)
            .Select(static (manifest, _) => manifest!)
            .Collect();

        IncrementalValuesProvider<(MapperPlan Plan, GeneratorSettings Settings)> plansWithSettings =
            plans.Combine(settings);

        // ----- Stage 4a: emit the generated code, the carrier, and diagnostics -----
        // This does NOT depend on the committed manifests, so editing a manifest
        // file never re-runs code generation.
        context.RegisterSourceOutput(plansWithSettings, static (production, pair) =>
            EmitMapper(production, pair.Plan, pair.Settings));

        // ----- Stage 4b: the drift check (SYNTX017) -----
        // This is the only part that needs the committed manifests.
        context.RegisterSourceOutput(
            plansWithSettings.Combine(committedManifests),
            static (production, data) =>
                ReportDrift(production, data.Left.Plan, data.Left.Settings, data.Right));
    }

    /// <summary>Emits the generated mapper source, the manifest carrier, and plan diagnostics.</summary>
    private static void EmitMapper(
        SourceProductionContext production, MapperPlan plan, GeneratorSettings settings)
    {
        // Report every diagnostic Stage 3 worked out for this mapper.
        foreach (DiagnosticInfo diagnostic in Emitter.CollectDiagnostics(plan))
        {
            production.ReportDiagnostic(diagnostic.ToDiagnostic());
        }

        // If the mapper class is unusable, or it has no methods, there is nothing
        // to generate - the diagnostics above already explain why.
        if (!plan.CanEmit || plan.Methods.Count == 0)
        {
            return;
        }

        // The generated partial class with the mapping method bodies.
        production.AddSource(
            plan.HintName + ".g.cs",
            SourceText.From(Emitter.Emit(plan), Encoding.UTF8));

        // The manifest carrier - the comment-only file the update target reads.
        if (settings.ManifestEnabled)
        {
            production.AddSource(
                plan.HintName + ".Manifest.g.cs",
                SourceText.From(ManifestCarrierEmitter.Emit(plan), Encoding.UTF8));
        }
    }

    /// <summary>Compares the mapper against its committed manifest and reports drift.</summary>
    private static void ReportDrift(
        SourceProductionContext production,
        MapperPlan plan,
        GeneratorSettings settings,
        ImmutableArray<CommittedManifest> committedManifests)
    {
        if (!settings.ManifestEnabled || !plan.CanEmit || plan.Methods.Count == 0)
        {
            return;
        }

        // Find the committed manifest that belongs to this mapper, if any.
        CommittedManifest? committed = null;
        foreach (CommittedManifest candidate in committedManifests)
        {
            if (candidate.MapperName == plan.Name)
            {
                committed = candidate;
                break;
            }
        }

        DiagnosticInfo? drift = DriftChecker.Check(plan, committed, settings, plan.Location);
        if (drift is not null)
        {
            production.ReportDiagnostic(drift.ToDiagnostic());
        }
    }

    /// <summary>Reads the generator settings from the MSBuild properties.</summary>
    private static GeneratorSettings ReadSettings(AnalyzerConfigOptionsProvider options)
    {
        AnalyzerConfigOptions global = options.GlobalOptions;

        // The manifest is on unless the property is explicitly "false".
        bool manifestEnabled =
            !global.TryGetValue(ManifestProperty, out string? manifestValue) ||
            !string.Equals(manifestValue, "false", System.StringComparison.OrdinalIgnoreCase);

        // Drift is an error only when the property is explicitly "true".
        bool driftAsError =
            global.TryGetValue(DriftAsErrorProperty, out string? driftValue) &&
            string.Equals(driftValue, "true", System.StringComparison.OrdinalIgnoreCase);

        return new GeneratorSettings(manifestEnabled, driftAsError);
    }

    /// <summary>Reads one committed manifest file into a value-comparable record.</summary>
    private static CommittedManifest? ReadCommittedManifest(AdditionalText text, System.Threading.CancellationToken ct)
    {
        SourceText? content = text.GetText(ct);
        if (content is null)
        {
            return null;
        }

        // The mapper name is the file name without the ".manifest.json" ending,
        // for example "OrderMapper.manifest.json" -> "OrderMapper".
        string fileName = Path.GetFileName(text.Path);
        string mapperName = fileName.Substring(0, fileName.Length - ManifestFileSuffix.Length);

        return new CommittedManifest(mapperName, content.ToString());
    }
}
