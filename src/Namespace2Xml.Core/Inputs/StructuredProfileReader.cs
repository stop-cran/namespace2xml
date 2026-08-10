using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;

namespace Namespace2Xml.Inputs;

/// <summary>
/// Section 15.1 steps 5 to 7 for every native structured format.
/// </summary>
/// <remarks>
/// <para>
/// JSON, YAML, and XML disagree about syntax and agree about meaning: a mapping becomes Section 4.2
/// children, a sequence becomes Section 5.4 ordering values, an empty container is still a Section
/// 4.4 shape contribution, and a native string still goes through the Appendix A.5 transducer. All
/// of that lives here once, so the three formats cannot drift apart on the half of their behaviour
/// that is not theirs.
/// </para>
/// <para>
/// The walk recurses, and its depth is the document's nesting, which Section 23's
/// <c>--max-depth</c> has already bounded by the time a document reaches this point.
/// </para>
/// </remarks>
public static class StructuredProfileReader
{
    /// <summary>Projects one native document into a contribution.</summary>
    /// <param name="root">The document's root node.</param>
    /// <param name="sourceOrdinal">The Section 4.7 CLI source ordinal.</param>
    /// <param name="source">The source diagnostics report this contribution against.</param>
    /// <param name="substitutes">Step 3's product: the Section 16.7 mode at each declared path.</param>
    /// <param name="diagnostics">The buffer this source's diagnostics accumulate in.</param>
    /// <param name="unsupported">
    /// The first construct this preview declines, or <see langword="null"/> when it declined none.
    /// </param>
    public static ProfileContribution Read(
        StructuredNode root,
        long sourceOrdinal,
        ProfileSource source,
        SubstituteModeMap substitutes,
        DiagnosticBuffer diagnostics,
        out UnsupportedCapability? unsupported)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(substitutes);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var projection = new Projection(sourceOrdinal, source, substitutes, diagnostics);
        var overlay = projection.Build(root, []);

        unsupported = projection.Refusal;

        return new ProfileContribution(overlay, [], projection.Templates);
    }

    private sealed class Projection(
        long sourceOrdinal,
        ProfileSource source,
        SubstituteModeMap substitutes,
        DiagnosticBuffer diagnostics)
    {
        private readonly ImmutableArray<ProfileEntry>.Builder templates =
            ImmutableArray.CreateBuilder<ProfileEntry>();

        private long ordinal;


        public UnsupportedCapability? Refusal { get; private set; }

        /// <summary>The Section 15.1 step 7 templates this document declared.</summary>
        public ImmutableArray<ProfileEntry> Templates => templates.ToImmutable();

        /// <summary>The Section 16.7 mode governing a native value at one path.</summary>
        /// <param name="path">The value's declared path, from the document root.</param>
        /// <remarks>
        /// Section 15.1 step 6 matches a pattern against "an entry's declared pre-expansion path".
        /// A native document's root scalar has no path at all, and Appendix A.2 spells a name as
        /// "one or more components", so no path-scoped pattern can name it; a directive written
        /// with no path still governs it, and <see cref="SubstituteModeMap"/> answers that.
        /// </remarks>
        private SubstituteMode Mode(ImmutableArray<NamePart> path) =>
            substitutes.IsEmpty
                ? SubstituteMode.All
                : substitutes.For(path.IsDefaultOrEmpty ? null : new QualifiedName(path));
        public OverlayNode Build(StructuredNode node, ImmutableArray<NamePart> path)
        {
            var key = StableOrderingKey.FromSource(sourceOrdinal, ++ordinal);

            var built = node switch
            {
                StructuredScalar scalar => BuildScalar(scalar, path, key),
                StructuredMapping mapping => BuildMapping(mapping, path, key),
                StructuredSequence sequence => BuildSequence(sequence, path, key),
                _ => throw new InvalidOperationException(
                    $"'{node.GetType().Name}' is not a {nameof(StructuredNode)} shape."),
            };

            var attached = AttachComments(built, node.Comments);

            // Section 11.4 records the value the node's XML parent gave it "for deterministic
            // placement", so it belongs to the node rather than to how the node was built.
            return node.ContentToken is { } token
                ? attached.WithContentToken(token)
                : attached;
        }

        /// <summary>Turns a native comment channel into Section 4.5 <see cref="BoundComment"/>s.</summary>
        /// <param name="node">The overlay node the reader built for the native value.</param>
        /// <param name="comments">The native reader's comment channel.</param>
        /// <remarks>
        /// Section 4.5 requires each retained comment to carry a Section 4.7 ordering key so that
        /// "comments contributed to the same surviving logical path accumulate in source order".
        /// The projection's own <c>++ordinal</c> is the source-order counter used for every node in
        /// this document, so allocating one more from it per comment orders comments against the
        /// nodes they were bound to as well as against each other. Their Section 4.7 key therefore
        /// falls between the value node's own key and the next value node's, which is where the
        /// source read them.
        /// </remarks>
        private OverlayNode AttachComments(
            OverlayNode node, ImmutableArray<StructuredComment> comments)
        {
            if (comments.IsDefaultOrEmpty)
            {
                return node;
            }

            var result = node;

            foreach (var comment in comments)
            {
                var order = StableOrderingKey.FromSource(sourceOrdinal, ++ordinal);
                result = result.WithComment(new BoundComment(comment.Text, comment.Placement, order));
            }

            return result;
        }

        private OverlayNode BuildMapping(
            StructuredMapping mapping, ImmutableArray<NamePart> path, StableOrderingKey key)
        {
            // Section 4.4: an empty mapping is an explicit mapping-presence contribution and
            // "participates in precedence even though it has no children". A mapping that has
            // children records its shape through them, so marking it again would say nothing new.
            // A Section 11.4 element scalar is itself a shape contribution, so a node carrying one
            // needs no mapping mark either.
            var result = mapping.Scalar is { } own
                ? BuildScalar(own, path, key)
                : mapping.Properties.IsEmpty
                    ? OverlayNode.Intermediate(key).WithExplicitMapping(key)
                    : OverlayNode.Intermediate(key);

            foreach (var property in mapping.Properties)
            {
                // Section 15.1 step 6 scopes 'substitute' to "an entry's declared pre-expansion
                // path", so the mode is read at the path the key names rather than at its parent.
                var declared = path.Add(property.Name);

                if (Wildcards(property.Name))
                {
                    if (Mode(declared).InterpretsNames())
                    {
                        Extract(property.Value, declared);
                        continue;
                    }

                    // Section 16.7 'names interpreted: no' makes the token ordinary text, and it
                    // has to stop being a token here rather than survive into a concrete path
                    // where a later matcher would read it back as a pattern.
                    var literal = QualifiedNameLexer.Literalize(new QualifiedName([property.Name]));
                    var concrete = Build(property.Value, path.Add(literal.Parts[0]));

                    if (!concrete.IsEmpty)
                    {
                        result = result.WithChild(literal.Parts[0], concrete);
                    }

                    continue;
                }

                var child = Build(property.Value, declared);

                // Section 10.4: "carrier ancestors created only to contain an extracted template
                // do not contribute mapping-presence marks". A child that contributed nothing is
                // exactly such a carrier, and attaching it would give its parent a mapping mark
                // that no surviving contribution stands behind.
                if (!child.IsEmpty)
                {
                    result = result.WithChild(property.Name, child);
                }
            }

            return result;
        }

        /// <summary>Projects one native sequence, allocating Section 5.4 ordering values.</summary>
        /// <param name="sequence">The sequence node.</param>
        /// <param name="path">The path naming the sequence itself.</param>
        /// <param name="key">The sequence's own position mark.</param>
        /// <remarks>
        /// Items are walked under the container's own path, because Section 5.4 makes the ordering
        /// value the key the item is stored under and
        /// <see cref="OverlayNode.TryAppendSequenceItem"/> is what allocates it. The path a scalar
        /// receives here is therefore short by the item's ordering value, which is why the only
        /// thing it is used for is asking whether it is empty. An unresolved reference is carried
        /// as the node's own payload rather than as an entry recorded against a path, so nothing
        /// downstream has to reconstruct where it was.
        /// </remarks>
        private OverlayNode BuildSequence(
            StructuredSequence sequence, ImmutableArray<NamePart> path, StableOrderingKey key)
        {
            var result = sequence.Items.IsEmpty
                ? OverlayNode.Intermediate(key).WithExplicitSequence(key)
                : OverlayNode.Intermediate(key);

            foreach (var item in sequence.Items)
            {
                var built = Build(item, path);

                if (!result.TryAppendSequenceItem(SequenceItem.Native(built), out result))
                {
                    diagnostics.Add(new BufferedDiagnostic(
                        DiagnosticCodes.Limit001(
                            DiagnosticPhase.Input,
                            "\u00A75.4",
                            "this sequence needs an ordering value above "
                            + $"{SequenceOrderingAllocator.MaxOrderingValue}, which Section 5.4 "
                            + "makes a blocking limit error.",
                            source: source.File,
                            line: source.LineOf(item.Line),
                            column: source.ColumnOf(item.Column)),
                        key));
                    break;
                }
            }

            return result;
        }

        private OverlayNode BuildScalar(
            StructuredScalar scalar, ImmutableArray<NamePart> path, StableOrderingKey key)
        {
            // Section 18: "Typed JSON and YAML input scalars retain their source kind without
            // re-inference", so a number, a Boolean, and null are finished where they were read.
            if (scalar.Payload is { } typed)
            {
                return OverlayNode.OfPayload(typed, key);
            }

            // Section 13.4: "Native JSON, YAML, and XML strings matched by Key or None are
            // preserved exactly after native format decoding; no transformer escape decoding is
            // applied." The value is therefore not lexed at all under those modes — not lexed
            // with interpretation switched off, which would still consume the Appendix A.5
            // escapes and turn a literal '\*' into '*'.
            if (!Mode(path).InterpretsValues())
            {
                var preserved = ScalarPayload.OfString(scalar.NativeString!);

                return OverlayNode.OfPayload(
                    scalar.IsCdata ? preserved.AsCdata() : preserved, key);
            }

            // Section 12.1 reads a value's wildcard form from its owning name's captures. This
            // preview declines a native name that defines any, so no value here can substitute one.
            var lexed = ValueLexer.Lex(
                scalar.NativeString!, ValueSyntax.NativeString(WildcardSyntax.None));

            if (lexed.Value is null)
            {
                EmitValueFault(lexed.Fault!.Value, scalar, key);
                return OverlayNode.Intermediate(key);
            }

            if (!lexed.Value.ContainsReference)
            {
                var payload = ScalarPayload.OfString(lexed.Value.LiteralText!);

                return OverlayNode.OfPayload(
                    scalar.IsCdata ? payload.AsCdata() : payload, key);
            }

            if (path.IsEmpty)
            {
                diagnostics.Add(new BufferedDiagnostic(
                    DiagnosticCodes.Reference001(
                        DiagnosticPhase.Input,
                        "\u00A78.4",
                        "a reference at the root of a native document has no owning path, so "
                        + "nothing names the value it would resolve for.",
                        cardinalityKey: source.SourceKey,
                        source: source.File,
                        line: source.LineOf(scalar.Line),
                        column: source.ColumnOf(scalar.Column)),
                    key));
                return OverlayNode.Intermediate(key);
            }

            // Section 13.1 resolves this at step 15. It is a payload here, not a node held aside:
            // an Intermediate node would tell Section 4.4 that this path has no scalar at all, so a
            // reference to it would be a missing-reference error and a mapping written at the same
            // path would win a contest it should lose.
            var unresolved = ScalarPayload.Unresolved(
                lexed.Value,
                new ValueOrigin(
                    source.File,
                    source.LineOf(scalar.Line),
                    source.ColumnOf(scalar.Column)));

            return OverlayNode.OfPayload(
                scalar.IsCdata ? unresolved.AsCdata() : unresolved, key);
        }

        private void EmitValueFault(
            ValueFault fault, StructuredScalar scalar, StableOrderingKey key)
        {
            var occurrence = fault.Kind switch
            {
                ValueFaultKind.Reference => DiagnosticCodes.Reference001(
                    DiagnosticPhase.Input,
                    "\u00A78.4",
                    fault.Message,
                    cardinalityKey: source.Key(scalar.Line, scalar.Column),
                    source: source.File,
                    line: source.LineOf(scalar.Line),
                    column: source.ColumnOf(scalar.Column)),
                ValueFaultKind.Wildcard => DiagnosticCodes.Wildcard001(
                    DiagnosticPhase.Input,
                    "\u00A712.1",
                    fault.Message,
                    cardinalityKey: source.Key(scalar.Line, scalar.Column),
                    source: source.File,
                    line: source.LineOf(scalar.Line),
                    column: source.ColumnOf(scalar.Column)),
                _ => throw new InvalidOperationException(
                    $"'{fault.Kind}' is not a {nameof(ValueFaultKind)}."),
            };

            diagnostics.Add(new BufferedDiagnostic(occurrence, key));
        }

        private void Decline(StructuredNode node, ImmutableArray<NamePart> path, string what) =>
            Refusal ??= new UnsupportedCapability(
                "a native wildcard template over " + what,
                $"the template at '{CanonicalPath.Of(new QualifiedName(path))}' in {source.File} "
                + $"reaches {what} at "
                + $"line {source.LineOf(node.Line)}. Section 10.4 extracts a template "
                + "entry by entry, and an entry names one scalar; this build does not yet decide "
                + "what a template over that shape extracts to. Write the branch without a "
                + "wildcard key, or write '\\*' for a literal asterisk.",
                "\u00A710.4");

        /// <summary>
        /// Section 10.4 step 7 for a native document: extracts the subtree under a wildcard key as
        /// template entries rather than as concrete contributions.
        /// </summary>
        /// <param name="node">The value the wildcard key names.</param>
        /// <param name="path">The declared path, whose last part carries the wildcard tokens.</param>
        /// <remarks>
        /// <para>
        /// "Wildcard template entries are extracted before structural input merging", and
        /// "extraction is entry-by-entry" — so one <see cref="ProfileEntry"/> per scalar leaf,
        /// named by the whole path from the document root, exactly as
        /// <see cref="NamespaceProfileReader"/> produces from <c>a.*.c=XXX</c>. Nothing is added to
        /// the overlay, which is what makes the ancestors that only carried the template contribute
        /// no mapping-presence mark.
        /// </para>
        /// <para>
        /// Ordering keys are allocated from the same counter the concrete walk uses, so Section
        /// 12.4's "one deterministic worklist ordered by source order" interleaves templates from
        /// this source with the entries written around them.
        /// </para>
        /// </remarks>
        private void Extract(StructuredNode node, ImmutableArray<NamePart> path)
        {
            var key = StableOrderingKey.FromSource(sourceOrdinal, ++ordinal);

            switch (node)
            {
                case StructuredScalar scalar:
                    Emit(scalar, path, key);
                    return;

                case StructuredMapping { Scalar: { } own }:
                    Emit(own, path, key);
                    return;

                case StructuredMapping { Properties.IsEmpty: false } mapping:
                    foreach (var property in mapping.Properties)
                    {
                        Extract(property.Value, path.Add(property.Name));
                    }

                    return;

                // An empty mapping and a sequence are both Section 4.4 shape contributions rather
                // than scalars, and a ProfileEntry carries a value. Section 10.4 shows neither, so
                // the honest answer is that this build has not decided.
                case StructuredMapping:
                    Decline(node, path, "an empty mapping");
                    return;

                case StructuredSequence:
                    Decline(node, path, "a sequence");
                    return;

                default:
                    throw new InvalidOperationException(
                        $"'{node.GetType().Name}' is not a {nameof(StructuredNode)} shape.");
            }
        }

        /// <summary>Records one template entry for a scalar leaf under a wildcard key.</summary>
        /// <param name="scalar">The leaf.</param>
        /// <param name="path">The template's whole name.</param>
        /// <param name="key">The entry's Section 4.7 ordering key.</param>
        /// <remarks>
        /// Section 12.1 reads a value's capture form "from the owning name's captures", and the
        /// owning name here is the template's, so a bare <c>*</c> in the value substitutes what the
        /// key's <c>*</c> matched. That is the one place a native value's wildcard syntax is not
        /// <see cref="WildcardSyntax.None"/>.
        /// </remarks>
        private void Emit(StructuredScalar scalar, ImmutableArray<NamePart> path, StableOrderingKey key)
        {
            var name = new QualifiedName(path);

            // Section 18 keeps a typed native scalar's kind "without re-inference", and a template
            // entry carries text that Section 12.4 re-lexes for the generated entry. Canonical text
            // is what Section 18 defines as the typed scalar's spelling, so it is the projection
            // that loses nothing a reader could observe.
            var text = scalar.Payload is { } typed ? typed.ToCanonicalText() : scalar.NativeString!;

            if (!Mode(path).InterpretsValues())
            {
                templates.Add(new ProfileEntry(
                    name, Literal(text), key, source.LineOf(scalar.Line) ?? 0, []));
                return;
            }

            var lexed = ValueLexer.Lex(
                text, ValueSyntax.NativeString(QualifiedNameLexer.CaptureForm(name)));

            if (lexed.Value is null)
            {
                EmitValueFault(lexed.Fault!.Value, scalar, key);
                return;
            }

            templates.Add(new ProfileEntry(
                name, lexed.Value, key, source.LineOf(scalar.Line) ?? 0, []));
        }

        private static InterpretedValue Literal(string text) =>
            new([new LiteralValueToken(text)]);

        private static bool Wildcards(NamePart part) =>
            QualifiedNameLexer.ContainsWildcard(new QualifiedName([part]));
    }
}
