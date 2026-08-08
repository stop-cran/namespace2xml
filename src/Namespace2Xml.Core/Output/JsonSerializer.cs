using System.Globalization;
using System.Text;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;

namespace Namespace2Xml.Output;

/// <summary>Serializes Section 19.3 JSON output.</summary>
/// <remarks>
/// <para>
/// The writer is hand-written rather than delegating to a JSON library because Section 19.3 fixes
/// bytes a library chooses for itself: two-space indentation, uppercase hexadecimal escapes, an
/// <c>EscapeNonAscii</c> mode that is independent of layout, and arbitrary-precision numbers that
/// must reach the file in their Section 18 canonical spelling rather than through a
/// <see cref="double"/>.
/// </para>
/// <para>
/// Section 19.3 renders comments nowhere. They are counted rather than dropped silently, because
/// the section also requires "a summarized discard warning when comments exist", and Section 22
/// counts <c>WARN003</c> once per feature category and output file.
/// </para>
/// </remarks>
public sealed class JsonSerializer
{
    private readonly JsonOutputOptions options;
    private readonly DiagnosticBuffer diagnostics;
    private readonly DestinationRef? destination;
    private readonly StringBuilder scratch = new();

    private int discardedComments;

    /// <summary>Creates a serializer.</summary>
    /// <param name="options">The Section 16.9 JSON options.</param>
    /// <param name="diagnostics">The buffer discard warnings accumulate in.</param>
    /// <param name="destination">The Section 6.4.3 <c>destination</c> this instance writes to.</param>
    public JsonSerializer(
        JsonOutputOptions options,
        DiagnosticBuffer diagnostics,
        DestinationRef? destination = null)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        this.options = options;
        this.diagnostics = diagnostics;
        this.destination = destination;
    }

    /// <summary>Writes the document.</summary>
    /// <param name="document">The projected document root.</param>
    /// <param name="writer">The buffer to write into.</param>
    /// <returns>
    /// Whether the whole output was written. A false result is a budget crossing the caller reads
    /// from the writer's fault.
    /// </returns>
    public bool TrySerialize(DocumentNode document, OutputBufferWriter writer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryWriteNode(document, 0, writer))
        {
            return false;
        }

        // Section 24: a text output with content ends with exactly one LF. The document itself
        // never ends in one, in either layout mode.
        if (!writer.TryWrite("\n"u8))
        {
            return false;
        }

        ReportDiscardedComments();

        return true;
    }

    private bool TryWriteNode(DocumentNode node, int depth, OutputBufferWriter writer)
    {
        discardedComments += node.Comments.Length;

        return node switch
        {
            DocumentScalar scalar => TryWriteScalar(scalar.Payload, writer),
            DocumentSequence sequence => TryWriteSequence(sequence, depth, writer),
            DocumentMapping mapping => TryWriteMapping(mapping, depth, writer),
            _ => throw new InvalidOperationException(
                $"'{node.GetType().Name}' is not a Section 19.3 document node."),
        };
    }

    private bool TryWriteMapping(DocumentMapping mapping, int depth, OutputBufferWriter writer)
    {
        if (mapping.Members.IsEmpty)
        {
            return writer.TryWrite("{}"u8);
        }

        if (!writer.TryWrite("{"u8))
        {
            return false;
        }

        var first = true;

        foreach (var member in mapping.Members)
        {
            if (!TryWriteSeparator(first, depth + 1, writer))
            {
                return false;
            }

            first = false;

            if (!writer.TryWrite(Quote(member.Key)) || !writer.TryWrite(options.Indents() ? ": " : ":"))
            {
                return false;
            }

            if (!TryWriteNode(member.Value, depth + 1, writer))
            {
                return false;
            }
        }

        return TryWriteClose("}"u8, depth, writer);
    }

    private bool TryWriteSequence(DocumentSequence sequence, int depth, OutputBufferWriter writer)
    {
        if (sequence.Items.IsEmpty)
        {
            return writer.TryWrite("[]"u8);
        }

        if (!writer.TryWrite("["u8))
        {
            return false;
        }

        var first = true;

        foreach (var item in sequence.Items)
        {
            if (!TryWriteSeparator(first, depth + 1, writer))
            {
                return false;
            }

            first = false;

            if (!TryWriteNode(item, depth + 1, writer))
            {
                return false;
            }
        }

        return TryWriteClose("]"u8, depth, writer);
    }

    private bool TryWriteSeparator(bool first, int depth, OutputBufferWriter writer)
    {
        if (!first && !writer.TryWrite(","u8))
        {
            return false;
        }

        return !options.Indents() || writer.TryWrite(Break(depth));
    }

    private bool TryWriteClose(ReadOnlySpan<byte> bracket, int depth, OutputBufferWriter writer)
    {
        if (options.Indents() && !writer.TryWrite(Break(depth)))
        {
            return false;
        }

        return writer.TryWrite(bracket);
    }

    /// <summary>Section 16.9: <c>Indent</c> "uses two ASCII spaces per nesting level".</summary>
    private static string Break(int depth) => "\n" + new string(' ', 2 * depth);

    private bool TryWriteScalar(ScalarPayload payload, OutputBufferWriter writer) => payload.Kind switch
    {
        ScalarKind.Null => writer.TryWrite("null"u8),
        ScalarKind.Boolean => writer.TryWrite(payload.Boolean ? "true"u8 : "false"u8),

        // Section 18: "numeric source spelling is never retained", and the canonical text of both
        // numeric kinds is already a JSON number. Emitting it verbatim is what preserves an
        // arbitrary-precision value that no JSON library's numeric type could hold.
        ScalarKind.Integer or ScalarKind.Decimal => writer.TryWrite(payload.ToCanonicalText()),

        _ => writer.TryWrite(Quote(payload.Text)),
    };

    /// <summary>
    /// The JSON string literal for text, including its quotes.
    /// </summary>
    /// <remarks>
    /// Section 19.3 "serializes logical line breaks as JSON <c>\n</c> escapes", which is the
    /// ordinary escape below rather than a separate rule: a line break has no other JSON spelling.
    /// </remarks>
    private string Quote(string text)
    {
        var escapeNonAscii = options.EscapesNonAscii();

        scratch.Clear();
        scratch.Append('"');

        for (var index = 0; index < text.Length; index++)
        {
            var unit = text[index];

            switch (unit)
            {
                case '"':
                    scratch.Append("\\\"");
                    continue;

                case '\\':
                    scratch.Append("\\\\");
                    continue;

                case '\b':
                    scratch.Append("\\b");
                    continue;

                case '\f':
                    scratch.Append("\\f");
                    continue;

                case '\n':
                    scratch.Append("\\n");
                    continue;

                case '\r':
                    scratch.Append("\\r");
                    continue;

                case '\t':
                    scratch.Append("\\t");
                    continue;
            }

            if (unit < 0x20)
            {
                AppendEscape(unit);
                continue;
            }

            if (unit <= 0x7F)
            {
                scratch.Append(unit);
                continue;
            }

            // Section 16.9: a scalar above U+FFFF "is emitted as the corresponding UTF-16 surrogate
            // pair". Escaping each UTF-16 code unit produces exactly that, so the pair needs no
            // special case. An unpaired surrogate is escaped whatever the flag says, because UTF-8
            // has no encoding for one and the alternative is a silent U+FFFD.
            if (escapeNonAscii || char.IsSurrogate(unit) && !IsPaired(text, index))
            {
                AppendEscape(unit);
                continue;
            }

            scratch.Append(unit);
        }

        scratch.Append('"');

        return scratch.ToString();
    }

    private static bool IsPaired(string text, int index) =>
        char.IsHighSurrogate(text[index])
            ? index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])
            : index > 0 && char.IsHighSurrogate(text[index - 1]);

    /// <summary>Section 16.9: "uppercase hexadecimal JSON <c>\uXXXX</c>".</summary>
    private void AppendEscape(char unit) =>
        scratch.Append("\\u").Append(((int)unit).ToString("X4", CultureInfo.InvariantCulture));

    /// <summary>
    /// Section 19.3: "renders comments nowhere and emits a summarized discard warning when comments
    /// exist". Section 22 counts <c>WARN003</c> once per feature category and output file.
    /// </summary>
    private void ReportDiscardedComments()
    {
        if (discardedComments == 0)
        {
            return;
        }

        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Warn003(
                DiagnosticPhase.Planning,
                "\u00A719.3",
                $"JSON has no comment syntax, so {discardedComments} comment(s) selected into this "
                + "output were discarded.",
                cardinalityKey: FlatIdentity.Key(destination?.Canonical, "comments"),
                destination: destination?.Canonical),
            DestinationOrder: destination?.Order));
    }
}
