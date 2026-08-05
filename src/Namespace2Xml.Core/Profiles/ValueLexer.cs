using System.Collections.Immutable;
using System.Text;

namespace Namespace2Xml.Profiles;

/// <summary>Which escape table a value uses.</summary>
public enum ValueEscapeStyle
{
    /// <summary>
    /// Appendix A.3, for namespace-profile values: an escape consumes the backslash and the scalar
    /// after it, and an unrecognized one preserves both.
    /// </summary>
    NamespaceProfile,

    /// <summary>
    /// Appendix A.5, for JSON, YAML, and XML strings a native parser has already decoded: only
    /// <c>\*</c> and <c>\${</c> are escapes, and any other backslash emits itself and consumes
    /// nothing after it.
    /// </summary>
    NativeString,
}

/// <summary>
/// How a value is to be interpreted: everything the Appendix A.3 pass needs that is not in the text.
/// </summary>
/// <param name="Escapes">Which escape table applies.</param>
/// <param name="InterpretReferences">
/// Whether an unescaped <c>${</c> begins a Section 8.4 reference. False under <c>substitute=Key</c>
/// and <c>substitute=None</c>, where Section 13.4 still decodes lexical escapes.
/// </param>
/// <param name="InterpretWildcards">
/// Whether an unescaped <c>*</c> is a Section 12.1 or 12.2 capture substitution. Section 12.1
/// decides this before lexing, from the owning name's captures and the effective <c>substitute</c>
/// mode; when it is false, <c>*</c> and <c>*[</c> are literal text, so a glob such as
/// <c>/opt/x*[0-9]/y</c> needs no escape.
/// </param>
public readonly record struct ValueSyntax(
    ValueEscapeStyle Escapes,
    bool InterpretReferences,
    bool InterpretWildcards)
{
    /// <summary>A namespace-profile value in an entry that interprets everything.</summary>
    public static ValueSyntax Profile { get; } =
        new(ValueEscapeStyle.NamespaceProfile, InterpretReferences: true, InterpretWildcards: true);

    /// <summary>
    /// A namespace-profile value whose name defines no captures: escapes and references are still
    /// interpreted, and <c>*</c> is literal text.
    /// </summary>
    public static ValueSyntax ProfileWithoutCaptures { get; } =
        new(ValueEscapeStyle.NamespaceProfile, InterpretReferences: true, InterpretWildcards: false);

    /// <summary>
    /// A namespace-profile value under <c>substitute=Key</c> or <c>substitute=None</c>: Section 13.4
    /// still decodes profile escapes, and nothing else is interpreted.
    /// </summary>
    public static ValueSyntax ProfileUninterpreted { get; } =
        new(ValueEscapeStyle.NamespaceProfile, InterpretReferences: false, InterpretWildcards: false);

    /// <summary>A decoded native string in an entry that interprets everything.</summary>
    /// <remarks>
    /// Section 13.4 preserves a native string exactly under <c>Key</c> and <c>None</c>, so there is
    /// no uninterpreted native counterpart: that value is never lexed at all.
    /// </remarks>
    public static ValueSyntax NativeString { get; } =
        new(ValueEscapeStyle.NativeString, InterpretReferences: true, InterpretWildcards: true);
}

/// <summary>
/// The Appendix A.3 value lexer, and the Appendix A.5 transducer for decoded native strings.
/// </summary>
/// <remarks>
/// One left-to-right pass. At each position it tries an escape, longest first; then <c>${</c>, which
/// must begin a valid reference; then a wildcard token; and only then treats the scalar as literal
/// text. Emitted text is never rescanned, which is the whole reason <c>\${</c> and <c>\*</c> work.
/// </remarks>
public static class ValueLexer
{
    /// <summary>Lexes a value.</summary>
    /// <param name="text">The value's scalars, still escaped.</param>
    /// <param name="syntax">How the value is to be interpreted.</param>
    public static ValueResult Lex(string text, ValueSyntax syntax)
    {
        ArgumentNullException.ThrowIfNull(text);

        var tokens = ImmutableArray.CreateBuilder<ValueToken>();
        var literal = new StringBuilder();
        var index = 0;

        void FlushLiteral()
        {
            if (literal.Length > 0)
            {
                tokens.Add(new LiteralValueToken(literal.ToString()));
                literal.Clear();
            }
        }

        while (index < text.Length)
        {
            var c = text[index];

            if (c == '\\')
            {
                LexEscape(text, ref index, syntax.Escapes, literal);
                continue;
            }

            if (syntax.InterpretReferences && c == '$' && index + 1 < text.Length && text[index + 1] == '{')
            {
                FlushLiteral();
                if (!TryLexReference(text, ref index, out var reference, out var referenceFault))
                {
                    return new ValueResult(referenceFault);
                }

                tokens.Add(reference);
                continue;
            }

            if (syntax.InterpretWildcards && c == '*')
            {
                FlushLiteral();
                if (!QualifiedNameLexer.TryLexWildcard(text, ref index, out var wildcard, out var nameFault))
                {
                    return new ValueResult(
                        new ValueFault(nameFault.Message, nameFault.Offset, ValueFaultKind.Wildcard));
                }

                tokens.Add(new ValueWildcardToken(wildcard.CaptureId));
                continue;
            }

            literal.Append(c);
            index++;
        }

        FlushLiteral();
        return new ValueResult(new InterpretedValue(tokens.ToImmutable()));
    }

    private static void LexEscape(string text, ref int index, ValueEscapeStyle style, StringBuilder literal)
    {
        // Both tables recognize "\${" and both prefer it to the two-scalar forms, so this is the
        // longest-token-first step Appendix A.3 requires.
        if (index + 2 < text.Length && text[index + 1] == '$' && text[index + 2] == '{')
        {
            literal.Append("${");
            index += 3;
            return;
        }

        if (index + 1 >= text.Length)
        {
            // Appendix A.3: a trailing backslash matches value-scalar and emits itself. Appendix
            // A.5 rule 4 reaches the same place from the other direction.
            literal.Append('\\');
            index++;
            return;
        }

        var escaped = text[index + 1];

        if (style == ValueEscapeStyle.NativeString)
        {
            if (escaped == '*')
            {
                literal.Append('*');
                index += 2;
                return;
            }

            // Rule 4: any other backslash emits itself and consumes no following scalar, so the
            // scalar after it is processed normally rather than swallowed.
            literal.Append('\\');
            index++;
            return;
        }

        switch (escaped)
        {
            case '\\':
                literal.Append('\\');
                break;
            case '*':
                literal.Append('*');
                break;
            case 'n':
                literal.Append('\n');
                break;
            case 'r':
                literal.Append('\r');
                break;
            case 't':
                literal.Append('\t');
                break;
            default:
                // unknown-value-escape preserves both the backslash and the following scalar.
                literal.Append('\\').Append(escaped);
                break;
        }

        index += 2;
    }

    private static bool TryLexReference(
        string text,
        ref int index,
        out ReferenceToken token,
        out ValueFault fault)
    {
        token = null!;
        var markerAt = index;
        var cursor = index + 2;
        var result = QualifiedNameLexer.LexReferenceName(text, ref cursor);

        if (result.Fault is { } nameFault)
        {
            fault = new ValueFault(nameFault.Message, nameFault.Offset, ValueFaultKind.Reference);
            return false;
        }

        var name = result.Name!;

        if (QualifiedNameLexer.Wildcards(name).Any(wildcard => wildcard.CaptureId is null))
        {
            // Section 12.1 and Appendix A.4: a legacy unnamed capture inside a reference is not
            // supported, and Section 13.3 makes a free wildcard reference blocking anyway.
            fault = new ValueFault(
                "a reference cannot contain a bare '*'; a reference from a template must name the "
                + "capture it substitutes, as '*[identifier]'.",
                markerAt,
                ValueFaultKind.Reference);
            return false;
        }

        index = cursor;
        token = new ReferenceToken(name);
        fault = default;
        return true;
    }
}
