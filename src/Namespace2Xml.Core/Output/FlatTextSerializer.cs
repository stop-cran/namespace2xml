using Namespace2Xml.Diagnostics;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;

namespace Namespace2Xml.Output;

/// <summary>
/// Serializes Section 19.1 namespace output and Section 19.2 quoted-namespace output.
/// </summary>
/// <remarks>
/// <para>
/// The two formats share everything above the line: the same entries in the same Section 19.1
/// order, the same Section 20 comment normalization, one physical line per entry. They differ only
/// in how a value is spelled, which is why they are one serializer and not two that drift.
/// </para>
/// <para>
/// Values are encoded through <see cref="NamespaceEncoder"/> rather than by a second copy of the
/// Section 19.1 escape table. The escapes are the inverse of the value lexer, and a second copy is
/// a second thing to keep inverse.
/// </para>
/// </remarks>
public sealed class FlatTextSerializer
{
    private readonly FlatFormat format;
    private readonly string delimiter;
    private readonly DiagnosticBuffer diagnostics;
    private readonly DestinationRef? destination;

    /// <summary>Creates a serializer.</summary>
    /// <param name="format">Namespace or quoted namespace.</param>
    /// <param name="delimiter">The Section 16.4 delimiter, which reference names inside a value use.</param>
    /// <param name="diagnostics">The buffer serialization faults accumulate in.</param>
    /// <param name="destination">The Section 6.4.3 <c>destination</c> this instance writes to.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The format is INI, which Section 19.6 gives a section-structured layout of its own.
    /// </exception>
    public FlatTextSerializer(
        FlatFormat format,
        string delimiter,
        DiagnosticBuffer diagnostics,
        DestinationRef? destination = null)
    {
        ArgumentNullException.ThrowIfNull(delimiter);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (format is not (FlatFormat.Namespace or FlatFormat.QuotedNamespace))
        {
            throw new ArgumentOutOfRangeException(
                nameof(format),
                format,
                "Section 19.6 INI output is section-structured and has its own serializer.");
        }

        this.format = format;
        this.delimiter = delimiter;
        this.diagnostics = diagnostics;
        this.destination = destination;
    }

    /// <summary>Writes every entry, and the comments that own none.</summary>
    /// <param name="document">The keyed entries in Section 19.1 emission order, and the document comments.</param>
    /// <param name="writer">The buffer to write into.</param>
    /// <returns>
    /// Whether the whole output was written. A false result means either a reported
    /// <c>SERIALIZE001</c> or a budget crossing the caller reads from the writer's fault.
    /// </returns>
    /// <remarks>
    /// Section 20 places the ownerless comments: document-leading ones "precede that source's first
    /// surviving contribution" and document-trailing ones "follow its final surviving contribution".
    /// For one output instance that is the top and the bottom of the file, so they bracket the
    /// entries rather than binding to the first or last of them — which is what lets a document
    /// comment outlive an ignore mask over the entry it happened to sit against.
    /// </remarks>
    public bool TrySerialize(FlatKeyedDocument document, OutputBufferWriter writer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(writer);

        foreach (var comment in document.Leading)
        {
            if (!TryWriteComment(comment.Text, writer))
            {
                return false;
            }
        }

        foreach (var keyed in document.Entries)
        {
            foreach (var comment in keyed.Entry.Comments)
            {
                if (!TryWriteComment(comment.Text, writer))
                {
                    return false;
                }
            }

            if (!TrySpellValue(keyed.Entry.Payload, keyed.Key, out var value))
            {
                return false;
            }

            if (!writer.TryWriteLine($"{keyed.Key}={value}"))
            {
                return false;
            }
        }

        foreach (var comment in document.Trailing)
        {
            if (!TryWriteComment(comment.Text, writer))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Section 20: "comment text is normalized to LF and every physical line is prefixed
    /// independently with <c>#</c> and a space".
    /// </summary>
    /// <remarks>
    /// Prefixing each line independently is what makes the rule safe: a multiline comment "can
    /// therefore never introduce an executable shell assignment or an uncommented namespace entry".
    /// Prefixing only the first line would leave the rest of a multiline comment as live output.
    /// </remarks>
    private bool TryWriteComment(string text, OutputBufferWriter writer)
    {
        if (text.Contains('\0', StringComparison.Ordinal))
        {
            Report("Section 20 rejects NUL in comment text, and no escape for it exists here.");
            return false;
        }

        var normalized = text.ReplaceLineEndings("\n");

        foreach (var line in normalized.Split('\n'))
        {
            if (!writer.TryWriteLine(line.Length == 0 ? "#" : $"# {line}"))
            {
                return false;
            }
        }

        return true;
    }

    private bool TrySpellValue(ScalarPayload payload, string key, out string? value)
    {
        // Section 19.1 spells null as the text "null", and Section 19.2 adopts that spelling so a
        // shell consumer is not left unable to tell null from the empty string.
        var text = payload.IsNull ? "null" : payload.ToCanonicalText();

        if (format == FlatFormat.QuotedNamespace)
        {
            return TryQuoteForShell(text, key, out value);
        }

        if (NamespaceEncoder.TryEncodeValue(
                new InterpretedValue([new LiteralValueToken(text)]),
                delimiter,
                out var encoded,
                out var fault))
        {
            value = encoded!;
            return true;
        }

        Report(fault.Message);
        value = null;
        return false;
    }

    /// <summary>
    /// Section 19.2's single-quote escaping, which "preserves spaces, <c>$</c>, backticks, double
    /// quotes, backslashes, exclamation marks, and line breaks without expansion".
    /// </summary>
    /// <remarks>
    /// A single quote cannot appear inside a single-quoted shell word at all, so the word is closed,
    /// an escaped quote is emitted outside it, and a new word is opened: <c>'can'\''t'</c>. The
    /// shell concatenates adjacent words, so the assignment still receives one value.
    /// </remarks>
    private bool TryQuoteForShell(string text, string key, out string? value)
    {
        if (text.Contains('\0', StringComparison.Ordinal))
        {
            // Appendix B: "Invalid quoted-namespace identifier or NUL value" is SHELL001. The value
            // is unrepresentable as a shell word, which is a property of the shell target, not of
            // the serializer failing to represent something it otherwise could.
            ReportShellFault(
                key,
                "Section 19.2 makes NUL not representable in quoted-namespace output: a shell word "
                + "cannot carry it and single quoting admits no escape for it.");
            value = null;
            return false;
        }

        value = $"'{text.Replace("'", "'\\''", StringComparison.Ordinal)}'";
        return true;
    }

    /// <summary>Section 22: <c>SHELL001</c> occurs "once per projected key and output instance".</summary>
    private void ReportShellFault(string key, string message) =>
        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Shell001(
                DiagnosticPhase.Planning,
                "\u00A719.2",
                message,
                cardinalityKey: FlatIdentity.Key(destination?.Canonical, key),
                path: key,
                destination: destination?.Canonical),
            DestinationOrder: destination?.Order));

    private void Report(string message) =>
        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Serialize001(
                DiagnosticPhase.Planning,
                format == FlatFormat.QuotedNamespace ? "\u00A719.2" : "\u00A719.1",
                message,
                cardinalityKey: FlatIdentity.Key(destination?.Canonical, null),
                destination: destination?.Canonical),
            DestinationOrder: destination?.Order));
}
