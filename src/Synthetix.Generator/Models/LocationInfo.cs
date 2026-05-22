namespace Synthetix.Models;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

/// <summary>
/// A value-comparable copy of a source code location (which file, and where in it).
/// </summary>
/// <remarks>
/// Roslyn's own <see cref="Location"/> type cannot be stored inside our models:
/// it is tied to a whole compilation and is not safe to keep across builds. So
/// in Stage 2 we copy the few facts we need - the file path and the character
/// range - into this plain record. Stage 4 turns it back into a real
/// <see cref="Location"/> only at the moment a diagnostic is reported.
/// </remarks>
public sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    /// <summary>Rebuilds a Roslyn <see cref="Location"/> from the stored facts.</summary>
    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);

    /// <summary>Captures the location of a syntax node, or null if it has none.</summary>
    public static LocationInfo? CreateFrom(SyntaxNode? node)
        => node is null ? null : CreateFrom(node.GetLocation());

    /// <summary>Captures a Roslyn location, or null if it is not inside a source file.</summary>
    public static LocationInfo? CreateFrom(Location location)
    {
        // A location without a source tree (for example, one inside metadata)
        // cannot be pointed at, so we keep nothing.
        if (location.SourceTree is null)
        {
            return null;
        }

        return new LocationInfo(
            location.SourceTree.FilePath,
            location.SourceSpan,
            location.GetLineSpan().Span);
    }
}
