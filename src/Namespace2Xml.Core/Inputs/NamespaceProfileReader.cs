using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using Namespace2Xml.Text;

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
/// <remarks>
/// <para>
/// There is deliberately no separate list of reference-bearing entries. Section 13.1 resolves
/// references after ordinary merging, so such an entry is an ordinary contribution in
/// <paramref name="Overlay"/> carrying a <see cref="ScalarKind.Unresolved"/> payload, and the
/// merged model is the only place step 15 has to look.
/// </para>
/// <para>
/// There is likewise no separate list of document-trailing comments. Section 4.5 gives them "no
/// value owner", which the overlay expresses by binding them to the root of
/// <paramref name="Overlay"/>; a list beside the overlay would have to be threaded through merging
/// and view selection to reach an output, and a channel that reaches no output silently loses the
/// comments it carries.
/// </para>
/// </remarks>
public sealed record ProfileContribution(
    OverlayNode Overlay,
    ImmutableArray<ProfileMask> Masks,
    ImmutableArray<ProfileEntry> Templates);

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
    /// <param name="source">The source diagnostics report this contribution against.</param>
    /// <param name="substitutes">Step 3's product: the Section 16.7 mode at each declared path.</param>
    /// <param name="diagnostics">The buffer this source's diagnostics accumulate in.</param>
    public static ProfileContribution Read(
        ImmutableArray<NamespaceRecord> records,
        long sourceOrdinal,
        ProfileSource source,
        SubstituteModeMap substitutes,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(substitutes);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var overlay = OverlayNode.Intermediate(StableOrderingKey.FromSource(sourceOrdinal, 0));
        var masks = ImmutableArray.CreateBuilder<ProfileMask>();
        var templates = ImmutableArray.CreateBuilder<ProfileEntry>();
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
                            cardinalityKey: source.SourceKey,
                            source: source.File,
                            line: source.LineOf(record.Line),
                            column: source.ColumnOf(record.Column)),
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
                        record, key, source, substitutes, diagnostics, overlay, templates, pending);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Section 8.1 classifies records into the {nameof(NamespaceRecordKind)} "
                        + $"members; '{record.Kind}' is not one of them.");
            }
        }

        return new ProfileContribution(
            AttachDocumentTrailing(overlay, pending),
            masks.ToImmutable(),
            templates.ToImmutable());
    }

    /// <summary>Binds the comments no entry ever claimed to the contribution root.</summary>
    /// <param name="overlay">This source's contributions.</param>
    /// <param name="pending">The comments still unbound when the source ended.</param>
    /// <returns>The overlay, carrying any document-trailing comments at its root.</returns>
    /// <remarks>
    /// A comment still pending at end of source had no following entry to bind to, so Section 20's
    /// "a comment after the final payload or item is document-trailing" classifies it and Section
    /// 4.5 leaves it without a value owner. The root is the node that expresses that: it is never
    /// re-addressed and never removed by an ignore mask, and Section 15.1's view selection carries
    /// root comments into each output instance, where Section 20 places them after "its final
    /// surviving contribution".
    /// </remarks>
    private static OverlayNode AttachDocumentTrailing(
        OverlayNode overlay, ImmutableArray<BoundComment>.Builder pending)
    {
        var result = overlay;

        foreach (var comment in pending)
        {
            result = result.WithComment(comment with { Placement = CommentPlacement.Trailing });
        }

        return result;
    }

    private static void ReadMask(
        NamespaceRecord record,
        StableOrderingKey key,
        ProfileSource source,
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
                record.Column + ScalarColumn.Advance(pattern, lexed.Fault.Value.Offset),
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
        ProfileSource source,
        SubstituteModeMap substitutes,
        DiagnosticBuffer diagnostics,
        OverlayNode overlay,
        ImmutableArray<ProfileEntry>.Builder templates,
        ImmutableArray<BoundComment>.Builder pending)
    {
        var lexedName = QualifiedNameLexer.Lex(record.Name!);

        if (lexedName.Name is null)
        {
            EmitNameFault(
                lexedName.Fault!.Value,
                record.Line,
                record.Column + ScalarColumn.Advance(record.Name!, lexedName.Fault.Value.Offset),
                source,
                diagnostics,
                key);
            return overlay;
        }

        // Section 15.1 step 6 resolves the mode against "an entry's declared pre-expansion path",
        // which is this name as written — before any template it may be expands.
        var mode = substitutes.IsEmpty
            ? SubstituteMode.All
            : substitutes.For(lexedName.Name);

        var name = mode.InterpretsNames()
            ? lexedName.Name
            : QualifiedNameLexer.Literalize(lexedName.Name);

        // Section 12.1 decides wildcard recognition "before the value is lexed, from the owning
        // name's captures": in an entry whose name defines none, 'pattern=*.txt' is literal text.
        // Under a mode that does not interpret names there are no captures to define, so the two
        // halves of Section 16.7's table meet here rather than needing to be kept in step.
        var lexedValue = ValueLexer.Lex(
            record.Value!,
            mode.InterpretsValues()
                ? ValueSyntax.Profile(QualifiedNameLexer.CaptureForm(name))
                : ValueSyntax.ProfileUninterpreted);

        if (lexedValue.Value is null)
        {
            // The name occupies columns [Column, Column + the name's width in Section 22 columns),
            // after it, and the value begins immediately after that.
            EmitValueFault(
                lexedValue.Fault!.Value,
                record.Line,
                record.Column
                    + ScalarColumn.Width(record.Name!)
                    + 1
                    + ScalarColumn.Advance(record.Value!, lexedValue.Fault.Value.Offset),
                source,
                diagnostics,
                key);
            return overlay;
        }

        var comments = pending.ToImmutable();
        pending.Clear();

        var entry = new ProfileEntry(
            name, lexedValue.Value, key, record.Line, comments);

        // Section 15.1 step 7: a wildcard name makes this a template rather than a contribution. It
        // is extracted here and evaluated at step 10, so it never reaches the concrete overlay.
        // Literalize has already removed the tokens under a mode that does not interpret names, so
        // such an entry is concrete here without a second mode test.
        if (QualifiedNameLexer.ContainsWildcard(name))
        {
            templates.Add(entry);
            return overlay;
        }

        if (lexedValue.Value.ContainsReference || lexedValue.Value.ContainsWildcard)
        {
            // Section 13.1 resolves references "after wildcard generation and ordinary data
            // merging", so the entry is grafted as an ordinary scalar contribution carrying an
            // unresolved payload. Holding it aside instead would exclude it from the Section 17.1
            // merge it must win or lose, from the Section 4.4 shape contest at its node, and from
            // the Section 8.6 masks and Section 12 templates that see every other contribution.
            //
            // Section 22 counts a reference diagnostic once per reachable owning value, so the
            // origin names this value: the record it was written on, at the column it starts.
            var origin = new ValueOrigin(
                source.File,
                source.LineOf(record.Line),
                source.ColumnOf(record.Column + ScalarColumn.Width(record.Name!) + 1));

            return Graft(
                overlay,
                name.Parts,
                0,
                ScalarPayload.Unresolved(lexedValue.Value, origin),
                key,
                comments);
        }

        return Graft(
            overlay,
            name.Parts,
            0,
            ScalarPayload.Untyped(lexedValue.Value.LiteralText!),
            key,
            comments);
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
        ScalarPayload payload,
        StableOrderingKey key,
        ImmutableArray<BoundComment> comments)
    {
        if (depth == parts.Length)
        {
            var leaf = node.WithPayload(payload, key);

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
        ProfileSource source,
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
                cardinalityKey: source.RecordKey(line),
                source: source.File,
                line: source.LineOf(line),
                column: source.ColumnOf(column))
            : DiagnosticCodes.Parse001(
                DiagnosticPhase.Input,
                "\u00A78.2",
                fault.Message,
                cardinalityKey: source.SourceKey,
                source: source.File,
                line: source.LineOf(line),
                column: source.ColumnOf(column));

        diagnostics.Add(new BufferedDiagnostic(occurrence, key));
    }

    private static void EmitValueFault(
        ValueFault fault,
        int line,
        int column,
        ProfileSource source,
        DiagnosticBuffer diagnostics,
        StableOrderingKey key)
    {
        var occurrence = fault.Kind switch
        {
            ValueFaultKind.Reference => DiagnosticCodes.Reference001(
                DiagnosticPhase.Input,
                "\u00A78.4",
                fault.Message,
                cardinalityKey: source.RecordKey(line),
                source: source.File,
                line: source.LineOf(line),
                column: source.ColumnOf(column)),
            ValueFaultKind.Wildcard => DiagnosticCodes.Wildcard001(
                DiagnosticPhase.Input,
                "\u00A78.3",
                fault.Message,
                cardinalityKey: source.RecordKey(line),
                source: source.File,
                line: source.LineOf(line),
                column: source.ColumnOf(column)),
            _ => throw new InvalidOperationException(
                $"'{fault.Kind}' is not a {nameof(ValueFaultKind)}."),
        };

        diagnostics.Add(new BufferedDiagnostic(occurrence, key));
    }
}
