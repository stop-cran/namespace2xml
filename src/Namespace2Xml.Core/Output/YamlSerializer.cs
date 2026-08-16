using Namespace2Xml.Diagnostics;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;

namespace Namespace2Xml.Output;

/// <summary>Serializes Section 19.4 YAML output.</summary>
/// <remarks>
/// <para>
/// The emitter is hand-written because Section 19.4 and Section 20 require comments in the output,
/// and every general-purpose YAML library discards comments before the node graph exists. It also
/// fixes bytes a library chooses for itself: fixed two-space indentation, no <c>---</c>, literal
/// block scalars for multiline values, and arbitrary-precision numbers that must reach the file in
/// their Section 18 canonical spelling.
/// </para>
/// <para>
/// Lines are produced strictly in order, so the pending sequence indicator is a field rather than a
/// parameter threaded through every writer. A sequence item is written at its own indentation and
/// the first line it emits replaces the two spaces before it with <c>"- "</c>, which is why an item
/// holding a mapping renders in the compact form <c>- key: value</c> without the mapping writer
/// knowing it is inside a sequence.
/// </para>
/// </remarks>
public sealed class YamlSerializer
{
    private readonly YamlOutputOptions options;
    private readonly DiagnosticBuffer diagnostics;
    private readonly DestinationRef? destination;

    private string? pending;

    /// <summary>Creates a serializer.</summary>
    /// <param name="options">The Section 16.9 YAML options.</param>
    /// <param name="diagnostics">The buffer serialization faults accumulate in.</param>
    /// <param name="destination">The Section 6.4.3 <c>destination</c> this instance writes to.</param>
    public YamlSerializer(
        YamlOutputOptions options,
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
    /// Whether the whole output was written. A false result means either a reported
    /// <c>SERIALIZE001</c> or a budget crossing the caller reads from the writer's fault.
    /// </returns>
    public bool TrySerialize(DocumentNode document, OutputBufferWriter writer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(writer);

        pending = null;

        // Section 19.4 "does not emit '---'", so the document begins with its own first line.
        return TryWriteValue(document, string.Empty, 0, writer);
    }

    /// <summary>Writes one node, prefixed on its first line by a mapping key or nothing.</summary>
    /// <param name="node">The node.</param>
    /// <param name="label">
    /// The <c>key:</c> text this node's value follows, or empty for a document root or sequence
    /// item, whose value starts the line itself.
    /// </param>
    /// <param name="indent">The column this node's own lines begin at.</param>
    /// <param name="writer">The buffer to write into.</param>
    private bool TryWriteValue(DocumentNode node, string label, int indent, OutputBufferWriter writer)
    {
        var inline = Inline(node, label);

        if (!TryComments(node, CommentPlacement.Leading, inline, indent, writer))
        {
            return false;
        }

        var children = label.Length > 0 ? indent + 2 : indent;
        var text = InlineText(inline);

        var written = node switch
        {
            DocumentScalar scalar => TryWriteScalar(scalar.Payload, label, text, indent, writer),
            DocumentSequence { Items.IsEmpty: true } => TryLine(indent, Join(label, "[]", text), writer),
            DocumentMapping { Members.IsEmpty: true } => TryLine(indent, Join(label, "{}", text), writer),
            _ => TryWriteContainer(node, label, text, indent, children, writer),
        };

        if (!written)
        {
            return false;
        }

        return TryComments(node, CommentPlacement.Trailing, inline, indent, writer);
    }

    private bool TryWriteContainer(
        DocumentNode node,
        string label,
        string? inline,
        int indent,
        int children,
        OutputBufferWriter writer)
    {
        if (label.Length > 0 && !TryLine(indent, Join(label, null, inline), writer))
        {
            return false;
        }

        if (node is DocumentSequence sequence)
        {
            // A sequence that is itself a sequence item has no line of its own: YAML's compact
            // notation puts both indicators on the first item's line ("- - x"). Overwriting the
            // carried indicator instead of accumulating it would emit that item at an indentation
            // no enclosing node introduced, which is a different document.
            var carried = pending ?? string.Empty;

            foreach (var item in sequence.Items)
            {
                pending = carried + "- ";
                carried = string.Empty;

                if (!TryWriteValue(item, string.Empty, children + 2, writer))
                {
                    return false;
                }
            }

            return true;
        }

        foreach (var member in ((DocumentMapping)node).Members)
        {
            if (!TryWriteValue(member.Value, $"{Key(member.Key)}:", children, writer))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryWriteScalar(
        ScalarPayload payload,
        string label,
        string? inline,
        int indent,
        OutputBufferWriter writer)
    {
        if (payload.Kind is ScalarKind.String or ScalarKind.UntypedString
            && YamlScalarText.CanBlock(payload.Text))
        {
            return TryWriteBlock(payload.Text, label, inline, indent, writer);
        }

        return TryLine(indent, Join(label, Spell(payload), inline), writer);
    }

    /// <summary>Section 19.4 "uses literal block scalars for multiline values".</summary>
    /// <remarks>
    /// Section 16.9 indents "literal block-scalar content two spaces beyond its owning key or
    /// sequence indicator". The chomping indicator carries what the indentation cannot: a value
    /// that does not end in a line break is stripped, one that ends in exactly one is clipped, and
    /// one that ends in more is kept, which is the only way a block scalar can represent all three.
    /// </remarks>
    private bool TryWriteBlock(
        string text,
        string label,
        string? inline,
        int indent,
        OutputBufferWriter writer)
    {
        var terminated = text.EndsWith('\n');
        var chomping = !terminated ? "-" : text.EndsWith("\n\n", StringComparison.Ordinal) ? "+" : string.Empty;

        if (!TryLine(indent, Join(label, $"|{chomping}", inline), writer))
        {
            return false;
        }

        var lines = text.Split('\n');
        var last = terminated ? lines.Length - 1 : lines.Length;

        for (var index = 0; index < last; index++)
        {
            // An empty content line is written empty. Indenting it would add trailing white space
            // that Section 24 tolerates but that a reader would read back as content.
            var line = lines[index].Length == 0
                ? string.Empty
                : Spaces(indent + 2) + lines[index];

            if (!writer.TryWriteLine(line))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Section 19.4 preserves "supported scalar types", so only strings are quoted.</summary>
    private static string Spell(ScalarPayload payload) => payload.Kind switch
    {
        ScalarKind.Null => "null",
        ScalarKind.Boolean => payload.Boolean ? "true" : "false",
        ScalarKind.Integer or ScalarKind.Decimal => payload.ToCanonicalText(),
        _ => YamlScalarText.Spell(payload.Text),
    };

    /// <summary>
    /// Section 10.1 treats "every plain or quoted scalar mapping key ... as a string without scalar
    /// tag resolution", but a key that would resolve elsewhere is quoted anyway so that a reader
    /// outside <c>RestrictedYaml1</c> reads back the same key.
    /// </summary>
    /// <remarks>
    /// Section 19.4 spells a key "by these same rules, and by exactly these rules", so no key-only
    /// case is left: <c>&lt;&lt;</c> is portably typed and is quoted in either position. A key
    /// carries identity rather than data, which is what makes the quoting matter more here than in a
    /// value -- <c>yes</c> and <c>on</c> both resolve to <c>true</c> for a YAML 1.1 reader, so a
    /// mapping holding both would lose a member with no diagnostic, defeating Section 19.3's
    /// <c>FLAT001</c> from outside the tool.
    /// </remarks>
    private static string Key(string text) => YamlScalarText.Spell(text);

    private static string Join(string label, string? value, string? inline)
    {
        var head = (label.Length, value) switch
        {
            (0, null) => string.Empty,
            (0, _) => value!,
            (_, null) => label,
            _ => $"{label} {value}",
        };

        return inline is null ? head : head.Length == 0 ? $"#{inline}" : $"{head} #{inline}";
    }

    /// <summary>
    /// The comment this node carries on the same line as its value, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An inline spelling exists only when the comment is a single line and the node has a line of
    /// its own to sit at the end of. A multiline comment has none, and neither does a container
    /// written without a <c>key:</c> header, whose first line belongs to its first child. Section 20
    /// normalizes comment position rather than preserving it exactly, so those are emitted on their
    /// own line before the node instead of being dropped.
    /// </para>
    /// <para>
    /// Section 4.5: "when several YAML inline comments accumulate at one path, the latest remains
    /// inline and earlier inline comments become leading comments in source order". The last
    /// eligible comment is therefore chosen, not the first; <see cref="Emits"/> routes the rest
    /// into the leading pass, which already walks the list in source order.
    /// </para>
    /// </remarks>
    private BoundComment? Inline(DocumentNode node, string label)
    {
        if (!options.PreservesComments())
        {
            return null;
        }

        var standalone = label.Length > 0
            || node is DocumentScalar
            || node is DocumentSequence { Items.IsEmpty: true }
            || node is DocumentMapping { Members.IsEmpty: true };

        if (!standalone)
        {
            return null;
        }

        BoundComment? latest = null;

        foreach (var comment in node.Comments)
        {
            if (comment.Placement == CommentPlacement.Inline
                && !comment.Text.AsSpan().ContainsAny('\n', '\r')
                && !comment.Text.Contains('\0', StringComparison.Ordinal))
            {
                latest = comment;
            }
        }

        return latest;
    }

    private static string? InlineText(BoundComment? comment) =>
        comment is null ? null
        : comment.Text.Length == 0 || comment.Text[0] == ' ' ? comment.Text
        : $" {comment.Text}";

    private bool TryComments(
        DocumentNode node,
        CommentPlacement placement,
        BoundComment? inline,
        int indent,
        OutputBufferWriter writer)
    {
        if (!options.PreservesComments() || node.Comments.IsEmpty)
        {
            return true;
        }

        var column = indent - (pending?.Length ?? 0);

        foreach (var comment in node.Comments)
        {
            if (!Emits(comment, placement, inline))
            {
                continue;
            }

            if (comment.Text.Contains('\0', StringComparison.Ordinal))
            {
                Report("Section 20 rejects NUL in comment text, and YAML admits no escape for it.");
                return false;
            }

            if (HasUnprintable(comment.Text))
            {
                Report(
                    "Section 20 comment text contains a character YAML excludes from c-printable, "
                    + "and a comment admits no escape for it.");
                return false;
            }

            foreach (var line in comment.Text.ReplaceLineEndings("\n").Split('\n'))
            {
                if (!writer.TryWriteLine(Spaces(column) + (line.Length == 0 ? "#" : $"# {line}")))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the text contains a character outside YAML's <c>c-printable</c> set, ignoring the
    /// line breaks a multiline comment is split on.
    /// </summary>
    /// <remarks>
    /// A comment body is raw text: unlike a scalar it has no quoted form, so a character YAML
    /// excludes cannot be written at all. Emitting it anyway produces a file no parser will read,
    /// which is worse than declining to write one. A lone surrogate is excluded for the adjacent
    /// reason that UTF-8 cannot encode it, so writing it substitutes U+FFFD and silently changes
    /// the retained comment.
    /// </remarks>
    /// <param name="text">The comment text.</param>
    private static bool HasUnprintable(string text)
    {
        if (YamlScalarText.HasLoneSurrogate(text))
        {
            return true;
        }

        foreach (var unit in text)
        {
            if (unit is '\n' or '\r' or '\t')
            {
                continue;
            }

            if (char.IsControl(unit) || unit == '\uFEFF' || unit is '\u2028' or '\u2029'
                || unit is '\uFFFE' or '\uFFFF')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a comment is emitted in this pass. An inline comment with no inline spelling joins
    /// the leading pass, which keeps its text at the cost of its exact column.
    /// </summary>
    private static bool Emits(BoundComment comment, CommentPlacement placement, BoundComment? inline) =>
        comment.Placement == placement
        || placement == CommentPlacement.Leading
            && comment.Placement == CommentPlacement.Inline
            && !ReferenceEquals(comment, inline);

    /// <summary>
    /// Writes one line at its column, consuming any pending sequence indicator.
    /// </summary>
    /// <remarks>
    /// An indicator is exactly as wide as the indentation it replaces, so a node never needs to
    /// know whether it is a sequence item: the caller reserves the width in <paramref name="indent"/>
    /// and the indicator fills it.
    /// </remarks>
    /// <param name="indent">The column the line's content begins at.</param>
    /// <param name="text">The line's content.</param>
    /// <param name="writer">The buffer to write into.</param>
    private bool TryLine(int indent, string text, OutputBufferWriter writer)
    {
        string prefix;

        if (pending is null)
        {
            prefix = Spaces(indent);
        }
        else
        {
            prefix = Spaces(indent - pending.Length) + pending;
            pending = null;
        }

        return writer.TryWriteLine(prefix + text);
    }

    private static string Spaces(int count) => count <= 0 ? string.Empty : new string(' ', count);

    private void Report(string message) =>
        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Serialize001(
                DiagnosticPhase.Planning,
                "\u00A719.4",
                message,
                cardinalityKey: FlatIdentity.Key(destination?.Canonical, null),
                destination: destination?.Canonical),
            DestinationOrder: destination?.Order));
}
