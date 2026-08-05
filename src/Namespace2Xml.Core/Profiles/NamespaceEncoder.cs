using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Namespace2Xml.Profiles;

/// <summary>
/// Why a name or value has no namespace-output spelling.
/// </summary>
/// <param name="Message">Prose for the <c>SERIALIZE001</c> it earns.</param>
public readonly record struct EncodingFault(string Message);

/// <summary>
/// The Section 19.1 namespace name and value encoder, and the Section 16.4 delimiter
/// disambiguation it uses.
/// </summary>
/// <remarks>
/// <para>
/// Name encoding is total and injective: distinct names encode to distinct text, and with the
/// default delimiter that text lexes back to the name it came from. Escaping is unconditional, so
/// the encoder never has to know whether wildcards are active.
/// </para>
/// <para>
/// Section 16.4 makes namespace output with a delimiter other than <c>.</c> a consumer-oriented
/// projection outside the round-trip guarantee, because namespace input always reads <c>.</c>.
/// </para>
/// </remarks>
public static class NamespaceEncoder
{
    /// <summary>The Section 16.4 default namespace delimiter.</summary>
    public const string DefaultDelimiter = ".";

    /// <summary>Whether a delimiter is legal for namespace output under Section 16.4.</summary>
    /// <param name="delimiter">The delimiter to check.</param>
    /// <param name="reason">Why it is illegal, or <see langword="null"/> when it is legal.</param>
    public static bool IsValidDelimiter(string delimiter, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(delimiter);

        if (delimiter.Length == 0)
        {
            reason = "an empty delimiter is invalid.";
            return false;
        }

        if (ContainsIllegalDelimiterScalar(delimiter))
        {
            reason =
                "a namespace delimiter contains no '=', no backslash, and no character from the "
                + "Section 19.1 forbidden set, because a delimiter is emitted literally between "
                + "parts, where those characters would break the record.";
            return false;
        }

        if (delimiter.All(c => c is 'u' or '{' or '}' || char.IsAsciiDigit(c) || c is >= 'A' and <= 'F'))
        {
            // Such a delimiter occurs inside the \u{HEX} escapes this encoder emits, so a consumer
            // splitting the joined path would split inside an escape.
            reason =
                "a namespace delimiter is not built only from 'u', braces, and upper-case hexadecimal "
                + "digits, because that text occurs inside the '\\u{HEX}' escapes namespace output "
                + "emits.";
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>Encodes a qualified name for namespace output.</summary>
    /// <param name="name">The name to encode.</param>
    /// <param name="delimiter">The configured delimiter.</param>
    /// <param name="text">The encoded text, when encoding succeeds.</param>
    /// <param name="fault">Why the name has no spelling, when encoding fails.</param>
    public static bool TryEncodeName(
        QualifiedName name,
        string delimiter,
        out string? text,
        out EncodingFault fault)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(delimiter);

        text = null;
        fault = default;

        var builder = new StringBuilder();

        for (var i = 0; i < name.Parts.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(delimiter);
            }

            if (!TryEncodePart(name.Parts[i], delimiter, first: i == 0, builder, out fault))
            {
                return false;
            }
        }

        text = builder.ToString();
        return true;
    }

    /// <summary>Encodes an interpreted value for namespace output.</summary>
    /// <param name="value">The value to encode.</param>
    /// <param name="delimiter">The configured delimiter, which reference names also use.</param>
    /// <param name="text">The encoded text, when encoding succeeds.</param>
    /// <param name="fault">Why the value has no spelling, when encoding fails.</param>
    public static bool TryEncodeValue(
        InterpretedValue value,
        string delimiter,
        out string? text,
        out EncodingFault fault)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(delimiter);

        text = null;
        fault = default;

        var builder = new StringBuilder();

        foreach (var token in value.Tokens)
        {
            switch (token)
            {
                case LiteralValueToken literal:
                    if (!TryEncodeValueLiteral(literal.Text, builder, out fault))
                    {
                        return false;
                    }

                    break;

                case ValueWildcardToken wildcard:
                    AppendWildcard(wildcard.CaptureId, builder);
                    break;

                case ReferenceToken reference:
                    if (!TryEncodeName(reference.Name, delimiter, out var referenced, out fault))
                    {
                        return false;
                    }

                    builder.Append("${").Append(referenced).Append('}');
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(value));
            }
        }

        text = builder.ToString();
        return true;
    }

    /// <summary>Section 19.1's value escapes: the inverse of the namespace value lexer.</summary>
    private static bool TryEncodeValueLiteral(string literal, StringBuilder builder, out EncodingFault fault)
    {
        fault = default;
        var index = 0;

        while (index < literal.Length)
        {
            if (!TryDecodeScalar(literal, index, out var scalar, out var width, out fault))
            {
                return false;
            }

            switch (scalar)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '*':
                    builder.Append("\\*");
                    break;
                case '$' when index + 1 < literal.Length && literal[index + 1] == '{':
                    builder.Append("\\${");
                    index++;
                    break;
                default:
                    builder.Append(literal, index, width);
                    break;
            }

            index += width;
        }

        return true;
    }

    private static bool TryEncodePart(
        NamePart part,
        string delimiter,
        bool first,
        StringBuilder builder,
        out EncodingFault fault)
    {
        fault = default;

        switch (part)
        {
            case ContentPart content:
                if (first)
                {
                    fault = new EncodingFault(
                        "a content-token part cannot be the first namespace path part, because a "
                        + "record beginning with '#' is a comment; give the view a 'root' that places "
                        + "an ordinary part before it.");
                    return false;
                }

                builder.Append('#').Append(content.Ordinal.ToString(CultureInfo.InvariantCulture));
                return true;

            case AttributePart attribute:
                builder.Append('@');
                return TryEncodeXmlName(attribute.Name, delimiter, builder, out fault);

            case XmlNameComponent component:
                return TryEncodeXmlName(component, delimiter, builder, out fault);

            default:
                throw new ArgumentOutOfRangeException(nameof(part));
        }
    }

    private static bool TryEncodeXmlName(
        XmlNameComponent component,
        string delimiter,
        StringBuilder builder,
        out EncodingFault fault)
    {
        fault = default;

        switch (component)
        {
            case QualifiedElementPart qualified:
                builder.Append("Q{");
                if (!TryEncodeUri(qualified.Uri, builder, out fault))
                {
                    return false;
                }

                builder.Append('}');
                return TryEncodeTokens(qualified.Local, delimiter, ordinary: false, builder, out fault);

            case OrdinaryPart ordinary:
                return TryEncodeTokens(ordinary.Tokens, delimiter, ordinary: true, builder, out fault);

            default:
                throw new ArgumentOutOfRangeException(nameof(component));
        }
    }

    /// <summary>
    /// Section 19.1: inside a URI only <c>}</c> and backslash are escaped and everything else is
    /// literal, which leaves a forbidden scalar there with no spelling at all.
    /// </summary>
    private static bool TryEncodeUri(string uri, StringBuilder builder, out EncodingFault fault)
    {
        fault = default;
        var index = 0;

        while (index < uri.Length)
        {
            if (!TryDecodeScalar(uri, index, out var scalar, out var width, out fault))
            {
                return false;
            }

            if (IsForbidden(scalar))
            {
                fault = new EncodingFault(
                    "this namespace URI contains a character namespace output cannot spell: "
                    + "Section 11.4 admits only '\\}' and '\\\\' inside 'Q{...}', so a '\\u{HEX}' "
                    + "escape there would not read back.");
                return false;
            }

            switch (scalar)
            {
                case '}':
                    builder.Append("\\}");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                default:
                    builder.Append(uri, index, width);
                    break;
            }

            index += width;
        }

        return true;
    }

    private static bool TryEncodeTokens(
        ImmutableArray<NameToken> tokens,
        string delimiter,
        bool ordinary,
        StringBuilder builder,
        out EncodingFault fault)
    {
        fault = default;

        for (var t = 0; t < tokens.Length; t++)
        {
            switch (tokens[t])
            {
                case WildcardToken wildcard:
                    AppendWildcard(wildcard.CaptureId, builder);
                    break;

                case LiteralToken literal:
                    // Only the first scalar of the first token of an ordinary component can be a
                    // leading typed marker.
                    if (!TryEncodeNameLiteral(
                            literal.Text,
                            delimiter,
                            marker: ordinary && t == 0,
                            builder,
                            out fault))
                    {
                        return false;
                    }

                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(tokens));
            }
        }

        return true;
    }

    private static bool TryEncodeNameLiteral(
        string literal,
        string delimiter,
        bool marker,
        StringBuilder builder,
        out EncodingFault fault)
    {
        fault = default;
        var index = 0;

        while (index < literal.Length)
        {
            if (!TryDecodeScalar(literal, index, out var scalar, out var width, out fault))
            {
                return false;
            }

            // Section 16.4 runs first and always wins, so the default delimiter emits \u{2E} rather
            // than the \. Section 8.2 would otherwise supply. The scan advances one scalar, so
            // overlapping occurrences are each escaped.
            if (OccursAt(literal, index, delimiter))
            {
                AppendUnicodeEscape(scalar, builder);
                index += width;
                continue;
            }

            if (IsForbidden(scalar))
            {
                AppendUnicodeEscape(scalar, builder);
                index += width;
                continue;
            }

            switch (scalar)
            {
                case '\\':
                    builder.Append("\\\\");
                    index += width;
                    continue;
                case '*':
                    builder.Append("\\*");
                    index += width;
                    continue;
                case '=':
                    builder.Append("\\=");
                    index += width;
                    continue;
                case '}':
                    // Ordinary in a name, but Appendix A.4 makes it terminate a reference name, and
                    // Section 19.1 escapes unconditionally rather than by context.
                    builder.Append("\\}");
                    index += width;
                    continue;
                case '$' when index + 1 < literal.Length && literal[index + 1] == '{':
                    builder.Append("\\$");
                    index += width;
                    continue;
            }

            if (index == 0 && marker && scalar is '@' or '#')
            {
                builder.Append('\\').Append((char)scalar);
                index += width;
                continue;
            }

            if (index == 0 && marker && scalar == 'Q' && literal.Length > 1 && literal[1] == '{')
            {
                builder.Append("\\Q");
                index += width;
                continue;
            }

            // Section 19.1: "could begin a physical record" means everything emitted before it in
            // this record is spaces and tabs.
            if (scalar is '!' or '#' && AllSpacesAndTabs(builder))
            {
                builder.Append('\\').Append((char)scalar);
                index += width;
                continue;
            }

            builder.Append(literal, index, width);
            index += width;
        }

        return true;
    }

    private static void AppendWildcard(string? captureId, StringBuilder builder)
    {
        builder.Append('*');

        if (captureId is not null)
        {
            builder.Append('[').Append(captureId).Append(']');
        }
    }

    private static bool ContainsIllegalDelimiterScalar(string delimiter)
    {
        var index = 0;

        while (index < delimiter.Length)
        {
            // An unpaired surrogate decodes to no scalar at all, and Cs is forbidden either way.
            if (Rune.DecodeFromUtf16(delimiter.AsSpan(index), out var rune, out var width)
                != OperationStatus.Done)
            {
                return true;
            }

            if (rune.Value is '=' or '\\' || IsForbidden(rune.Value))
            {
                return true;
            }

            index += width;
        }

        return false;
    }

    private static bool TryDecodeScalar(
        string text,
        int index,
        out int scalar,
        out int width,
        out EncodingFault fault)
    {
        if (Rune.DecodeFromUtf16(text.AsSpan(index), out var rune, out width) != OperationStatus.Done)
        {
            scalar = 0;
            fault = new EncodingFault(
                "this name contains an unpaired surrogate, which Appendix A.2 excludes from "
                + "'\\u{HEX}', so namespace output cannot spell it.");
            return false;
        }

        scalar = rune.Value;
        fault = default;
        return true;
    }

    private static bool AllSpacesAndTabs(StringBuilder builder)
    {
        for (var i = 0; i < builder.Length; i++)
        {
            if (builder[i] is not (' ' or '\t'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool OccursAt(string text, int index, string delimiter) =>
        index + delimiter.Length <= text.Length
        && string.CompareOrdinal(text, index, delimiter, 0, delimiter.Length) == 0;

    private static bool IsForbidden(int scalar) =>
        scalar is 0x85 or 0x2028 or 0x2029
        || Rune.GetUnicodeCategory(new Rune(scalar))
            is UnicodeCategory.Control or UnicodeCategory.Format;

    private static void AppendUnicodeEscape(int scalar, StringBuilder builder) =>
        builder
            .Append("\\u{")
            .Append(scalar.ToString("X", CultureInfo.InvariantCulture))
            .Append('}');
}
