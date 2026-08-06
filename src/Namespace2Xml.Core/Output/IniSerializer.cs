using System.Text;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;

namespace Namespace2Xml.Output;

/// <summary>Serializes Section 19.6 <c>PortableIni1</c> output.</summary>
/// <remarks>
/// <para>
/// The layout is a projection, not a reordering: Section 19.6 hoists "all global keys ... in one
/// preamble before the first section", and says outright that doing so "does not change value
/// precedence". Within each block the entries keep the Section 19.1 order they arrived in.
/// </para>
/// <para>
/// Sections are emitted at the position of their first key, which is what Section 19.6 defines
/// mapping order to mean for a section. A section is a projection of a path prefix and not a node,
/// so nothing else about it is an order; Section 19.6 states the consequence that a nested section
/// precedes its parent when the parent's own keys come later.
/// </para>
/// <para>
/// The grouping is built in one pass. Selecting each section's entries by rescanning the whole
/// sequence would cost the product of the two counts, and both are unbounded.
/// </para>
/// </remarks>
public sealed class IniSerializer
{
    private readonly IniOutputOptions options;
    private readonly DiagnosticBuffer diagnostics;
    private readonly DestinationRef? destination;

    /// <summary>Creates a serializer.</summary>
    /// <param name="options">The Section 16.9 options.</param>
    /// <param name="diagnostics">The buffer serialization faults accumulate in.</param>
    /// <param name="destination">The Section 6.4.3 <c>destination</c> this instance writes to.</param>
    public IniSerializer(
        IniOutputOptions options,
        DiagnosticBuffer diagnostics,
        DestinationRef? destination = null)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        this.options = options;
        this.diagnostics = diagnostics;
        this.destination = destination;
    }

    /// <summary>Writes every entry.</summary>
    /// <param name="entries">The keyed entries, in Section 19.1 emission order.</param>
    /// <param name="writer">The buffer to write into.</param>
    /// <returns>
    /// Whether the whole output was written. A false result means either a reported <c>INI001</c>
    /// or a budget crossing the caller reads from the writer's fault.
    /// </returns>
    /// <remarks>
    /// An unrepresentable value does not end the pass. Section 15.4 requires a phase to complete
    /// "every independent check that does not depend on a failed result", and one value's failure
    /// tells you nothing about the next one's, so every entry is checked and Section 22's "once
    /// per path and output instance" can actually be reached. That cardinality is what
    /// distinguishes <c>INI001</c> from <c>SERIALIZE001</c>'s "once per output instance"; a
    /// serializer that stopped at the first offending value would make the distinction empty and
    /// would report one of an author's mistakes per run.
    ///
    /// A budget fault is not like that. It is a property of the buffer rather than of an entry,
    /// nothing written after it can succeed, and continuing would report a limit repeatedly. So
    /// the writer's fault still ends the pass immediately.
    /// </remarks>
    public bool TrySerialize(IEnumerable<FlatKeyedEntry> entries, OutputBufferWriter writer)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(writer);

        var ordered = entries.ToList();
        var marker = options.CommentMarker();

        if (marker is null && ordered.Exists(entry => !entry.Entry.Comments.IsEmpty))
        {
            ReportDiscardedComments();
        }

        var globals = new List<FlatKeyedEntry>();
        var sections = new List<string>();
        var bySection = new Dictionary<string, List<FlatKeyedEntry>>(StringComparer.Ordinal);

        foreach (var keyed in ordered)
        {
            if (keyed.Section.Length == 0)
            {
                globals.Add(keyed);
                continue;
            }

            if (!bySection.TryGetValue(keyed.Section, out var members))
            {
                members = [];
                bySection.Add(keyed.Section, members);
                sections.Add(keyed.Section);
            }

            members.Add(keyed);
        }

        var complete = true;

        foreach (var keyed in globals)
        {
            if (!TryWriteEntry(keyed, marker, writer))
            {
                complete = false;

                if (writer.Fault is not null)
                {
                    return false;
                }
            }
        }

        foreach (var section in sections)
        {
            if (!writer.TryWriteLine($"[{section}]"))
            {
                return false;
            }

            foreach (var keyed in bySection[section])
            {
                if (!TryWriteEntry(keyed, marker, writer))
                {
                    complete = false;

                    if (writer.Fault is not null)
                    {
                        return false;
                    }
                }
            }
        }

        return complete;
    }

    private bool TryWriteEntry(FlatKeyedEntry keyed, char? marker, OutputBufferWriter writer)
    {
        if (marker is { } commentMarker)
        {
            foreach (var comment in keyed.Entry.Comments)
            {
                // Section 20 emits INI comments "only as full-line leading comments", and the
                // section header is already written, so a section's first key gets its comments
                // "after the section header and before that key" without a special case here.
                foreach (var line in comment.Text.ReplaceLineEndings("\n").Split('\n'))
                {
                    if (!writer.TryWriteLine($"{commentMarker} {line}"))
                    {
                        return false;
                    }
                }
            }
        }

        if (!TrySpellValue(keyed, out var value))
        {
            return false;
        }

        // Section 19.6: "no spaces around '=' for compatibility".
        return writer.TryWriteLine($"{keyed.Key}={value}");
    }

    private bool TrySpellValue(FlatKeyedEntry keyed, out string? value)
    {
        value = null;

        var text = keyed.Entry.Payload.IsNull ? "null" : keyed.Entry.Payload.ToCanonicalText();

        if (text.Contains('\0', StringComparison.Ordinal))
        {
            Report(keyed, "NUL is not representable in a 'PortableIni1' value under any option.");
            return false;
        }

        if (!options.EscapesMultiline()
            && text.AsSpan().ContainsAny('\n', '\r'))
        {
            Report(
                keyed,
                "Section 19.6 rejects CR and LF in a value unless 'EscapeMultiline' is selected.");
            return false;
        }

        if (!options.HasFlag(IniOutputOptions.QuoteValues)
            && !TryValidateUnquoted(keyed, text))
        {
            return false;
        }

        value = Escape(text);
        return true;
    }

    /// <summary>
    /// The Section 19.6 restrictions that only quoting lifts, because without quotes the reader
    /// cannot tell the value from a comment, and cannot tell padding from content.
    /// </summary>
    private bool TryValidateUnquoted(FlatKeyedEntry keyed, string text)
    {
        if (text.StartsWith(';') || text.StartsWith('#'))
        {
            Report(
                keyed,
                "a value beginning with ';' or '#' reads back as a comment, so Section 19.6 "
                + "requires 'QuoteValues' for it.");
            return false;
        }

        if (text.Length > 0
            && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1])))
        {
            Report(
                keyed,
                "leading or trailing whitespace does not survive an unquoted value, so "
                + "Section 19.6 requires 'QuoteValues' for it.");
            return false;
        }

        return true;
    }

    private string Escape(string text)
    {
        var quote = options.HasFlag(IniOutputOptions.QuoteValues);

        if (!quote && !options.EscapesMultiline())
        {
            return text;
        }

        var builder = new StringBuilder(text.Length + 2);

        if (quote)
        {
            builder.Append('"');
        }

        foreach (var c in text)
        {
            // Section 19.6 doubles a literal backslash "whether or not QuoteValues is also
            // selected", and QuoteValues asks for the same doubling, so it happens exactly once.
            _ = c switch
            {
                '\\' => builder.Append("\\\\"),
                '"' when quote => builder.Append("\\\""),
                '\n' when options.EscapesMultiline() => builder.Append("\\n"),
                '\r' when options.EscapesMultiline() => builder.Append("\\r"),
                '\t' when options.EscapesMultiline() => builder.Append("\\t"),
                _ => builder.Append(c),
            };
        }

        if (quote)
        {
            builder.Append('"');
        }

        return builder.ToString();
    }

    private void Report(FlatKeyedEntry keyed, string message)
    {
        var path = FlatIdentity.PathText(keyed.Entry.LogicalPath);

        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Ini001(
                DiagnosticPhase.Planning,
                "\u00A719.6",
                $"the value at '{path ?? keyed.Key}' cannot be written: {message}",
                cardinalityKey: FlatIdentity.Key(destination?.Canonical, path),
                path: path,
                destination: destination?.Canonical),
            DestinationOrder: destination?.Order));
    }

    private void ReportDiscardedComments() =>
        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Warn003(
                DiagnosticPhase.Planning,
                "\u00A720",
                "comments were discarded because neither 'SemicolonComments' nor 'HashComments' "
                + "is selected in 'inioutputoptions'.",
                cardinalityKey: FlatIdentity.Key(destination?.Canonical, "comments"),
                destination: destination?.Canonical),
            DestinationOrder: destination?.Order));
}
