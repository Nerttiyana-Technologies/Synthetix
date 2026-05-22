namespace Synthetix.Manifest;

using System;
using Microsoft.CodeAnalysis;
using Synthetix.Diagnostics;
using Synthetix.Models;

/// <summary>
/// Compares the current mapping against the manifest committed to source control
/// and reports SYNTX017 when they no longer match (design doc 7.5).
/// </summary>
/// <remarks>
/// This is what stops a mapping from changing silently. If a developer adds a
/// field, renames a property, or a flatten path starts resolving somewhere else,
/// the generated mapping changes - and this check makes that change show up as a
/// diagnostic until the committed manifest is refreshed on purpose.
/// </remarks>
internal static class DriftChecker
{
    /// <summary>
    /// Checks one mapper for drift. Returns a SYNTX017 diagnostic, or null when
    /// there is nothing to report.
    /// </summary>
    public static DiagnosticInfo? Check(
        MapperPlan plan,
        CommittedManifest? committed,
        GeneratorSettings settings,
        LocationInfo? location)
    {
        // Nothing to compare against: either the manifest feature is off, or the
        // developer has not committed a manifest for this mapper yet.
        if (!settings.ManifestEnabled || committed is null)
        {
            return null;
        }

        string current = Normalize(ManifestBuilder.BuildJson(plan));
        string previous = Normalize(committed.Json);

        if (current == previous)
        {
            // The mapping still matches the committed manifest - all good.
            return null;
        }

        string difference = DescribeDifference(previous, current);

        // SYNTX017 is normally a warning. A CI build can ask for it to be an
        // error so a drifted mapping cannot be merged.
        if (settings.TreatDriftAsError)
        {
            return DiagnosticInfo.Create(
                DiagnosticDescriptors.ManifestDrift, location, DiagnosticSeverity.Error,
                plan.Name, difference);
        }

        return DiagnosticInfo.Create(
            DiagnosticDescriptors.ManifestDrift, location, plan.Name, difference);
    }

    /// <summary>Trims and normalises line endings so trivial differences are ignored.</summary>
    private static string Normalize(string text)
        => text.Replace("\r\n", "\n").Trim();

    /// <summary>
    /// Builds a short, human-friendly description of the first place the current
    /// mapping and the committed manifest disagree.
    /// </summary>
    private static string DescribeDifference(string committed, string current)
    {
        string[] committedLines = committed.Split('\n');
        string[] currentLines = current.Split('\n');
        int shorter = Math.Min(committedLines.Length, currentLines.Length);

        for (int i = 0; i < shorter; i++)
        {
            if (committedLines[i] != currentLines[i])
            {
                return "expected '" + committedLines[i].Trim() +
                       "' but the mapping now produces '" + currentLines[i].Trim() + "'";
            }
        }

        // No line differed within the shared part, so the length must differ.
        return currentLines.Length > committedLines.Length
            ? "the mapping now has more entries than the committed manifest"
            : "the mapping now has fewer entries than the committed manifest";
    }
}
