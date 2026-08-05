using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Namespace2Xml.Profiles;

/// <summary>
/// The Appendix A.2 qualified-name lexer.
/// </summary>
/// <remarks>
/// <para>
/// Typed-marker recognition commits, as Section 8.2 requires: a component beginning with an
/// unescaped <c>@</c>, <c>#</c>, or <c>Q{</c> must complete that typed production. The lexer
/// therefore never unreads a component, and every decision it makes is final at the scalar that
/// caused it, which is what lets a fault name one offset.
/// </para>
/// <para>
/// Wildcards are always lexed. Appendix A.2 permits them only where the effective <c>substitute</c>
/// mode enables name interpretation, but that is a property of the context a name appears in rather
/// than of the name's syntax, so it is decided by the caller from
/// <see cref="ContainsWildcard(QualifiedName)"/>. Treating an unescaped <c>*</c> as a literal in
/// those contexts would contradict Section 21, which escapes a literal asterisk unconditionally
/// "rather than dependent on whether a wildcard happens to be active".
/// </para>
/// </remarks>
public static class QualifiedNameLexer
{
    /// <summary>Lexes a qualified name.</summary>
    /// <param name="text">The name's scalars, still escaped.</param>
    public static QualifiedNameResult Lex(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var index = 0;
        return Lex(text, ref index, inReference: false);
    }

    /// <summary>
    /// Lexes an Appendix A.4 <c>reference-name</c>, which ends at the first unescaped <c>}</c>
    /// outside a <c>Q{...}</c> URI.
    /// </summary>
    /// <param name="text">The text containing the reference.</param>
    /// <param name="index">
    /// On entry, the offset just past the opening <c>${</c>. On success, the offset just past the
    /// closing <c>}</c>. Unchanged on failure.
    /// </param>
    public static QualifiedNameResult LexReferenceName(string text, ref int index)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, text.Length);

        var cursor = index;
        var result = Lex(text, ref cursor, inReference: true);

        if (result.Succeeded)
        {
            index = cursor;
        }

        return result;
    }

    private static QualifiedNameResult Lex(string text, ref int index, bool inReference)
    {
        var parts = ImmutableArray.CreateBuilder<NamePart>();

        while (true)
        {
            if (!TryLexPart(text, ref index, inReference, out var part, out var fault))
            {
                return new QualifiedNameResult(fault);
            }

            parts.Add(part);

            if (index >= text.Length)
            {
                return inReference
                    ? new QualifiedNameResult(new NameFault(
                        "this reference has no closing brace; write '\\${' for literal text.",
                        index))
                    : new QualifiedNameResult(new QualifiedName(parts.ToImmutable()));
            }

            if (inReference && text[index] == '}')
            {
                index++;
                return new QualifiedNameResult(new QualifiedName(parts.ToImmutable()));
            }

            // A part stops only at an unescaped delimiter, a reference terminator, or the end.
            index++;
        }
    }

    /// <summary>The wildcard tokens a name contains, in source order.</summary>
    public static IEnumerable<WildcardToken> Wildcards(QualifiedName name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return name.Parts.SelectMany(PartWildcards);
    }

    /// <summary>Whether any component of a name contains a wildcard token.</summary>
    public static bool ContainsWildcard(QualifiedName name) => Wildcards(name).Any();

    private static IEnumerable<WildcardToken> PartWildcards(NamePart part) => part switch
    {
        OrdinaryPart ordinary => ordinary.Tokens.OfType<WildcardToken>(),
        QualifiedElementPart qualified => qualified.Local.OfType<WildcardToken>(),
        AttributePart attribute => PartWildcards(attribute.Name),
        _ => [],
    };

    private static bool TryLexPart(
        string text,
        ref int index,
        bool inReference,
        out NamePart part,
        out NameFault fault)
    {
        part = null!;

        if (inReference && index >= text.Length)
        {
            fault = new NameFault(
                "this reference has no closing brace; write '\\${' for literal text.",
                index);
            return false;
        }

        if (index >= text.Length || text[index] == '.' || (inReference && text[index] == '}'))
        {
            fault = new NameFault(
                "a qualified name has no empty parts, so it cannot begin or end with a delimiter or "
                + "contain two in a row.",
                index);
            return false;
        }

        if (text[index] == '@')
        {
            var markerAt = index;
            index++;

            if (!TryLexXmlNameComponent(text, ref index, inReference, out var name, out fault))
            {
                return false;
            }

            if (name is null)
            {
                fault = new NameFault(
                    "an attribute marker introduces a name, so '@' must be followed by one; write "
                    + "'\\@' for an ordinary part beginning with an at sign.",
                    markerAt);
                return false;
            }

            part = new AttributePart(name);
            return true;
        }

        if (text[index] == '#')
        {
            return TryLexContent(text, ref index, inReference, out part, out fault);
        }

        if (text[index] == 'Q' && index + 1 < text.Length && text[index + 1] == '{')
        {
            if (!TryLexQualifiedElement(text, ref index, inReference, out var qualified, out fault))
            {
                return false;
            }

            part = qualified;
            return true;
        }

        if (!TryLexTokens(text, ref index, inReference, out var tokens, out fault))
        {
            return false;
        }

        part = new OrdinaryPart(tokens);
        return true;
    }

    /// <summary>
    /// Lexes an <c>xml-name-component</c>. Reports a null component, rather than a fault, when the
    /// text at <paramref name="index"/> is empty, so the caller can blame its marker instead.
    /// </summary>
    private static bool TryLexXmlNameComponent(
        string text,
        ref int index,
        bool inReference,
        out XmlNameComponent? component,
        out NameFault fault)
    {
        component = null;
        fault = default;

        if (index >= text.Length || text[index] == '.' || (inReference && text[index] == '}'))
        {
            return true;
        }

        if (text[index] == 'Q' && index + 1 < text.Length && text[index + 1] == '{')
        {
            if (!TryLexQualifiedElement(text, ref index, inReference, out var qualified, out fault))
            {
                return false;
            }

            component = qualified;
            return true;
        }

        if (!TryLexTokens(text, ref index, inReference, out var tokens, out fault))
        {
            return false;
        }

        component = new OrdinaryPart(tokens);
        return true;
    }

    /// <summary>Lexes <c>typed-content</c>: <c>#</c> followed by a canonical decimal.</summary>
    private static bool TryLexContent(
        string text,
        ref int index,
        bool inReference,
        out NamePart part,
        out NameFault fault)
    {
        part = null!;
        var markerAt = index;
        var start = index + 1;
        var end = start;

        while (end < text.Length && char.IsAsciiDigit(text[end]))
        {
            end++;
        }

        var digits = text[start..end];

        // Section 8.2 commits once "#" is recognized, so each of these is an error rather than an
        // ordinary part whose text happens to begin with a number sign.
        if (digits.Length == 0)
        {
            fault = new NameFault(
                "a content-token part is '#' followed by a decimal ordering value; write '\\#' for "
                + "an ordinary part beginning with a number sign.",
                markerAt);
            return false;
        }

        if (digits.Length > 1 && digits[0] == '0')
        {
            fault = new NameFault(
                "a content-token ordering value is written without leading zeros, so '#"
                + digits + "' is not one.",
                start);
            return false;
        }

        if (end < text.Length && text[end] != '.' && !(inReference && text[end] == '}'))
        {
            fault = new NameFault(
                "a content-token part ends after its ordering value; write '\\#' for an ordinary "
                + "part beginning with a number sign.",
                end);
            return false;
        }

        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal))
        {
            fault = new NameFault(
                "this content-token ordering value is larger than any a document can assign.",
                start);
            return false;
        }

        index = end;
        part = new ContentPart(ordinal);
        fault = default;
        return true;
    }

    /// <summary>Lexes <c>qualified-element</c>: <c>Q{</c> URI <c>}</c> local name.</summary>
    private static bool TryLexQualifiedElement(
        string text,
        ref int index,
        bool inReference,
        out QualifiedElementPart part,
        out NameFault fault)
    {
        part = null!;
        var markerAt = index;
        var cursor = index + 2;
        var uri = new StringBuilder();

        while (true)
        {
            if (cursor >= text.Length)
            {
                fault = new NameFault(
                    "this 'Q{' URI has no closing brace; write '\\Q' for an ordinary part beginning "
                    + "with a Q.",
                    markerAt);
                return false;
            }

            var c = text[cursor];

            if (c == '}')
            {
                cursor++;
                break;
            }

            if (c == '\\')
            {
                // Section 11.4: inside the URI only these two escapes exist, and any other
                // backslash sequence is a blocking parse error.
                var next = cursor + 1 < text.Length ? text[cursor + 1] : '\0';
                if (next is not ('}' or '\\'))
                {
                    fault = new NameFault(
                        "inside 'Q{...}' the only escapes are '\\}' and '\\\\'.",
                        cursor);
                    return false;
                }

                uri.Append(next);
                cursor += 2;
                continue;
            }

            if (c is '\n' or '\r')
            {
                fault = new NameFault("a qualified name contains no line terminators.", cursor);
                return false;
            }

            uri.Append(c);
            cursor++;
        }

        index = cursor;

        if (!TryLexTokens(text, ref index, inReference, out var local, out fault))
        {
            return false;
        }

        part = new QualifiedElementPart(uri.ToString(), local);
        return true;
    }

    /// <summary>
    /// Lexes an <c>ordinary-component</c> or <c>local-component</c>, stopping at an unescaped
    /// delimiter or the end of the text.
    /// </summary>
    private static bool TryLexTokens(
        string text,
        ref int index,
        bool inReference,
        out ImmutableArray<NameToken> tokens,
        out NameFault fault)
    {
        tokens = default;
        fault = default;

        var start = index;
        var builder = ImmutableArray.CreateBuilder<NameToken>();
        var literal = new StringBuilder();

        void FlushLiteral()
        {
            if (literal.Length > 0)
            {
                builder.Add(new LiteralToken(literal.ToString()));
                literal.Clear();
            }
        }

        while (index < text.Length)
        {
            var c = text[index];

            if (c == '.')
            {
                break;
            }

            if (inReference && c == '}')
            {
                // Appendix A.4: the first unescaped '}' outside a Q{...} URI ends the reference,
                // even though ordinary-scalar would otherwise admit it.
                break;
            }

            if (c == '\\')
            {
                if (!TryLexEscape(text, ref index, literal, out fault))
                {
                    return false;
                }

                continue;
            }

            if (c == '*')
            {
                FlushLiteral();
                if (!TryLexWildcard(text, ref index, out var wildcard, out fault))
                {
                    return false;
                }

                builder.Add(wildcard);
                continue;
            }

            if (c == '=')
            {
                // Appendix A.2 excludes an unescaped '=' from ordinary-scalar. Section 8.1 has
                // already divided a profile record here, so this is reachable only from a scheme
                // path, a reference, or a root value.
                fault = new NameFault("write an equals sign in a name as '\\='.", index);
                return false;
            }

            if (c is '\n' or '\r')
            {
                fault = new NameFault("a qualified name contains no line terminators.", index);
                return false;
            }

            literal.Append(c);
            index++;
        }

        FlushLiteral();

        if (builder.Count == 0)
        {
            fault = new NameFault(
                "a qualified name has no empty parts, so it cannot begin or end with a delimiter or "
                + "contain two in a row.",
                start);
            return false;
        }

        tokens = builder.ToImmutable();
        return true;
    }

    /// <summary>Lexes one Appendix A.2 <c>name-escape</c>.</summary>
    private static bool TryLexEscape(string text, ref int index, StringBuilder literal, out NameFault fault)
    {
        fault = default;

        if (index + 1 >= text.Length)
        {
            fault = new NameFault("a name ends with a backslash, which escapes nothing.", index);
            return false;
        }

        var escaped = text[index + 1];

        if (escaped == 'u')
        {
            return TryLexUnicodeEscape(text, ref index, literal, out fault);
        }

        if (escaped is not ('.' or '*' or '=' or '#' or '!' or '$' or '@' or '}' or 'Q' or '\\'))
        {
            fault = new NameFault(
                $"'\\{escaped}' is not a name escape; Section 8.2 makes every other backslash "
                + "sequence in a name an error.",
                index);
            return false;
        }

        literal.Append(escaped);
        index += 2;
        return true;
    }

    /// <summary>Lexes <c>unicode-escape</c>: <c>\u{</c> one to six hex digits <c>}</c>.</summary>
    private static bool TryLexUnicodeEscape(string text, ref int index, StringBuilder literal, out NameFault fault)
    {
        var markerAt = index;
        fault = default;

        if (index + 2 >= text.Length || text[index + 2] != '{')
        {
            fault = new NameFault("a '\\u' escape is written '\\u{HEX}'.", markerAt);
            return false;
        }

        var start = index + 3;
        var end = start;

        while (end < text.Length && char.IsAsciiHexDigit(text[end]))
        {
            end++;
        }

        var digits = text[start..end];

        if (digits.Length is 0 or > 6)
        {
            fault = new NameFault("a '\\u{HEX}' escape takes one to six hexadecimal digits.", markerAt);
            return false;
        }

        if (end >= text.Length || text[end] != '}')
        {
            fault = new NameFault("this '\\u{HEX}' escape has no closing brace.", markerAt);
            return false;
        }

        var value = int.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        if (value > 0x10FFFF)
        {
            fault = new NameFault(
                "a '\\u{HEX}' escape encodes one Unicode scalar, and U+10FFFF is the largest.",
                markerAt);
            return false;
        }

        if (value is >= 0xD800 and <= 0xDFFF)
        {
            fault = new NameFault(
                "a '\\u{HEX}' escape encodes one Unicode scalar, and U+D800 through U+DFFF are "
                + "surrogate code points rather than scalars.",
                markerAt);
            return false;
        }

        literal.Append(char.ConvertFromUtf32(value));
        index = end + 1;
        return true;
    }

    /// <summary>Lexes <c>wildcard-token</c>: <c>*</c> with an optional <c>[identifier]</c>.</summary>
    /// <remarks>
    /// Shared with the value lexer, because Sections 12.1 and 12.2 give a value wildcard exactly
    /// this spelling and two copies of one grammar rule drift.
    /// </remarks>
    internal static bool TryLexWildcard(string text, ref int index, out WildcardToken token, out NameFault fault)
    {
        token = null!;
        fault = default;
        index++;

        if (index >= text.Length || text[index] != '[')
        {
            token = new WildcardToken(null);
            return true;
        }

        // Section 8.2 says an identifier "must not be empty", which makes "*[]" an error rather
        // than a bare wildcard followed by an ordinary "[]".
        var bracketAt = index;
        var start = index + 1;
        var end = start;

        while (end < text.Length && (char.IsAsciiLetterOrDigit(text[end]) || text[end] is '_' or '-'))
        {
            end++;
        }

        if (end == start || end >= text.Length || text[end] != ']')
        {
            fault = new NameFault(
                "a wildcard capture is written '*[identifier]', where the identifier is one or more "
                + "ASCII letters, digits, underscores, or hyphens.",
                bracketAt)
            {
                IsWildcardFault = true,
            };
            return false;
        }

        token = new WildcardToken(text[start..end]);
        index = end + 1;
        return true;
    }
}
