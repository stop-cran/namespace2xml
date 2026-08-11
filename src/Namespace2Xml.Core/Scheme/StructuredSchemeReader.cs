using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Inputs;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;

namespace Namespace2Xml.Scheme;

/// <summary>
/// Reads a JSON or YAML document into Section 15 directives.
/// </summary>
/// <remarks>
/// <para>
/// This is Section 15.1 step 1 for the structured scheme formats. Section 15 admits them — "Scheme
/// files may use the same case-insensitive format extensions as input files for compatibility. Their
/// parsed content must project to qualified directive paths and scalar directive values" — and
/// Sections 9.1 and 10.4 supply the projection: "Each object-property name becomes one literal
/// qualified-name part", with unescaped <c>*</c> and <c>*[identifier]</c> retaining their
/// wildcard-template meaning inside it.
/// </para>
/// <para>
/// A mapping with properties is therefore a path, and everything else is a declaration site. That
/// single rule is what makes the projection total: no node is silently walked past, so a scheme
/// cannot declare less than its author wrote without saying so. A sequence or an empty mapping
/// reaches the declaration site and earns Section 15's "container value" <c>SCHEME001</c>, which is
/// the same answer a profile gives for the same intent expressed differently.
/// </para>
/// <para>
/// XML is deliberately not read here. Section 15 names XML scheme files once, about secure parsing,
/// and never says how an XML document projects to directive paths; <c>SchemePhase</c> refuses one
/// before it is read. Consequently <see cref="StructuredMapping.Scalar"/> — which only the XML
/// reader sets — cannot occur below, and this reader does not attempt to guess what it would mean.
/// </para>
/// </remarks>
public static class StructuredSchemeReader
{
    /// <summary>Reads one structured scheme document.</summary>
    /// <param name="document">The parsed native document.</param>
    /// <param name="sourceOrdinal">The Section 4.7 CLI source ordinal.</param>
    /// <param name="source">The source name diagnostics report.</param>
    /// <param name="diagnostics">The buffer this source's diagnostics accumulate in.</param>
    /// <returns>The directives, in document order.</returns>
    public static SchemeContribution Read(
        StructuredNode document,
        long sourceOrdinal,
        string source,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var state = new Walk(sourceOrdinal, source, diagnostics);

        // Section 15.2 orders directives by source order, and a structured document's source order
        // is its document order. A within-source counter carries it rather than the line, because a
        // JSON object may write every property on one line and two directives sharing an ordering
        // key would make their override stream depend on the dictionary that held them.
        state.Visit(document, []);

        return new SchemeContribution(state.Entries.ToImmutable());
    }

    private sealed class Walk(long sourceOrdinal, string source, DiagnosticBuffer diagnostics)
    {
        private long ordinal;

        public ImmutableArray<SchemeEntry>.Builder Entries { get; } =
            ImmutableArray.CreateBuilder<SchemeEntry>();

        public void Visit(StructuredNode node, ImmutableArray<NamePart> path)
        {
            if (node is StructuredMapping { Properties.IsEmpty: false } mapping)
            {
                foreach (var property in mapping.Properties)
                {
                    Visit(property.Value, path.Add(property.Name));
                }

                return;
            }

            Declare(node, path);
        }

        private void Declare(StructuredNode node, ImmutableArray<NamePart> path)
        {
            var key = StableOrderingKey.FromSource(sourceOrdinal, ++ordinal);

            // A root scalar or a root sequence has no path at all, so no part of it can be the
            // "final qualified-name part" Section 15 makes a directive. It also spells no
            // declaration, and a synthetic one would be indistinguishable from written text.
            if (path.IsEmpty)
            {
                Reject(
                    $"the document's root value is a {Describe(node)} rather than a mapping, so it "
                    + "spells no qualified directive path, and Section 15 admits only those in a "
                    + "scheme.",
                    node,
                    key,
                    name: null,
                    declaration: null);
                return;
            }

            var name = new QualifiedName(path);
            var declaration = CanonicalPath.Of(name)!;

            // Section 15: "The final qualified-name part identifies a directive." A wildcard
            // component spells no directive name, so it cannot be the final part — a scheme may
            // select 'app.*' and set 'output' on it, never 'app.*'.
            if (path[^1] is not OrdinaryPart { LiteralText: { } directiveName })
            {
                Reject(
                    "the final name part is not literal text, so it identifies no Section 15 "
                    + "directive.",
                    node,
                    key,
                    name,
                    declaration);
                return;
            }

            if (!SchemeDirectives.TryRecognize(directiveName, out var directive, out var alias))
            {
                Reject(
                    $"'{directiveName}' is not a recognized Section 15 directive."
                        + AcceptedValues.Sentence(SchemeDirectives.Spellings),
                    node,
                    key,
                    name,
                    declaration);
                return;
            }

            if (!TryReadValue(node, name, declaration, directiveName, directive, key, out var value))
            {
                return;
            }

            if (alias != SchemeAlias.None)
            {
                diagnostics.Add(new BufferedDiagnostic(
                    DiagnosticCodes.Warn002(
                        DiagnosticPhase.Scheme,
                        "\u00A715.3",
                        $"'{SchemeDirectives.Spelling(alias)}' is a deprecated alias for "
                        + $"'{SchemeDirectives.Replacement(alias)}'.",
                        cardinalityKey: $"{source}:{alias}",
                        source: source,
                        line: node.Line,
                        column: node.Column,
                        declaration: declaration),
                    key));
            }

            Entries.Add(new SchemeEntry(
                path.Length == 1 ? null : new QualifiedName([.. path[..^1]]),
                directive,
                value!,
                key,
                node.Line,
                source,
                declaration));
        }

        private bool TryReadValue(
            StructuredNode node,
            QualifiedName name,
            string declaration,
            string directiveName,
            SchemeDirective directive,
            StableOrderingKey key,
            out InterpretedValue? value)
        {
            value = null;

            // Section 15: "An empty value, null, container value, unknown directive value, or
            // illegal option/type combination is SCHEME001." The first three are decided here; the
            // last two belong to the directive's own section and are decided by the compiler.
            if (node is not StructuredScalar scalar)
            {
                Reject(
                    $"'{directiveName}' has a {Describe(node)} value, and Section 15 requires "
                    + "every directive to carry a nonempty scalar one.",
                    node,
                    key,
                    name,
                    declaration);
                return false;
            }

            if (scalar.Payload is { } payload)
            {
                if (payload.IsNull)
                {
                    Reject(
                        $"'{directiveName}' has a null value, and Section 15 requires every "
                        + "directive to carry a nonempty scalar one.",
                        node,
                        key,
                        name,
                        declaration);
                    return false;
                }

                // Section 15 asks for a scalar value "after format parsing", and Section 18 keeps a
                // typed JSON or YAML scalar in its source kind. Its Section 18 canonical text is
                // therefore the value the directive was given: 'true' for a native Boolean, and no
                // culture anywhere in the decision.
                value = Literal(payload.ToCanonicalText());
                return true;
            }

            // Section 9.1: "String scalar values use the same strict reference and value-escape
            // lexer as namespace values." Section 12.1 decides the capture form from the owning
            // name, exactly as the profile reader does, and the directive part is literal by the
            // check above, so the whole name gives the same answer -- except for the two directives
            // Section 12.1 excludes, which SchemeDirectives.CaptureForm applies.
            var lexed = ValueLexer.Lex(
                scalar.NativeString!,
                ValueSyntax.NativeString(
                    SchemeDirectives.CaptureForm(directive, QualifiedNameLexer.CaptureForm(name))));

            if (lexed.Value is null)
            {
                EmitValueFault(lexed.Fault!.Value, node, key);
                return false;
            }

            if (lexed.Value.LiteralText is { Length: 0 })
            {
                Reject(
                    $"'{directiveName}' has an empty value, and Section 15 requires every "
                    + "directive to carry a nonempty scalar one.",
                    node,
                    key,
                    name,
                    declaration);
                return false;
            }

            value = lexed.Value;
            return true;
        }

        private void Reject(
            string message,
            StructuredNode node,
            StableOrderingKey key,
            QualifiedName? name,
            string? declaration) =>
            diagnostics.Add(new BufferedDiagnostic(
                DiagnosticCodes.Scheme001(
                    DiagnosticPhase.Scheme,
                    "\u00A715",
                    message,
                    cardinalityKey: $"{source}:{node.Line}:{node.Column}",
                    source: source,
                    line: node.Line,
                    column: node.Column,
                    path: CanonicalPath.Of(name),
                    declaration: declaration),
                key));

        private void EmitValueFault(ValueFault fault, StructuredNode node, StableOrderingKey key)
        {
            // Appendix B maps every condition to exactly one most-specific code, and the codes a
            // malformed value earns do not change because the value arrived as a native string.
            // The offset is not added to the column: a native string's decoded text and its source
            // spelling are different strings, so an offset into the first names no position in the
            // second.
            var occurrence = fault.Kind is ValueFaultKind.Wildcard
                ? DiagnosticCodes.Wildcard001(
                    DiagnosticPhase.Scheme,
                    "\u00A79.1",
                    fault.Message,
                    cardinalityKey: $"{source}:{node.Line}:{node.Column}",
                    source: source,
                    line: node.Line,
                    column: node.Column)
                : DiagnosticCodes.Reference001(
                    DiagnosticPhase.Scheme,
                    "\u00A78.4",
                    fault.Message,
                    cardinalityKey: $"{source}:{node.Line}:{node.Column}",
                    source: source,
                    line: node.Line,
                    column: node.Column);

            diagnostics.Add(new BufferedDiagnostic(occurrence, key));
        }
    }

    private static InterpretedValue Literal(string text) =>
        new([new LiteralValueToken(text)]);

    private static string Describe(StructuredNode node) => node switch
    {
        StructuredSequence => "sequence",
        StructuredMapping => "mapping",
        _ => "scalar",
    };
}
