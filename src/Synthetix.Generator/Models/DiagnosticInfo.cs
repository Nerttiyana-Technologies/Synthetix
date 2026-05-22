namespace Synthetix.Models;

using Microsoft.CodeAnalysis;

/// <summary>
/// A value-comparable description of one diagnostic the generator wants to report.
/// </summary>
/// <remarks>
/// The generator works out its diagnostics in Stage 3 (so they take part in
/// caching) but is only allowed to actually report them in Stage 4. Roslyn's own
/// <see cref="Diagnostic"/> type is not safe to keep between those stages, so we
/// store the plain facts here and rebuild the real <see cref="Diagnostic"/> at
/// the last moment with <see cref="ToDiagnostic"/>.
/// </remarks>
public sealed record DiagnosticInfo(
    DiagnosticDescriptor Descriptor,
    LocationInfo? Location,
    EquatableArray<string> MessageArgs,
    // Normally a diagnostic uses the severity baked into its descriptor. But the
    // [Mapper] attribute lets the developer pick the severity for a few rules
    // (for example, treat an unmapped target member as an error). When that
    // happens we store the chosen severity here.
    DiagnosticSeverity? OverrideSeverity = null)
{
    /// <summary>Rebuilds a real Roslyn diagnostic ready to be reported.</summary>
    public Diagnostic ToDiagnostic()
    {
        Location where = Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None;
        object[] args = MessageArgs.ToArray();

        if (OverrideSeverity is DiagnosticSeverity severity)
        {
            // Report at the severity the developer asked for instead of the default.
            return Diagnostic.Create(
                Descriptor,
                where,
                severity,
                additionalLocations: null,
                properties: null,
                messageArgs: args);
        }

        return Diagnostic.Create(Descriptor, where, args);
    }

    /// <summary>Convenience builder so callers do not touch EquatableArray directly.</summary>
    public static DiagnosticInfo Create(
        DiagnosticDescriptor descriptor,
        LocationInfo? location,
        params string[] messageArgs)
        => new(descriptor, location, new EquatableArray<string>(messageArgs));

    /// <summary>Builds a diagnostic that reports at a chosen, non-default severity.</summary>
    public static DiagnosticInfo Create(
        DiagnosticDescriptor descriptor,
        LocationInfo? location,
        DiagnosticSeverity overrideSeverity,
        params string[] messageArgs)
        => new(descriptor, location, new EquatableArray<string>(messageArgs), overrideSeverity);
}
