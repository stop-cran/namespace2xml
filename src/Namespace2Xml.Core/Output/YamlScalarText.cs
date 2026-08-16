using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Namespace2Xml.Output;

/// <summary>
/// Spells a string as a Section 19.4 YAML scalar.
/// </summary>
/// <remarks>
/// <para>
/// Section 19.4 gives one explicit rule: "a string whose plain spelling would resolve to a
/// non-string kind under <c>RestrictedYaml1</c> is emitted single-quoted, with a literal single
/// quote doubled as <c>''</c>". That rule is about <em>meaning</em>. A plain scalar must also be
/// <em>syntactically</em> plain, and the two conditions are checked together here because a
/// serializer that honoured only the first would emit files no YAML parser accepts.
/// </para>
/// <para>
/// The plain test is deliberately conservative: a leading indicator character disqualifies plain
/// spelling whether or not that particular position is ambiguous in block context. Quoting one
/// scalar too many costs a pair of quotes; quoting one too few costs a wrong file, and the
/// conservative rule is the one that does not depend on remembering every YAML context rule
/// correctly.
/// </para>
/// </remarks>
internal static partial class YamlScalarText
{
    private const string LeadingIndicators = "-?:,[]{}#&*!|>'\"%@`";

    /// <summary>
    /// U+FEFF. YAML admits a byte order mark only at the start of a stream, and Section 24 requires
    /// UTF-8 "without a BOM", so this character can never be written as itself: a reader either
    /// rejects the file or silently discards the character. It is escaped in every spelling.
    /// </summary>
    private const char ByteOrderMark = '\uFEFF';

    /// <summary>
    /// Whether the character is one of YAML's non-ASCII line breaks. U+2028 and U+2029 are
    /// categorized as separators rather than controls, so <see cref="char.IsControl(char)"/> does
    /// not report them, yet a YAML reader normalizes them exactly as it normalizes LF. Written
    /// literally they end the line they sit in, which corrupts every spelling that is not escaped.
    /// U+0085 is a C1 control and is already covered by the control tests.
    /// </summary>
    /// <param name="unit">The character to test.</param>
    private static bool IsLineSeparator(char unit) => unit is '\u2028' or '\u2029';

    /// <summary>
    /// Whether the text contains a surrogate code unit that is not part of a valid pair. Such a
    /// unit is not a Unicode scalar value, so no YAML spelling can carry it faithfully; it is
    /// escaped so the output stays well formed rather than being emitted as invalid UTF-8.
    /// </summary>
    /// <param name="text">The string value.</param>
    internal static bool HasLoneSurrogate(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (!char.IsSurrogate(text[index]))
            {
                continue;
            }

            if (!char.IsHighSurrogate(text[index])
                || index + 1 >= text.Length
                || !char.IsLowSurrogate(text[index + 1]))
            {
                return true;
            }

            index++;
        }

        return false;
    }

    /// <summary>
    /// Whether a plain spelling of this text would resolve to something other than a string under
    /// the Section 10.1 <c>RestrictedYaml1</c> schema.
    /// </summary>
    /// <param name="text">The string value.</param>
    public static bool ResolvesToNonString(string text) =>
        text.Length == 0
        || text == "~"
        || text is "null" or "Null" or "NULL"
        || IsBoolean(text)
        || IsJsonNumber(text);

    /// <summary>Section 10.1: "any ASCII case spelling of <c>true</c> or <c>false</c>".</summary>
    private static bool IsBoolean(string text) =>
        string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Section 10.1: "JSON-compatible decimal integers and floating-point values are numeric",
    /// while "<c>+1</c>, <c>.5</c>, and <c>1.</c> remain strings because they are not JSON-compatible
    /// numbers".
    /// </summary>
    /// <param name="text">The string value.</param>
    public static bool IsJsonNumber(string text)
    {
        var index = 0;

        if (index < text.Length && text[index] == '-')
        {
            index++;
        }

        if (!TryDigits(text, ref index, out var digits))
        {
            return false;
        }

        // JSON forbids a leading zero on a multi-digit integer part.
        if (digits > 1 && text[index - digits] == '0')
        {
            return false;
        }

        if (index < text.Length && text[index] == '.')
        {
            index++;

            if (!TryDigits(text, ref index, out _))
            {
                return false;
            }
        }

        if (index < text.Length && (text[index] == 'e' || text[index] == 'E'))
        {
            index++;

            if (index < text.Length && (text[index] == '+' || text[index] == '-'))
            {
                index++;
            }

            if (!TryDigits(text, ref index, out _))
            {
                return false;
            }
        }

        return index == text.Length;
    }

    private static bool TryDigits(string text, ref int index, out int count)
    {
        var start = index;

        while (index < text.Length && text[index] is >= '0' and <= '9')
        {
            index++;
        }

        count = index - start;

        return count > 0;
    }

    /// <summary>
    /// Section 19.4's <em>portably typed</em> words: the boolean and null spellings, folded by
    /// ASCII case as Section 1 requires of every case-insensitive comparison in this contract.
    /// </summary>
    private static readonly string[] PortableWords =
        ["y", "n", "yes", "no", "on", "off", "true", "false", "null"];

    /// <summary>
    /// Whether the text is one of Section 19.4's <em>portably typed</em> spellings: a scalar that a
    /// conforming YAML reader resolves to something other than a string, under either the YAML 1.2
    /// Core Schema or the YAML 1.1 type repository.
    /// </summary>
    /// <param name="text">The string value.</param>
    /// <remarks>
    /// <para>
    /// This is deliberately not <see cref="ResolvesToNonString"/>. That method answers what
    /// <c>RestrictedYaml1</c> resolves, which is what <em>this tool</em> reads back, and Section 10
    /// says outright that the schema is "intentionally not the complete YAML 1.2 Core Schema".
    /// Quoting on that answer under-quotes by exactly the difference between the two, and the
    /// difference cannot be seen from inside the tool: a same-format round trip through this writer
    /// and this reader agrees with itself while every other reader disagrees.
    /// </para>
    /// <para>
    /// The set is the union of the two published schemas rather than a choice between them, because
    /// they disagree with each other as well as with this one: <c>010</c> is 8 to a YAML 1.1 reader
    /// and 10 to a YAML 1.2 one, and <c>0o17</c> is a string to the first and 15 to the second.
    /// </para>
    /// <para>
    /// <c>&lt;&lt;</c> and <c>=</c> are here for a heavier reason than a changed value. A YAML 1.1
    /// reader resolves them to the merge and value tags, and a reader with no constructor for those
    /// tags -- the default in the most widely deployed implementations -- abandons the entire
    /// document rather than that one node, so every sibling entry is lost with them.
    /// </para>
    /// </remarks>
    public static bool IsPortablyTyped(string text)
    {
        if (text.Length == 0 || text is "~" or "<<" or "=")
        {
            return true;
        }

        foreach (var word in PortableWords)
        {
            if (Ascii.EqualsIgnoreCase(text, word))
            {
                return true;
            }
        }

        return PortableInteger().IsMatch(text)
            || PortableFloat().IsMatch(text)
            || PortableTimestamp().IsMatch(text);
    }

    /// <summary>Section 19.4's portably typed integer productions.</summary>
    [GeneratedRegex(
        @"\A[+-]?(?:[0-9][0-9_]*|0b[01_]+|0[0-7_]+|0o[0-7]+|0x[0-9A-Fa-f_]+|[0-9][0-9_]*(?::[0-5]?[0-9])+)\z",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex PortableInteger();

    /// <summary>
    /// Section 19.4's portably typed float productions. Every one requires at least one digit, so
    /// <c>.</c>, <c>._</c>, and <c>+.</c> stay plain: no schema types them.
    /// </summary>
    [GeneratedRegex(
        @"\A(?:[+-]?(?:[0-9][0-9_]*\.[0-9_]*(?:[eE][+-]?[0-9]+)?|\.[0-9][0-9_]*(?:[eE][+-]?[0-9]+)?|[0-9][0-9_]*[eE][+-]?[0-9]+|[0-9][0-9_]*(?::[0-5]?[0-9])+\.[0-9_]*|\.(?:inf|Inf|INF))|\.(?:nan|NaN|NAN))\z",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex PortableFloat();

    /// <summary>Section 19.4's portably typed timestamp productions.</summary>
    [GeneratedRegex(
        @"\A(?:[0-9]{4}-[0-9]{2}-[0-9]{2}|[0-9]{4}-[0-9]{1,2}-[0-9]{1,2}(?:[Tt]|[ \t]+)[0-9]{1,2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]*)?(?:[ \t]*(?:Z|[+-][0-9]{1,2}(?::[0-9]{2})?))?)\z",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex PortableTimestamp();

    /// <summary>Whether the text may be written as a plain scalar.</summary>
    /// <param name="text">The string value.</param>
    public static bool IsPlainSafe(string text)
    {
        if (text.Length == 0 || ResolvesToNonString(text) || IsPortablyTyped(text))
        {
            return false;
        }

        if (text[0] == ' ' || text[^1] == ' ' || text[^1] == ':')
        {
            return false;
        }

        if (LeadingIndicators.Contains(text[0], StringComparison.Ordinal))
        {
            return false;
        }

        // A line whose content begins "..." is the document-end marker, and a bare scalar occupies
        // a line of its own, so plain spelling would turn the value into an empty document.
        if (text.StartsWith("...", StringComparison.Ordinal))
        {
            return false;
        }

        if (HasLoneSurrogate(text))
        {
            return false;
        }

        for (var index = 0; index < text.Length; index++)
        {
            var unit = text[index];

            if (unit is '\n' or '\r' or '\t' || unit == ByteOrderMark || IsLineSeparator(unit)
                || char.IsControl(unit))
            {
                return false;
            }

            if (unit is '[' or ']' or '{' or '}' or ',')
            {
                return false;
            }

            if (unit == ':' && index + 1 < text.Length && text[index + 1] == ' ')
            {
                return false;
            }

            if (unit == '#' && index > 0 && text[index - 1] == ' ')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether every scalar in the text is a YAML <c>c-printable</c> character other than a line
    /// break, which is what a single-quoted scalar can carry without folding or escapes.
    /// </summary>
    /// <param name="text">The string value.</param>
    public static bool CanSingleQuote(string text)
    {
        if (HasLoneSurrogate(text))
        {
            return false;
        }

        foreach (var unit in text)
        {
            if (unit == '\t')
            {
                continue;
            }

            if (char.IsSurrogate(unit))
            {
                continue;
            }

            if (unit is '\n' or '\r' || unit == ByteOrderMark || IsLineSeparator(unit)
                || char.IsControl(unit))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Section 19.4's single-quoted form, "with a literal single quote doubled as <c>''</c>".</summary>
    /// <param name="text">The string value.</param>
    public static string SingleQuote(string text) =>
        $"'{text.Replace("'", "''", StringComparison.Ordinal)}'";

    /// <summary>
    /// The double-quoted form, which is the only YAML spelling that can carry text a literal block
    /// scalar and a single-quoted scalar both refuse.
    /// </summary>
    /// <param name="text">The string value.</param>
    public static string DoubleQuote(string text)
    {
        var builder = new StringBuilder(text.Length + 2);

        builder.Append('"');

        for (var index = 0; index < text.Length; index++)
        {
            var unit = text[index];

            if (unit == '"')
            {
                builder.Append("\\\"");
            }
            else if (unit == '\\')
            {
                builder.Append("\\\\");
            }
            else if (unit == '\n')
            {
                builder.Append("\\n");
            }
            else if (unit == '\r')
            {
                builder.Append("\\r");
            }
            else if (unit == '\t')
            {
                builder.Append("\\t");
            }
            else if (char.IsHighSurrogate(unit)
                && index + 1 < text.Length
                && char.IsLowSurrogate(text[index + 1]))
            {
                // A supplementary character is one Unicode scalar value and is written as itself,
                // exactly as any other non-ASCII character is. Splitting it into two \u escapes
                // would spell two unpaired surrogates, which is a different string.
                builder.Append(unit).Append(text[index + 1]);
                index++;
            }
            else if (char.IsControl(unit) || char.IsSurrogate(unit) || unit == ByteOrderMark
                || IsLineSeparator(unit))
            {
                builder
                    .Append("\\u")
                    .Append(((int)unit).ToString("X4", CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append(unit);
            }
        }

        builder.Append('"');

        return builder.ToString();
    }

    /// <summary>
    /// The single-line spelling of a string: plain when that is both safe and unambiguous, single
    /// quoted when it can be, and double quoted otherwise.
    /// </summary>
    /// <param name="text">The string value.</param>
    public static string Spell(string text) =>
        IsPlainSafe(text) ? text
        : CanSingleQuote(text) ? SingleQuote(text)
        : DoubleQuote(text);

    /// <summary>
    /// Whether Section 19.4's literal block scalar can carry this multiline value byte for byte.
    /// </summary>
    /// <param name="text">The string value.</param>
    /// <remarks>
    /// <para>
    /// A literal block scalar reproduces its content lines exactly, which makes it lossless only
    /// when every line survives the round trip. A carriage return, a control character, or trailing
    /// white space on a line does not: parsers differ on whether such a line keeps its trailing
    /// spaces, and a CR would be read back as a line break. Leading white space on the block's
    /// <em>first non-empty</em> line is refused too, because a reader detects the block's
    /// indentation from that line and would absorb the space rather than return it. Checking the
    /// first character instead would miss a value such as <c>"\n leading"</c>, whose first line is
    /// empty. Those values are double quoted instead, which is uglier and exact.
    /// </para>
    /// <para>
    /// A value ending in a blank line is refused for a different reason. Carrying that line needs
    /// the keep indicator <c>|+</c>, whose content then ends with two physical LFs; when the value
    /// is the last thing in the document, the file does too, and Section 24 requires a text output
    /// to "end with exactly one LF". Refusing only in that position would make the spelling depend
    /// on where the value happened to sort, so that adding an unrelated key changed how an
    /// untouched value was written. The double-quoted form spells the trailing breaks as <c>\n</c>
    /// escapes, ends the file with exactly one LF, and reads back identically everywhere.
    /// </para>
    /// </remarks>
    public static bool CanBlock(string text)
    {
        if (!text.Contains('\n', StringComparison.Ordinal))
        {
            return false;
        }

        if (text.EndsWith("\n\n", StringComparison.Ordinal))
        {
            return false;
        }

        if (HasLoneSurrogate(text))
        {
            return false;
        }

        foreach (var unit in text)
        {
            if (unit == ByteOrderMark || IsLineSeparator(unit))
            {
                return false;
            }

            if (unit is not '\n' && !char.IsSurrogate(unit) && char.IsControl(unit))
            {
                return false;
            }
        }

        foreach (var line in text.Split('\n'))
        {
            if (line.Length > 0 && (line[^1] == ' ' || line[^1] == '\t'))
            {
                return false;
            }
        }

        foreach (var line in text.Split('\n'))
        {
            if (line.Length == 0)
            {
                continue;
            }

            return line[0] != ' ' && line[0] != '\t';
        }

        return true;
    }
}
