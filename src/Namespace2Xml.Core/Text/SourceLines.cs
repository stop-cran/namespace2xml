namespace Namespace2Xml.Text;

/// <summary>
/// The Section 22 line boundaries of one decoded source, and the conversion from a host parser's
/// column into the Section 22 column of the same position.
/// </summary>
/// <remarks>
/// <para>
/// Section 22 measures a column in Unicode scalar values. Every host parser this tool reads
/// through -- <see cref="System.Xml.IXmlLineInfo"/> and YamlDotNet's <c>Mark</c> among them --
/// measures it in UTF-16 code units instead, because that is the unit its buffer is indexed by.
/// The two agree on every line made only of characters in the Basic Multilingual Plane and differ
/// by one column per supplementary scalar to the left of the position, so passing a host column
/// straight through is a defect that stays invisible until someone puts an emoji in a
/// configuration file.
/// </para>
/// <para>
/// Section 22 also fixes what ends a line: "LF, CRLF, or a lone CR, and by nothing else", and
/// U+0085, U+2028 and U+2029 explicitly do not. That is the rule this table is built to, and it
/// is also the rule XML and YAML line counting follows, so a host line number indexes it directly.
/// </para>
/// </remarks>
public sealed class SourceLines
{
    private readonly string text;
    private readonly int[] starts;

    /// <summary>Builds the table for one decoded source.</summary>
    /// <param name="text">The decoded text, after Section 7.4 has removed any byte-order mark.</param>
    public SourceLines(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        this.text = text;

        var found = new List<int> { 0 };

        for (var i = 0; i < text.Length; i++)
        {
            var current = text[i];

            if (current is not ('\r' or '\n'))
            {
                continue;
            }

            if (current == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                i++;
            }

            found.Add(i + 1);
        }

        starts = [.. found];
    }

    /// <summary>The Section 22 column of a host parser's one-based column on a one-based line.</summary>
    /// <param name="line">The one-based line, counted as Section 22 counts lines.</param>
    /// <param name="column">The one-based column, measured in UTF-16 code units.</param>
    /// <returns>
    /// The same position measured in Unicode scalar values. A column beyond the end of the line
    /// keeps advancing one per code unit, and a line this source does not contain is returned
    /// unchanged, because a column invented here would be worse than the host's.
    /// </returns>
    public int ColumnOf(int line, int column)
    {
        if (line < 1 || line > starts.Length || column < 2)
        {
            return column;
        }

        var start = starts[line - 1];
        var end = line < starts.Length ? starts[line] : text.Length;
        var codeUnits = Math.Min(column - 1, end - start);

        return codeUnits < 1
            ? column
            : ScalarColumn.Advance(text.AsSpan(start, codeUnits), codeUnits)
                + 1
                + (column - 1 - codeUnits);
    }
}
