namespace Synthetix.Pipeline;

using System;
using System.Collections.Generic;
using Synthetix.Diagnostics;
using Synthetix.Models;

/// <summary>
/// Reads the member-level attributes on one mapping method into easy-to-use
/// lookups, and reports the diagnostics that come from bad configuration.
/// </summary>
/// <remarks>
/// This is where SYNTX006 (a config names a target member that does not exist),
/// SYNTX007 (an ignored source member does not exist), and SYNTX014 (two
/// attributes of equal precedence fight over one member) are detected.
/// </remarks>
internal sealed class MethodConfiguration
{
    /// <summary>[MapValue] attributes, keyed by the exact target member name.</summary>
    public Dictionary<string, MemberConfig> MapValues { get; } = new(StringComparer.Ordinal);

    /// <summary>[MapProperty] attributes, keyed by the exact target member name.</summary>
    public Dictionary<string, MemberConfig> MapProperties { get; } = new(StringComparer.Ordinal);

    /// <summary>Target member names carrying [MapperIgnoreTarget].</summary>
    public HashSet<string> IgnoredTargetNames { get; } = new(StringComparer.Ordinal);

    /// <summary>Source member names carrying [MapperIgnoreSource].</summary>
    public HashSet<string> IgnoredSourceNames { get; } = new(StringComparer.Ordinal);

    /// <summary>The ignored source members, ready to record in the plan and manifest.</summary>
    public List<IgnoredMember> IgnoredSourceMembers { get; } = new();

    public MethodConfiguration(
        MappingMethodModel method,
        TypeModel target,
        TypeModel source,
        List<DiagnosticInfo> diagnostics)
    {
        foreach (MemberConfig config in method.Configurations)
        {
            switch (config.Kind)
            {
                case ConfigKind.MapValue:
                    AddTargetConfig(config, config.Target, target, MapValues, diagnostics);
                    break;

                case ConfigKind.MapProperty:
                    AddTargetConfig(config, config.Target, target, MapProperties, diagnostics);
                    break;

                case ConfigKind.IgnoreTarget:
                    AddIgnoreTarget(config, target, diagnostics);
                    break;

                case ConfigKind.IgnoreSource:
                    AddIgnoreSource(config, source, diagnostics);
                    break;
            }
        }
    }

    /// <summary>Adds a [MapValue] or [MapProperty] after checking the target member exists.</summary>
    private static void AddTargetConfig(
        MemberConfig config,
        string? targetName,
        TypeModel target,
        Dictionary<string, MemberConfig> store,
        List<DiagnosticInfo> diagnostics)
    {
        if (targetName is null)
        {
            return;
        }

        // The named target member has to exist.
        if (target.FindMember(targetName, ignoreCase: false) is null)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.TargetMemberNotFound,
                config.Location, targetName, target.DisplayName));
            return;
        }

        // Two attributes of the same kind on the same member is a real conflict.
        if (store.ContainsKey(targetName))
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.ConflictingConfiguration, config.Location, targetName));
            return;
        }

        store[targetName] = config;
    }

    private void AddIgnoreTarget(MemberConfig config, TypeModel target, List<DiagnosticInfo> diagnostics)
    {
        if (config.Target is null)
        {
            return;
        }

        if (target.FindMember(config.Target, ignoreCase: false) is null)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.TargetMemberNotFound,
                config.Location, config.Target, target.DisplayName));
            return;
        }

        IgnoredTargetNames.Add(config.Target);
    }

    private void AddIgnoreSource(MemberConfig config, TypeModel source, List<DiagnosticInfo> diagnostics)
    {
        if (config.Source is null)
        {
            return;
        }

        if (source.FindMember(config.Source, ignoreCase: false) is null)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.SourcePathNotFound,
                config.Location, config.Source, source.DisplayName));
            return;
        }

        IgnoredSourceNames.Add(config.Source);
        IgnoredSourceMembers.Add(new IgnoredMember(config.Source, "[MapperIgnoreSource]"));
    }
}
