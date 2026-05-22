// This project targets netstandard2.0, which is an old API surface. The C#
// "init" accessor (used by records and init-only properties) needs a marker
// type called IsExternalInit to exist. Newer target frameworks ship it, but
// netstandard2.0 does not, so we declare it here ourselves.
//
// This is a well-known, harmless "polyfill". It contains no logic.

namespace System.Runtime.CompilerServices;

using System.ComponentModel;

/// <summary>
/// Marker type the compiler needs so that "init" accessors can be used.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
internal static class IsExternalInit
{
}
