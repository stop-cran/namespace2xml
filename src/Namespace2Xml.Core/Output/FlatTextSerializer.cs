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
    private readonly NamespaceOutputOptions options;
    private readonly DiagnosticBuffer diagnostics;
    private readonly DestinationRef? destination;

    /// <summary>Creates a serializer.</summary>
    /// <param name="format">Namespace or quoted namespace.</param>
    /// <param name="delimiter">The Section 16.4 delimiter, which reference names inside a value use.</param>
    /// <param name="options">The Section 16.9 namespace options, which govern the namespace format alone.</param>
    /// <param name="diagnostics">The buffer serialization faults accumulate in.</param>
    /// <param name="destination">The Section 6.4.3 <c>destination</c> this instance writes to.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The format is INI, which Section 19.6 gives a section-structured layout of its own.
    /// </exception>
    public FlatTextSerializer(
        FlatFormat format,
        string delimiter,
        NamespaceOutputOptions options,
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
        this.options = options;
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

            if (!TrySpellValue(keyed.Entry, keyed.Key, out var value))
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

    private bool TrySpellValue(FlatEntry entry, string key, out string? value)
    {
        // Section 19.1: an empty container is spelled as the bare sentinel. It reaches no escape
        // table, because the two characters are the shape rather than text that could need one.
        if (entry.Container is not ContainerSentinel.None)
        {
            value = entry.Container is ContainerSentinel.EmptyMapping
                ? ContainerSentinels.Mapping
                : ContainerSentinels.Sequence;
            return true;
        }

        var payload = entry.Payload!;

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
            // Section 19.1 checks the emitted text rather than the payload, because the escape table
            // decides what actually reaches the line: a TAB leaves as '\t' and ends it in a letter.
            if (NamespaceOutput.EndsInForbiddenWhitespace(encoded!)
                && !ReportTrailingWhitespace(key))
            {
                value = null;
                return false;
            }

            // Section 19.1: "a scalar whose text is exactly '{}' or '[]' emits '\{}' or '\[]'", so
            // the two readings never collide. The emitted text is tested rather than the payload
            // for the same reason the rule above tests it: that is what a reader will see.
            value = ContainerSentinels.Spell(encoded!);
            return true;
        }

        Report(fault.Message);
        value = null;
        return false;
    }

    /// <summary>
    /// Reports the Section 19.1 trailing-whitespace condition, and says whether the entry may be
    /// written anyway.
    /// </summary>
    /// <param name="key">The projected key, which Section 22 carries as the diagnostic's path.</param>
    /// <returns>
    /// Whether <c>AllowTrailingWhitespace</c> admits the entry, so the caller writes it after a
    /// <c>WARN013</c> rather than refusing it as <c>NAMESPACE001</c>.
    /// </returns>
    /// <remarks>
    /// Both codes occur "once per path and output instance", so both take the same cardinality key.
    /// Section 24 relaxes its byte rule only here, and only because Section 8.1 preserves a value's
    /// trailing spaces on read while Section 8.3 gives values no escape to write them with.
    /// </remarks>
    private bool ReportTrailingWhitespace(string key)
    {
        var allowed = options.AllowsTrailingWhitespace();

        diagnostics.Add(new BufferedDiagnostic(
            allowed
                ? DiagnosticCodes.Warn013(
                    DiagnosticPhase.Planning,
                    "\u00A719.1",
                    $"the value at '{key}' is written with trailing whitespace because "
                    + "'AllowTrailingWhitespace' is selected, so this line ends in a space that an "
                    + "editor or a formatter may strip without reporting it.",
                    cardinalityKey: FlatIdentity.Key(destination?.Canonical, key),
                    path: key,
                    destination: destination?.Canonical)
                : DiagnosticCodes.Namespace001(
                    DiagnosticPhase.Planning,
                    "\u00A719.1",
                    $"the value at '{key}' cannot be written: it ends in whitespace that would end "
                    + "the line in a space, Section 8.3 gives namespace values no escape for it, and "
                    + "Section 24 forbids it. Use 'quotednamespace', or select "
                    + "'namespaceoutputoptions=AllowTrailingWhitespace' to write it anyway.",
                    cardinalityKey: FlatIdentity.Key(destination?.Canonical, key),
                    path: key,
                    destination: destination?.Canonical),
            DestinationOrder: destination?.Order));

        return allowed;
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
