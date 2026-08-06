using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;

namespace Namespace2Xml.Inputs;

/// <summary>
/// A Section 8.6 permanent exclusion mask, as read from one source.
/// </summary>
/// <param name="Pattern">The mask pattern.</param>
/// <param name="Order">The mask's Section 4.7 key.</param>
/// <param name="Line">The one-based line it was written on.</param>
public sealed record ProfileMask(QualifiedName Pattern, StableOrderingKey Order, int Line);

/// <summary>
/// An entry the concrete overlay does not receive: a Section 15.1 step 7 wildcard template, or an
/// entry whose value is not resolvable until step 15.
/// </summary>
/// <param name="Name">The entry's name.</param>
/// <param name="Value">The entry's interpreted value.</param>
/// <param name="Order">The entry's Section 4.7 key.</param>
/// <param name="Line">The one-based line it was written on.</param>
/// <param name="Comments">
/// The Section 8.5 run of comments bound to this entry. Section 4.5 clones a template's comments
/// onto every contribution it generates, so they must travel with the entry rather than fall
/// through to the next one.
/// </param>
public sealed record ProfileEntry(
    QualifiedName Name,
    InterpretedValue Value,
    StableOrderingKey Order,
    int Line,
    ImmutableArray<BoundComment> Comments);

/// <summary>
/// One namespace-profile source, read into the parts Section 15.1 separates at steps 5 and 7.
/// </summary>
/// <param name="Overlay">The concrete contributions of this source.</param>
/// <param name="Masks">The Section 8.6 masks this source declares.</param>
/// <param name="Templates">The wildcard template entries extracted at step 7.</param>
/// <param name="TrailingComments">
/// Section 8.5 document-trailing comments: those with no following entry.
/// </param>
/// <param name="UnresolvedValues">
/// Entries whose values carry a reference or a value wildcard. They are concrete contributions, but
/// their payloads are not known until Section 15.1 step 15.
/// </param>
public sealed record ProfileContribution(
    OverlayNode Overlay,
    ImmutableArray<ProfileMask> Masks,
    ImmutableArray<ProfileEntry> Templates,
    ImmutableArray<BoundComment> TrailingComments,
    ImmutableArray<ProfileEntry> UnresolvedValues);

/// <summary>
/// Reads classified Section 8.1 records into a Section 4.2 overlay.
/// </summary>
/// <remarks>
/// <para>
/// This is Section 15.1 step 5 for the namespace-profile format, together with the step 7 split
/// between concrete contributions, wildcard templates and permanent masks. It does not merge
/// sources, resolve references, evaluate templates, or infer scalar kinds — every payload it
/// produces is <see cref="ScalarKind.UntypedString"/>, which Section 4.3 makes the initial kind of
/// every namespace scalar.
/// </para>
/// <para>
/// Diagnostics are buffered with their Section 4.7 key rather than written, so that Section 24 can
/// order them against diagnostics raised by other sources parsed concurrently.
/// </para>
/// </remarks>
public static class NamespaceProfileReader
{
    /// <summary>Reads one source's records.</summary>
    /// <param name="records">The Section 8.1 classified records, in source order.</param>
    /// <param name="sourceOrdinal">The Section 4.7 CLI source ordinal.</param>
    /// <param name="source">The source name diagnostics report.</param>
    /// <param name="diagnostics">The buffer this source's diagnostics accumulate in.</param>
    public static ProfileContribution Read(
        ImmutableArray<NamespaceRecord> records,
        long sourceOrdinal,
        string source,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var overlay = OverlayNode.Intermediate(StableOrderingKey.FromSource(sourceOrdinal, 0));
        var masks = ImmutableArray.CreateBuilder<ProfileMask>();
        var templates = ImmutableArray.CreateBuilder<ProfileEntry>();
        var unresolved = ImmutableArray.CreateBuilder<ProfileEntry>();
        var pending = ImmutableArray.CreateBuilder<BoundComment>();

        foreach (var record in records)
        {
            var key = StableOrderingKey.FromSource(sourceOrdinal, record.Line);

            switch (record.Kind)
            {
                case NamespaceRecordKind.Ignored:
                    break;

                case NamespaceRecordKind.Malformed:
                    diagnostics.Add(new BufferedDiagnostic(
                        DiagnosticCodes.Parse001(
                            DiagnosticPhase.Input,
                            "\u00A78.1",
                            "this record is neither a comment nor a mask and has no separating '=', "
                            + "so Section 8.1 rule 5 makes it a parse error.",
                            cardinalityKey: $"{source}:{record.Line}:{record.Column}",
                            source: source,
                            line: record.Line,
                            column: record.Column),
                        key));
                    break;

                case NamespaceRecordKind.Comment:
                    pending.Add(new BoundComment(record.Comment!, CommentPlacement.Leading, key));
                    break;

                case NamespaceRecordKind.Mask:
                    ReadMask(record, key, source, diagnostics, masks);
                    break;

                case NamespaceRecordKind.Entry:
                    overlay = ReadEntry(
                        record, key, source, diagnostics, overlay, templates, unresolved, pending);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Section 8.1 classifies records into the {nameof(NamespaceRecordKind)} "
                        + $"members; '{record.Kind}' is not one of them.");
            }
        }

        return new ProfileContribution(
            overlay,
            masks.ToImmutable(),
            templates.ToImmutable(),
            pending.ToImmutable(),
            unresolved.ToImmutable());
    }

    private static void ReadMask(
        NamespaceRecord record,
        StableOrderingKey key,
        string source,
        DiagnosticBuffer diagnostics,
        ImmutableArray<ProfileMask>.Builder masks)
    {
        // Section 8.6: "The legacy form with an ignored value remains accepted." Section 8.1 has
        // already classified the record on its leading '!', so the pattern is everything up to the
        // first separating '=' and the rest is discarded rather than lexed as part of the name.
        var text = record.Pattern!;
        var separator = NamespaceRecordClassifier.FindSeparatingEquals(text);
        var pattern = separator < 0 ? text : text[..separator];

        var lexed = QualifiedNameLexer.Lex(pattern);

        if (lexed.Name is null)
        {
            EmitNameFault(
                lexed.Fault!.Value,
                record.Line,
                record.Column + lexed.Fault.Value.Offset,
                source,
                diagnostics,
                key);
            return;
        }

        masks.Add(new ProfileMask(lexed.Name, key, record.Line));
    }

    private static OverlayNode ReadEntry(
        NamespaceRecord record,
        StableOrderingKey key,
        string source,
        DiagnosticBuffer diagnostics,
        OverlayNode overlay,
        ImmutableArray<ProfileEntry>.Builder templates,
        ImmutableArray<ProfileEntry>.Builder unresolved,
        ImmutableArray<BoundComment>.Builder pending)
    {
        var lexedName = QualifiedNameLexer.Lex(record.Name!);

        if (lexedName.Name is null)
        {
            EmitNameFault(
                lexedName.Fault!.Value,
                record.Line,
                record.Column + lexedName.Fault.Value.Offset,
                source,
                diagnostics,
                key);
            return overlay;
        }

        // Section 12.1 decides wildcard recognition "before the value is lexed, from the owning
        // name's captures": in an entry whose name defines none, 'pattern=*.txt' is literal text.
        var lexedValue = ValueLexer.Lex(
            record.Value!,
            ValueSyntax.Profile(QualifiedNameLexer.CaptureForm(lexedName.Name)));

        if (lexedValue.Value is null)
        {
            // The name occupies columns [Column, Column + Name.Length), the separating '=' the one
            // after it, and the value begins immediately after that.
            EmitValueFault(
                lexedValue.Fault!.Value,
                record.Line,
                record.Column + record.Name!.Length + 1 + lexedValue.Fault.Value.Offset,
                source,
                diagnostics,
                key);
            return overlay;
        }

        var comments = pending.ToImmutable();
        pending.Clear();

        var entry = new ProfileEntry(
            lexedName.Name, lexedValue.Value, key, record.Line, comments);

        // Section 15.1 step 7: a wildcard name makes this a template rather than a contribution. It
        // is extracted here and evaluated at step 10, so it never reaches the concrete overlay.
        if (QualifiedNameLexer.ContainsWildcard(lexedName.Name))
        {
            templates.Add(entry);
            return overlay;
        }

        if (lexedValue.Value.ContainsReference || lexedValue.Value.ContainsWildcard)
        {
            unresolved.Add(entry);
            return overlay;
        }

        return Graft(overlay, lexedName.Name.Parts, 0, lexedValue.Value.LiteralText!, key, comments);
    }

    /// <summary>
    /// Rebuilds the spine from the root to one entry's path, recording the payload at the leaf.
    /// </summary>
    /// <remarks>
    /// Every node on the way down records a descendant rather than a contribution at itself, which
    /// is what keeps Section 5.2's "adding a new child never moves its parent" true for the
    /// intermediate nodes an entry brings into existence on its way past them.
    /// </remarks>
    private static OverlayNode Graft(
        OverlayNode node,
        ImmutableArray<NamePart> parts,
        int depth,
        string payload,
        StableOrderingKey key,
        ImmutableArray<BoundComment> comments)
    {
        if (depth == parts.Length)
        {
            var leaf = node.WithPayload(ScalarPayload.Untyped(payload), key);

            return comments.Aggregate(leaf, (current, comment) => current.WithComment(comment));
        }

        var name = parts[depth];
        var child = node.Children.TryGetValue(name, out var existing)
            ? existing
            : OverlayNode.Intermediate(key);

        return node.WithChild(name, Graft(child, parts, depth + 1, payload, key, comments));
    }

    private static void EmitNameFault(
        NameFault fault,
        int line,
        int column,
        string source,
        DiagnosticBuffer diagnostics,
        StableOrderingKey key)
    {
        // Appendix B maps every condition to exactly one most-specific code, and an invalid capture
        // outside a reference earns WILDCARD001 rather than the PARSE001 a malformed name earns.
        var occurrence = fault.IsWildcardFault
            ? DiagnosticCodes.Wildcard001(
                DiagnosticPhase.Input,
                "\u00A78.2",
                fault.Message,
                cardinalityKey: $"{source}:{line}:{column}",
                source: source,
                line: line,
                column: column)
            : DiagnosticCodes.Parse001(
                DiagnosticPhase.Input,
                "\u00A78.2",
                fault.Message,
                cardinalityKey: $"{source}:{line}:{column}",
                source: source,
                line: line,
                column: column);

        diagnostics.Add(new BufferedDiagnostic(occurrence, key));
    }

    private static void EmitValueFault(
        ValueFault fault,
        int line,
        int column,
        string source,
        DiagnosticBuffer diagnostics,
        StableOrderingKey key)
    {
        var occurrence = fault.Kind switch
        {
            ValueFaultKind.Reference => DiagnosticCodes.Reference001(
                DiagnosticPhase.Input,
                "\u00A78.4",
                fault.Message,
                cardinalityKey: $"{source}:{line}:{column}",
                source: source,
                line: line,
                column: column),
            ValueFaultKind.Wildcard => DiagnosticCodes.Wildcard001(
                DiagnosticPhase.Input,
                "\u00A78.3",
                fault.Message,
                cardinalityKey: $"{source}:{line}:{column}",
                source: source,
                line: line,
                column: column),
            _ => throw new InvalidOperationException(
                $"'{fault.Kind}' is not a {nameof(ValueFaultKind)}."),
        };

        diagnostics.Add(new BufferedDiagnostic(occurrence, key));
    }
}
