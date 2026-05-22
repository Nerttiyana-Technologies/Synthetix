namespace Synthetix.Emit;

using System.Text;

/// <summary>
/// A small helper for writing C# source text with correct indentation.
/// </summary>
/// <remarks>
/// It keeps track of how deeply nested the current line is, so the calling code
/// never has to count spaces. One level of nesting is four spaces, the normal
/// C# style.
/// </remarks>
internal sealed class SourceCodeBuilder
{
    private const int SpacesPerLevel = 4;

    private readonly StringBuilder _text = new();
    private int _level;

    /// <summary>Writes one line at the current indentation. An empty call writes a blank line.</summary>
    public SourceCodeBuilder Line(string content = "")
    {
        if (content.Length > 0)
        {
            _text.Append(' ', _level * SpacesPerLevel);
            _text.Append(content);
        }

        _text.Append('\n');
        return this;
    }

    /// <summary>Writes "{" and then indents every following line one level deeper.</summary>
    public SourceCodeBuilder OpenBrace()
    {
        Line("{");
        _level++;
        return this;
    }

    /// <summary>Steps back out one level and writes "}" (with an optional suffix such as ";").</summary>
    public SourceCodeBuilder CloseBrace(string suffix = "")
    {
        _level--;
        Line("}" + suffix);
        return this;
    }

    /// <summary>Returns the finished source text.</summary>
    public override string ToString() => _text.ToString();
}
