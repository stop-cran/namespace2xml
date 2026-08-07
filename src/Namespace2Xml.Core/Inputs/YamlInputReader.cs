using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using Namespace2Xml.Budgets;
using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using Namespace2Xml.Scalars;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Namespace2Xml.Inputs;

/// <summary>
/// The Section 10 YAML reader: one <c>RestrictedYaml1</c> document into the Section 4.2 shapes.
/// </summary>
/// <remarks>
/// <para>
/// Section 10.1 makes <c>RestrictedYaml1</c> normative "rather than an underlying library's
/// advertised YAML 1.1 or 1.2 mode", so scalar resolution is performed here and YamlDotNet is used
/// only as a scanner and event source. Its own schema is never consulted: it would resolve
/// <c>yes</c> to a Boolean and <c>1.</c> to a number, both of which Section 10.1 keeps as strings.
/// </para>
/// <para>
/// The walk keeps its own stack rather than recursing, for the reason Section 7.3 gives in
/// <see cref="JsonInputReader"/>: <c>--max-depth</c> can be raised, and a recursive reader turns a
/// raised bound into a stack overflow instead of a <c>LIMIT001</c>.
/// </para>
/// </remarks>
public static class YamlInputReader
{
    /// <summary>Reads one YAML document.</summary>
    /// <param name="text">The decoded document text.</param>
    /// <param name="limits">The Section 23 bounds.</param>
    /// <param name="budget">This source's budget, already charged for its bytes.</param>
    /// <param name="origin">How diagnostics name this source.</param>
    /// <param name="phase">The phase its diagnostics report.</param>
    /// <param name="diagnostics">The buffer its diagnostics accumulate in.</param>
    /// <param name="key">The Section 4.7 key its diagnostics are ordered by.</param>
    /// <returns>
    /// The document's root node, or <see langword="null"/> when it contributed nothing. A
    /// <c>null</c> return with no diagnostic added means <paramref name="budget"/> holds the fault,
    /// which the caller reports as <c>LIMIT001</c> against the bound it names.
    /// </returns>
    public static StructuredNode? Read(
        string text,
        ResourceLimits limits,
        SourceBudget budget,
        ProfileSource origin,
        DiagnosticPhase phase,
        DiagnosticBuffer diagnostics,
        StableOrderingKey key)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var reader = new Reader(origin, phase, diagnostics, key, budget);

        return reader.Run(text);
    }

    private sealed class Reader(
        ProfileSource origin,
        DiagnosticPhase phase,
        DiagnosticBuffer diagnostics,
        StableOrderingKey key,
        SourceBudget budget)
    {
        private readonly Stack<Frame> frames = new();
        private StructuredNode? root;
        private int documents;
        private long order;

        public StructuredNode? Run(string text)
        {
            var parser = new Parser(new StringReader(text));

            try
            {
                while (parser.MoveNext())
                {
                    if (!Step(parser.Current!))
                    {
                        return null;
                    }
                }
            }
            catch (YamlException error)
            {
                Fail(
                    Explain(error),
                    (int)error.Start.Line,
                    (int)error.Start.Column,
                    "\u00A710.1");
                return null;
            }

            if (root is null)
            {
                // An empty stream reaches here having produced no value at all. Section 10.1 gives
                // "an empty plain scalar" the null payload, but that is a scalar the document
                // contains, not the absence of a document.
                Fail("a YAML document is exactly one value, and this stream holds none.", 1, 1);
            }

            return root;
        }

        private bool Step(ParsingEvent current)
        {
            order++;

            switch (current)
            {
                case StreamStart:
                case StreamEnd:
                case Comment:
                    return true;

                case DocumentStart document:
                    return StartDocument(document);

                case DocumentEnd document:
                    return document.IsImplicit
                        || Reject(
                            "an explicit '...' document marker is not supported: Section 10.3 "
                            + "admits one implicit document per stream.",
                            document.Start,
                            "\u00A710.3");

                case AnchorAlias alias:
                    return Reject(
                        $"the alias '*{alias.Value.Value}' is not supported: Section 10.2 makes "
                        + "anchors and aliases blocking input errors in this version.",
                        alias.Start,
                        "\u00A710.2");

                case Scalar scalar:
                    return ReadScalar(scalar);

                case MappingStart mapping:
                    return Decorated(mapping.Anchor, mapping.Tag, mapping.Start)
                        && Enter(new Frame(mapping.Start, sequence: false));

                case SequenceStart sequence:
                    return Decorated(sequence.Anchor, sequence.Tag, sequence.Start)
                        && Enter(new Frame(sequence.Start, sequence: true));

                case MappingEnd:
                case SequenceEnd:
                    return Leave();

                default:
                    return Reject(
                        $"a '{current.GetType().Name}' event is outside the Section 10.1 subset.",
                        current.Start,
                        "\u00A710.1");
            }
        }

        private bool StartDocument(DocumentStart document)
        {
            // A directive is only legal immediately before an explicit '---', so Section 10.3's
            // marker rule below would refuse this document anyway. The version directive is worth
            // naming because it is the one a user writes deliberately. A '%TAG' directive is not
            // detectable here: YamlDotNet pre-populates Tags with the two default handles on every
            // document, and a '%TAG' may redefine one rather than add a third, so any count test
            // would be a guess. It reaches the marker rule instead, which is accurate.
            if (document.Version is not null)
            {
                return Reject(
                    "a '%YAML' directive is not supported: Section 10.2 does not preserve "
                    + "directives, and Section 10.1 fixes the schema as RestrictedYaml1 "
                    + "regardless of one.",
                    document.Start,
                    "\u00A710.2");
            }

            if (!document.IsImplicit)
            {
                // Section 10.3 rejects the marker itself, so a single '---' document fails here
                // even though it holds one value. A second document reaches this same branch,
                // because YAML requires '---' to begin one.
                return Reject(
                    "an explicit '---' document marker is not supported: Section 10.3 admits one "
                    + "implicit document per stream, so a stream that marks its documents "
                    + "explicitly is refused whether it holds one or several.",
                    document.Start,
                    "\u00A710.3");
            }

            if (++documents > 1)
            {
                // Unreachable while the marker rule above stands, because YAML requires '---' to
                // begin a second document and '...' to end the first, and both are refused there.
                // It is kept so that relaxing either marker rule cannot silently admit a second
                // document, which is why no test can kill it.
                return Reject(
                    "this stream holds more than one YAML document, which Section 10.3 does not "
                    + "support in this version.",
                    document.Start,
                    "\u00A710.3");
            }

            return true;
        }

        private bool ReadScalar(Scalar scalar)
        {
            if (!Decorated(scalar.Anchor, scalar.Tag, scalar.Start))
            {
                return false;
            }

            var frame = frames.Count > 0 ? frames.Peek() : null;

            if (frame is { AwaitingKey: true })
            {
                return ReadKey(scalar, frame);
            }

            return Charge(frames.Count) && Attach(Resolve(scalar), scalar.Start);
        }

        private bool ReadKey(Scalar scalar, Frame frame)
        {
            var line = (int)scalar.Start.Line;
            var column = (int)scalar.Start.Column;

            // Section 10.1: "every plain or quoted scalar mapping key is treated as a string
            // without scalar tag resolution", so a key is never a number, a Boolean, or null. The
            // key '1' and the key 'true' are the strings they are written as.
            if (scalar.Style == ScalarStyle.Plain
                && string.Equals(scalar.Value, "<<", StringComparison.Ordinal))
            {
                // Section 10.1 lists merge keys as unsupported without saying whether that is an
                // error or an ordinary key. Refusing is the safer reading: a merge key silently
                // becoming data is exactly the hidden override Section 9.3 rejects for duplicates.
                // Quoting it says "this is a string" and is accepted.
                return Reject(
                    "the merge key '<<' is not supported: Section 10.1 excludes merge keys from "
                    + "RestrictedYaml1. Quote it as '\"<<\"' to use it as an ordinary key.",
                    scalar.Start,
                    "\u00A710.1");
            }

            if (!frame.Keys.Add(scalar.Value))
            {
                return Reject(
                    $"'{scalar.Value}' appears twice in one YAML mapping, and Section 10.1 makes "
                    + "duplicate mapping keys errors rather than letting parser order decide "
                    + "which one wins.",
                    scalar.Start,
                    "\u00A710.1");
            }

            var lexed = QualifiedNameLexer.LexNativePart(scalar.Value);

            if (lexed.Name is null)
            {
                var fault = lexed.Fault!.Value;

                diagnostics.Add(new BufferedDiagnostic(
                    fault.IsWildcardFault
                        ? DiagnosticCodes.Wildcard001(
                            phase,
                            "\u00A710.4",
                            fault.Message,
                            cardinalityKey: origin.Key(line, column),
                            source: origin.File,
                            line: origin.LineOf(line),
                            column: origin.ColumnOf(column))
                        : DiagnosticCodes.Parse001(
                            phase,
                            "\u00A710.4",
                            fault.Message,
                            cardinalityKey: origin.SourceKey,
                            source: origin.File,
                            line: origin.LineOf(line),
                            column: origin.ColumnOf(column)),
                    key));
                return false;
            }

            frame.Pending = new StructuredProperty(
                scalar.Value, lexed.Name.Parts[0], NullPlaceholder, line, column);
            return true;
        }

        /// <summary>Applies the Section 10.1 <c>RestrictedYaml1</c> schema to one scalar value.</summary>
        /// <param name="scalar">The scalar event.</param>
        /// <remarks>
        /// Only a plain scalar is resolved. Every quoted, literal, or folded scalar is a string by
        /// construction, which is what makes <c>"true"</c> expressible at all.
        /// </remarks>
        private static StructuredScalar Resolve(Scalar scalar)
        {
            var line = (int)scalar.Start.Line;
            var column = (int)scalar.Start.Column;

            if (scalar.Style != ScalarStyle.Plain)
            {
                return StructuredScalar.OfNativeString(scalar.Value, line, column);
            }

            var text = scalar.Value;

            // Section 10.1: "'null', 'Null', 'NULL', '~', or an empty plain scalar are null". The
            // case list is exhaustive here, unlike the Boolean rule immediately below it, which
            // Section 10.1 states "case-insensitively" and illustrates with 'tRuE'. So 'nULL' is a
            // string and 'tRuE' is not.
            if (text.Length == 0
                || string.Equals(text, "~", StringComparison.Ordinal)
                || string.Equals(text, "null", StringComparison.Ordinal)
                || string.Equals(text, "Null", StringComparison.Ordinal)
                || string.Equals(text, "NULL", StringComparison.Ordinal))
            {
                return StructuredScalar.OfTyped(ScalarPayload.Null, line, column);
            }

            if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
            {
                return StructuredScalar.OfTyped(ScalarPayload.OfBoolean(true), line, column);
            }

            if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
            {
                return StructuredScalar.OfTyped(ScalarPayload.OfBoolean(false), line, column);
            }

            return TryNumber(text, line, column, out var number)
                ? number
                : StructuredScalar.OfNativeString(text, line, column);
        }

        /// <summary>Reads a JSON-compatible number, or declines.</summary>
        /// <param name="text">The plain scalar's text.</param>
        /// <param name="line">The scalar's line.</param>
        /// <param name="column">The scalar's column.</param>
        /// <param name="scalar">The typed scalar, when the text is one.</param>
        /// <remarks>
        /// Section 10.1 admits "JSON-compatible decimal integers and floating-point values" and
        /// nothing else, so <c>+1</c>, <c>.5</c> and <c>1.</c> stay strings. The grammar is checked
        /// here rather than delegated, because every general-purpose numeric parse in the framework
        /// accepts at least one of those three. Integer against decimal is the Section 9.1 lexical
        /// rule, so <c>1e0</c> is a decimal here exactly as it is in JSON.
        /// </remarks>
        private static bool TryNumber(
            string text, int line, int column, out StructuredScalar scalar)
        {
            scalar = NullPlaceholder;

            if (!IsJsonNumber(text, out var isDecimal))
            {
                return false;
            }

            if (isDecimal)
            {
                if (!BigDecimal.TryParse(text, out var value))
                {
                    return false;
                }

                scalar = StructuredScalar.OfTyped(ScalarPayload.OfDecimal(value), line, column);
                return true;
            }

            if (!BigInteger.TryParse(
                text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
            {
                return false;
            }

            scalar = StructuredScalar.OfTyped(ScalarPayload.OfInteger(integer), line, column);
            return true;
        }

        /// <summary>Matches the JSON number grammar exactly.</summary>
        /// <param name="text">The candidate text.</param>
        /// <param name="isDecimal">Whether it carries a fraction part or an exponent.</param>
        private static bool IsJsonNumber(string text, out bool isDecimal)
        {
            isDecimal = false;

            var index = 0;

            if (index < text.Length && text[index] == '-')
            {
                index++;
            }

            if (index >= text.Length || !char.IsAsciiDigit(text[index]))
            {
                return false;
            }

            if (text[index] == '0')
            {
                index++;
            }
            else
            {
                while (index < text.Length && char.IsAsciiDigit(text[index]))
                {
                    index++;
                }
            }

            if (index < text.Length && text[index] == '.')
            {
                index++;
                isDecimal = true;

                if (index >= text.Length || !char.IsAsciiDigit(text[index]))
                {
                    return false;
                }

                while (index < text.Length && char.IsAsciiDigit(text[index]))
                {
                    index++;
                }
            }

            if (index < text.Length && (text[index] == 'e' || text[index] == 'E'))
            {
                index++;
                isDecimal = true;

                if (index < text.Length && (text[index] == '+' || text[index] == '-'))
                {
                    index++;
                }

                if (index >= text.Length || !char.IsAsciiDigit(text[index]))
                {
                    return false;
                }

                while (index < text.Length && char.IsAsciiDigit(text[index]))
                {
                    index++;
                }
            }

            return index == text.Length;
        }

        private bool Decorated(AnchorName anchor, TagName tag, Mark at)
        {
            if (!anchor.IsEmpty)
            {
                return Reject(
                    $"the anchor '&{anchor.Value}' is not supported: Section 10.2 makes anchors "
                    + "and aliases blocking input errors in this version.",
                    at,
                    "\u00A710.2");
            }

            // Section 10.2 rejects "every explicit tag token -- including standard '!!' tags,
            // verbatim tags, and local tags -- regardless of whether the tag would preserve the
            // same basic value", and adds that "no tag is accepted implicitly". So '!!str x' is
            // refused even though x is a string either way, and the bare non-specific '!' is
            // refused with them: it is a token the document wrote, not one the parser inferred.
            // An untagged node reports an empty tag rather than a non-specific one, so this
            // refuses only what was written.
            if (!tag.IsEmpty)
            {
                return Reject(
                    $"the tag '{tag.Value}' is not supported: Section 10.2 makes every explicit "
                    + "tag token a blocking input error in this version, regardless of whether it "
                    + "would preserve the same basic value.",
                    at,
                    "\u00A710.2");
            }

            return true;
        }

        private bool Enter(Frame frame)
        {
            if (!Charge(frames.Count))
            {
                return false;
            }

            frames.Push(frame);
            return true;
        }

        /// <summary>Charges one node against Section 23's bounds.</summary>
        /// <param name="depth">The depth of the node, counted from zero at the document root.</param>
        /// <returns><see langword="false"/> when this source crossed <c>--max-depth</c>.</returns>
        /// <remarks>
        /// Every value is charged, scalars included: Section 11.1 has "every element, attribute,
        /// text, comment, and CDATA overlay node" consume <c>--max-nodes</c>, and a YAML scalar is
        /// the text node of that list. Charging containers alone would leave a flat mapping of a
        /// million entries costing one node. <see cref="JsonInputReader"/> charges identically,
        /// which is what makes the bound a property of the document rather than of the format that
        /// spells it.
        /// </remarks>
        private bool Charge(int depth)
        {
            if (depth == 0)
            {
                // The document's own root value is not beneath anything, so it is not a node this
                // source could name and it occupies no depth.
                return true;
            }

            budget.AddNodes(1);
            return budget.TryEnterDepth(depth, order);
        }

        private bool Leave()
        {
            var frame = frames.Pop();

            return Attach(frame.Build(), frame.Start);
        }

        private bool Attach(StructuredNode node, Mark at)
        {
            if (frames.Count == 0)
            {
                if (root is not null)
                {
                    return Reject(
                        "a YAML stream this version supports holds exactly one value, and this "
                        + "one holds a second.",
                        at,
                        "\u00A710.3");
                }

                root = node;
                return true;
            }

            var frame = frames.Peek();

            // Section 10.1: "complex mapping keys are errors". Every value passes through here, so
            // a mapping still awaiting a key at this point was given one that is not a scalar --
            // '? [1, 2]' or '? {a: 1}'. Without this the frame would attach a value to a key that
            // was never read.
            if (frame.AwaitingKey)
            {
                return Reject(
                    "a complex mapping key is not supported: Section 10.1 excludes them from "
                    + "RestrictedYaml1, which requires every mapping key to be a scalar.",
                    at,
                    "\u00A710.1");
            }

            frame.Add(node);
            return true;
        }

        private bool Reject(string message, Mark at, string spec)
        {
            Fail(message, (int)at.Line, (int)at.Column, spec);
            return false;
        }

        private void Fail(string message, int line, int column, string spec = "\u00A710.1") =>
            diagnostics.Add(new BufferedDiagnostic(
                DiagnosticCodes.Parse001(
                    phase,
                    spec,
                    message,
                    cardinalityKey: origin.SourceKey,
                    source: origin.File,
                    line: origin.LineOf(line),
                    column: origin.ColumnOf(column)),
                key));

        /// <summary>Strips the position trailer YamlDotNet appends to its messages.</summary>
        /// <param name="error">The parser's exception.</param>
        /// <remarks>
        /// The trailer repeats the position in the library's own words, beside the Section 22 one
        /// this reader reports. Two spellings of one position invite a reader to trust the wrong
        /// one, so only ours is kept.
        /// </remarks>
        private static string Explain(YamlException error)
        {
            var text = error.Message;
            var trailer = text.IndexOf("(Line: ", StringComparison.Ordinal);

            if (trailer >= 0)
            {
                var close = text.IndexOf("): ", trailer, StringComparison.Ordinal);
                text = close >= 0 ? text[(close + 3)..] : text[..trailer];
            }

            return text.Trim();
        }

        /// <summary>
        /// Stands in for a property's value until its value event arrives, and is never observed.
        /// </summary>
        private static readonly StructuredScalar NullPlaceholder =
            StructuredScalar.OfTyped(ScalarPayload.Null, 1, 1);

        /// <summary>One open collection.</summary>
        private sealed class Frame(Mark start, bool sequence)
        {
            private readonly ImmutableArray<StructuredProperty>.Builder properties =
                ImmutableArray.CreateBuilder<StructuredProperty>();

            private readonly ImmutableArray<StructuredNode>.Builder items =
                ImmutableArray.CreateBuilder<StructuredNode>();

            public Mark Start { get; } = start;

            public HashSet<string> Keys { get; } = new(StringComparer.Ordinal);

            public StructuredProperty? Pending { get; set; }

            /// <summary>Whether the next scalar in this frame names a key rather than a value.</summary>
            public bool AwaitingKey => !sequence && Pending is null;

            public void Add(StructuredNode node)
            {
                if (sequence)
                {
                    items.Add(node);
                    return;
                }

                properties.Add(Pending! with { Value = node });
                Pending = null;
            }

            public StructuredNode Build() =>
                sequence
                    ? new StructuredSequence(
                        items.ToImmutable(), (int)Start.Line, (int)Start.Column)
                    : new StructuredMapping(
                        properties.ToImmutable(), (int)Start.Line, (int)Start.Column);
        }
    }
}
