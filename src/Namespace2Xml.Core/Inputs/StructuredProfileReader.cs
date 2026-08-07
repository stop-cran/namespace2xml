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
    /// <param name="diagnostics">The buffer this source's diagnostics accumulate in.</param>
    /// <param name="unsupported">
    /// The first construct this preview declines, or <see langword="null"/> when it declined none.
    /// </param>
    public static ProfileContribution Read(
        StructuredNode root,
        long sourceOrdinal,
        ProfileSource source,
        DiagnosticBuffer diagnostics,
        out UnsupportedCapability? unsupported)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var projection = new Projection(sourceOrdinal, source, diagnostics);
        var overlay = projection.Build(root, []);

        unsupported = projection.Refusal;

        return new ProfileContribution(overlay, [], [], []);
    }

    private sealed class Projection(
        long sourceOrdinal, ProfileSource source, DiagnosticBuffer diagnostics)
    {
        private long ordinal;


        public UnsupportedCapability? Refusal { get; private set; }

        public OverlayNode Build(StructuredNode node, ImmutableArray<NamePart> path)
        {
            var key = StableOrderingKey.FromSource(sourceOrdinal, ++ordinal);

            return node switch
            {
                StructuredScalar scalar => BuildScalar(scalar, path, key),
                StructuredMapping mapping => BuildMapping(mapping, path, key),
                StructuredSequence sequence => BuildSequence(sequence, path, key),
                _ => throw new InvalidOperationException(
                    $"'{node.GetType().Name}' is not a {nameof(StructuredNode)} shape."),
            };
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
                if (Wildcards(property.Name))
                {
                    Decline(property);
                    continue;
                }

                var child = Build(property.Value, path.Add(property.Name));

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
                return OverlayNode.OfPayload(
                    ScalarPayload.OfString(lexed.Value.LiteralText!), key);
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
            return OverlayNode.OfPayload(
                ScalarPayload.Unresolved(
                    lexed.Value,
                    new ValueOrigin(
                        source.File,
                        source.LineOf(scalar.Line),
                        source.ColumnOf(scalar.Column))),
                key);
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

        private void Decline(StructuredProperty property) =>
            Refusal ??= new UnsupportedCapability(
                "wildcard templates in native input",
                $"the key '{property.Key}' contains an unescaped wildcard token, which Section 9.1 "
                + "keeps for compatibility but this preview does not yet evaluate for native "
                + "input. Write '\\*' for a literal asterisk.",
                "\u00A79.1");

        private static bool Wildcards(NamePart part) =>
            QualifiedNameLexer.ContainsWildcard(new QualifiedName([part]));
    }
}
