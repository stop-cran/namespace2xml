namespace Namespace2Xml.Text;

/// <summary>
/// Column arithmetic in the unit Section 22 defines.
/// </summary>
/// <remarks>
/// <para>
/// Section 22: "Column 1 is the first Unicode scalar value of a line, and each subsequent scalar
/// advances the column by one, so a character outside the Basic Multilingual Plane occupies one
/// column and a tab occupies one column."
/// </para>
/// <para>
/// A .NET <see cref="string"/> is a sequence of UTF-16 code units, so an index into one is not a
/// column: a scalar above U+FFFF is stored as a surrogate pair and would advance a naive column by
/// two. Lexers report a zero-based code-unit offset because that is what they index by; every
/// conversion from such an offset to a Section 22 column goes through this type so the two units
/// cannot be confused at a call site.
/// </para>
/// </remarks>
public static class ScalarColumn
{
    /// <summary>
    /// The number of Unicode scalar values in the first <paramref name="codeUnits"/> UTF-16 code
    /// units of <paramref name="text"/>, which is how far a zero-based lexer offset advances a
    /// Section 22 column.
    /// </summary>
    /// <param name="text">The text the offset indexes.</param>
    /// <param name="codeUnits">A zero-based UTF-16 code-unit offset into <paramref name="text"/>.</param>
    public static int Advance(string text, int codeUnits)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(codeUnits);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(codeUnits, text.Length);

        var scalars = 0;

        for (var i = 0; i < codeUnits; i++)
        {
            // A well-formed surrogate pair is one scalar, counted on its high half. Section 7.4
            // decoding rejects an unpaired surrogate, so the second test never fails on text that
            // reached a lexer; it is here so this stays total for any caller.
            if (char.IsHighSurrogate(text[i]) && i + 1 < codeUnits && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }

            scalars++;
        }

        return scalars;
    }

    /// <summary>
    /// The width of <paramref name="text"/> in Unicode scalar values, which is how many columns it
    /// occupies under Section 22.
    /// </summary>
    /// <param name="text">The text to measure.</param>
    public static int Width(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Advance(text, text.Length);
    }
}
