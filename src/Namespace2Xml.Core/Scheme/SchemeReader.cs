using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using Namespace2Xml.Text;

namespace Namespace2Xml.Scheme;

/// <summary>
/// One Section 15 directive, as written.
/// </summary>
/// <param name="Selector">
/// The qualified name before the directive name: a Section 15.2 output selector, or the Section
/// 16.10 path a <c>merge</c> governs. Null when the directive is written at the root.
/// </param>
/// <param name="Directive">The directive the final name part identifies, after alias resolution.</param>
/// <param name="Value">The interpreted directive value.</param>
/// <param name="Order">The entry's Section 4.7 key, which is also its source order.</param>
/// <param name="Line">The one-based line it was written on.</param>
/// <param name="Source">The source name diagnostics report.</param>
/// <param name="Declaration">The written directive text, for the <c>declaration</c> field.</param>
public sealed record SchemeEntry(
    QualifiedName? Selector,
    SchemeDirective Directive,
    InterpretedValue Value,
    StableOrderingKey Order,
    int Line,
    string Source,
    string Declaration);

/// <summary>One scheme source, read into its Section 15 directives.</summary>
/// <param name="Entries">The directives, in source order.</param>
public sealed record SchemeContribution(ImmutableArray<SchemeEntry> Entries);

/// <summary>
/// Reads classified Section 8.1 records into Section 15 directives.
/// </summary>
/// <remarks>
/// <para>
/// This is Section 15.1 step 1 for the namespace-profile scheme format, which Section 15 calls "the
/// canonical and recommended representation". It recognizes directive names and rejects what cannot
/// be one; it does not interpret directive values, which each directive's own section governs.
/// </para>
/// <para>
/// A scheme builds no overlay. Section 15 requires scheme content to "project to qualified directive
/// paths and scalar directive values", and a directive is not data: grafting one into a tree would
/// make <c>a.output</c> and <c>a.output.x</c> parent and child, when the second is not a directive
/// at all.
/// </para>
/// </remarks>
public static class SchemeReader
{
    /// <summary>Reads one scheme source's records.</summary>
    /// <param name="records">The Section 8.1 classified records, in source order.</param>
    /// <param name="sourceOrdinal">The Section 4.7 CLI source ordinal.</param>
    /// <param name="source">The source name diagnostics report.</param>
    /// <param name="diagnostics">The buffer this source's diagnostics accumulate in.</param>
    public static SchemeContribution Read(
        ImmutableArray<NamespaceRecord> records,
        long sourceOrdinal,
        string source,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var entries = ImmutableArray.CreateBuilder<SchemeEntry>();


        foreach (var record in records)
        {
            var key = StableOrderingKey.FromSource(sourceOrdinal, record.Line);

            switch (record.Kind)
            {
                case NamespaceRecordKind.Ignored:
                case NamespaceRecordKind.Comment:
                    break;

                case NamespaceRecordKind.Malformed:
                    diagnostics.Add(new BufferedDiagnostic(
                        DiagnosticCodes.Parse001(
                            DiagnosticPhase.Scheme,
                            "\u00A78.1",
                            "this record is neither a comment nor a mask and has no separating '=', "
                            + "so Section 8.1 rule 5 makes it a parse error.",
                            cardinalityKey: source,
                            source: source,
                            line: record.Line,
                            column: record.Column),
                        key));
                    break;

                case NamespaceRecordKind.Mask:
                    Reject(
                        "a '!' mask projects to neither a directive path nor a scalar value, and "
                        + "Section 15 admits only those in a scheme.",
                        record,
                        source,
                        diagnostics,
                        key,
                        declaration: "!" + record.Pattern);
                    break;

                case NamespaceRecordKind.Entry:
                    ReadEntry(record, key, source, diagnostics, entries);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Section 8.1 classifies records into the {nameof(NamespaceRecordKind)} "
                        + $"members; '{record.Kind}' is not one of them.");
            }
        }

        return new SchemeContribution(entries.ToImmutable());
    }

    private static void ReadEntry(
        NamespaceRecord record,
        StableOrderingKey key,
        string source,
        DiagnosticBuffer diagnostics,
        ImmutableArray<SchemeEntry>.Builder entries)
    {
        var written = record.Name!;
        var lexedName = QualifiedNameLexer.Lex(written);

        if (lexedName.Name is null)
        {
            EmitNameFault(lexedName.Fault!.Value, record, source, diagnostics, key);
            return;
        }

        var parts = lexedName.Name.Parts;

        // Section 15: "The final qualified-name part identifies a directive." A wildcard or typed
        // component spells no directive name, so it cannot be the final part. Rejecting it here
        // rather than failing to match keeps the reason in the message.
        if (parts[^1] is not OrdinaryPart { LiteralText: { } name })
        {
            Reject(
                "the final name part is not literal text, so it identifies no Section 15 directive.",
                record,
                source,
                diagnostics,
                key,
                declaration: written);
            return;
        }

        if (!SchemeDirectives.TryRecognize(name, out var directive, out var alias))
        {
            Reject(
                $"'{name}' is not a recognized Section 15 directive.",
                record,
                source,
                diagnostics,
                key,
                declaration: written);
            return;
        }

        // Section 12.1: a scheme directive's value is decided from the captures its selector
        // defines, and 'substitute' does not apply to scheme declarations. The directive part is
        // literal by the check above, so the whole written name gives the same answer.
        var lexedValue = ValueLexer.Lex(
            record.Value!,
            ValueSyntax.Profile(QualifiedNameLexer.CaptureForm(lexedName.Name)));

        if (lexedValue.Value is null)
        {
            EmitValueFault(lexedValue.Fault!.Value, record, source, diagnostics, key);
            return;
        }

        // Section 15: "Every recognized directive requires a nonempty scalar value after format
        // parsing." A value that is only a reference is not empty; it is unresolved until step 1
        // resolves it, and emptiness is judged after that.
        if (lexedValue.Value.LiteralText is { Length: 0 })
        {
            Reject(
                $"'{name}' has an empty value, and Section 15 requires every directive to carry a "
                + "nonempty scalar one.",
                record,
                source,
                diagnostics,
                key,
                declaration: written);
            return;
        }

        // Section 15.3 and the registry raise WARN002 "once per alias category and scheme". The
        // cardinality key below is that scope, and DiagnosticBuffer keeps one occurrence per key,
        // so a second guard here would be a second mechanism enforcing one rule.
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
                    line: record.Line,
                    column: record.Column,
                    declaration: written),
                key));
        }

        entries.Add(new SchemeEntry(
            parts.Length == 1 ? null : new QualifiedName([.. parts[..^1]]),
            directive,
            lexedValue.Value,
            key,
            record.Line,
            source,
            written));
    }

    private static void Reject(
        string message,
        NamespaceRecord record,
        string source,
        DiagnosticBuffer diagnostics,
        StableOrderingKey key,
        string declaration) =>
        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Scheme001(
                DiagnosticPhase.Scheme,
                "\u00A715",
                message,
                cardinalityKey: $"{source}:{record.Line}",
                source: source,
                line: record.Line,
                column: record.Column,
                path: declaration,
                declaration: declaration),
            key));

    private static void EmitNameFault(
        NameFault fault,
        NamespaceRecord record,
        string source,
        DiagnosticBuffer diagnostics,
        StableOrderingKey key)
    {
        var column = record.Column + ScalarColumn.Advance(record.Name!, fault.Offset);

        // Appendix B maps every condition to exactly one most-specific code, and the codes a
        // malformed name earns do not change because the name was written in a scheme. Only the
        // phase does.
        var occurrence = fault.IsWildcardFault
            ? DiagnosticCodes.Wildcard001(
                DiagnosticPhase.Scheme,
                "\u00A78.2",
                fault.Message,
                cardinalityKey: $"{source}:{record.Line}",
                source: source,
                line: record.Line,
                column: column)
            : DiagnosticCodes.Parse001(
                DiagnosticPhase.Scheme,
                "\u00A78.2",
                fault.Message,
                cardinalityKey: source,
                source: source,
                line: record.Line,
                column: column);

        diagnostics.Add(new BufferedDiagnostic(occurrence, key));
    }

    private static void EmitValueFault(
        ValueFault fault,
        NamespaceRecord record,
        string source,
        DiagnosticBuffer diagnostics,
        StableOrderingKey key)
    {
        var column = record.Column
            + ScalarColumn.Width(record.Name!)
            + 1
            + ScalarColumn.Advance(record.Value!, fault.Offset);

        var occurrence = fault.Kind is ValueFaultKind.Wildcard
            ? DiagnosticCodes.Wildcard001(
                DiagnosticPhase.Scheme,
                "\u00A78.3",
                fault.Message,
                cardinalityKey: $"{source}:{record.Line}",
                source: source,
                line: record.Line,
                column: column)
            : DiagnosticCodes.Reference001(
                DiagnosticPhase.Scheme,
                "\u00A78.4",
                fault.Message,
                cardinalityKey: $"{source}:{record.Line}",
                source: source,
                line: record.Line,
                column: column);

        diagnostics.Add(new BufferedDiagnostic(occurrence, key));
    }
}
