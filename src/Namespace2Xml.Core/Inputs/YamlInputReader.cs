using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;
using Namespace2Xml.Budgets;
using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using Namespace2Xml.Scalars;
using Namespace2Xml.Text;
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

        var reader = new Reader(origin, phase, diagnostics, key, budget, new SourceLines(text));

        return reader.Run(text);
    }

    private sealed class Reader(
        ProfileSource origin,
        DiagnosticPhase phase,
        DiagnosticBuffer diagnostics,
        StableOrderingKey key,
        SourceBudget budget,
        SourceLines lines)
    {
        private readonly Stack<Frame> frames = new();
        private readonly Stack<long> pendingSlots = new();
        private readonly List<PendingComment> pendingComments = [];
        private readonly List<CommentAnchor> anchors = [];
        private readonly List<CommentEntry> entries = [];
        private readonly List<CommentCollection> collections = [];
        private readonly Dictionary<long, StructuredNode> slotToNode = [];
        private StructuredNode? root;
        private int documents;
        private long order;
        private long slotCounter;
        private long rootSlot;
        private long eventOrdinal;

        public StructuredNode? Run(string text)
        {
            // Comments are scanned rather than skipped. YamlDotNet's convenience constructor
            // passes skipComments: true, which would make Section 11.1's "--max-comments and
            // --max-comment-bytes exactly as other formats do" unenforceable here -- the events
            // never reach Step, so nothing could charge them. Scanning them also lets Section 4.5
            // hold: each event is buffered until the tree is built, then associated to its owning
            // value node.
            var parser = new Parser(new Scanner(new StringReader(text), skipComments: false));

            rootSlot = ++slotCounter;
            pendingSlots.Push(rootSlot);

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
                var (line, column) = At(error.Start);

                Fail(Explain(error), line, column, "\u00A710.1");
                return null;
            }

            if (root is null)
            {
                // An empty stream reaches here having produced no value at all. Section 10.1 gives
                // "an empty plain scalar" the null payload, but that is a scalar the document
                // contains, not the absence of a document.
                Fail("a YAML document is exactly one value, and this stream holds none.", 1, 1);
                return null;
            }

            // Section 4.5's association rules are applied on the finished tree, so a document that
            // was refused above never has comments bound to it -- a partial tree can name nodes the
            // reader would not have accepted as owners.
            return BindComments(root);
        }

        private bool Step(ParsingEvent current)
        {
            order++;
            eventOrdinal++;

            switch (current)
            {
                case StreamStart:
                case StreamEnd:
                    return true;

                // Section 23 has comments consume the corresponding global budget, and Section
                // 11.1 fixes the rule for every format alike. The budget is charged once, on the
                // event; the association pass reuses the buffered token without re-charging, so
                // retaining a comment does not double-count it.
                case Comment comment:
                    budget.AddComments(1, Encoding.UTF8.GetByteCount(comment.Value));
                    pendingComments.Add(new PendingComment(comment.Value, comment.Start, comment.End));
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
                    return ReadMappingStart(mapping);

                case SequenceStart sequence:
                    return ReadSequenceStart(sequence);

                case MappingEnd mappingEnd:
                    return LeaveMapping(mappingEnd);

                case SequenceEnd sequenceEnd:
                    return LeaveSequence(sequenceEnd);

                default:
                    return Reject(
                        $"a '{current.GetType().Name}' event is outside the Section 10.1 subset.",
                        current.Start,
                        "\u00A710.1");
            }
        }

        private bool ReadMappingStart(MappingStart mapping)
        {
            if (!Decorated(mapping.Anchor, mapping.Tag, mapping.Start))
            {
                return false;
            }

            var ownSlot = pendingSlots.Peek();

            RecordAnchor(ownSlot, mapping.Start, CommentAnchorKind.CollectionStart);

            var frame = new Frame(
                mapping.Start,
                sequence: false,
                ownSlot: ownSlot,
                isFlow: mapping.Style == MappingStyle.Flow,
                depth: frames.Count);

            return Enter(frame);
        }

        private bool ReadSequenceStart(SequenceStart sequence)
        {
            if (!Decorated(sequence.Anchor, sequence.Tag, sequence.Start))
            {
                return false;
            }

            var ownSlot = pendingSlots.Peek();

            RecordAnchor(ownSlot, sequence.Start, CommentAnchorKind.CollectionStart);

            var frame = new Frame(
                sequence.Start,
                sequence: true,
                ownSlot: ownSlot,
                isFlow: sequence.Style == SequenceStyle.Flow,
                depth: frames.Count);

            if (!Enter(frame))
            {
                return false;
            }

            // Reserve a slot for the first item so its incoming value's Attach can bind to it.
            // A later SequenceEnd discards the unused pushed slot when no item consumed it.
            pendingSlots.Push(NextSequenceItemSlot(frame));
            return true;
        }

        private bool LeaveMapping(MappingEnd end)
        {
            var frame = frames.Peek();

            if (frame.IsFlow && !frame.IsSequence)
            {
                RecordAnchor(frame.OwnSlot, end.Start, CommentAnchorKind.FlowCollectionEnd);
            }

            frame.End = end.End;
            return Leave();
        }

        private bool LeaveSequence(SequenceEnd end)
        {
            var frame = frames.Peek();

            if (frame.IsFlow && frame.IsSequence)
            {
                RecordAnchor(frame.OwnSlot, end.Start, CommentAnchorKind.FlowCollectionEnd);
            }

            // Drop the pre-reserved "next item" slot that no item consumed.
            if (frame.IsSequence && pendingSlots.Count > 0)
            {
                pendingSlots.Pop();
            }

            frame.End = end.End;
            return Leave();
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

            var slot = pendingSlots.Peek();

            RecordAnchor(slot, scalar.Start, CommentAnchorKind.ScalarStart);

            if (ScalarHasLexicalEnd(scalar))
            {
                RecordAnchor(slot, scalar.End, CommentAnchorKind.ScalarEnd);
            }

            return Charge(frames.Count) && Attach(Resolve(scalar), scalar.Start);
        }

        private bool ReadKey(Scalar scalar, Frame frame)
        {
            var (line, column) = At(scalar.Start);

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

            // Section 4.5: an entry's leading comment binds to "the logical path or sequence item
            // of the immediately following entry", and an inline one to "the entry or item on the
            // same logical line". Both attach on the entry's value node, so a slot is allocated
            // for that value here, before the value's own events arrive, and the key's End is
            // recorded as an inline anchor for it.
            var valueSlot = ++slotCounter;
            pendingSlots.Push(valueSlot);

            RecordAnchor(valueSlot, scalar.End, CommentAnchorKind.KeyEnd);

            var (headerLine, headerColumn) = At(scalar.Start);

            entries.Add(new CommentEntry(
                ValueSlot: valueSlot,
                ContainerSlot: frame.OwnSlot,
                Depth: frames.Count,
                HeaderIndex: scalar.Start.Index,
                HeaderEndIndex: scalar.End.Index,
                HeaderLine: headerLine,
                HeaderColumn: headerColumn,
                IsSequenceItem: false));

            return true;
        }

        /// <summary>Applies the Section 10.1 <c>RestrictedYaml1</c> schema to one scalar value.</summary>
        /// <param name="scalar">The scalar event.</param>
        /// <remarks>
        /// Only a plain scalar is resolved. Every quoted, literal, or folded scalar is a string by
        /// construction, which is what makes <c>"true"</c> expressible at all.
        /// </remarks>
        private StructuredScalar Resolve(Scalar scalar)
        {
            var (line, column) = At(scalar.Start);

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

            var (line, column) = At(frame.Start);

            var (startLine, startColumn) = At(frame.Start);
            var containingSlot = pendingSlots.Count > 0 ? pendingSlots.Peek() : (long?)null;

            // The parent-collection link is recorded before the container node itself is attached,
            // because Attach pops the container's own slot and there is no other place that names
            // the collection this container closes.
            collections.Add(new CommentCollection(
                OwnSlot: frame.OwnSlot,
                ContainingSlot: containingSlot == frame.OwnSlot ? null : containingSlot,
                StartIndex: frame.Start.Index,
                EndIndex: frame.End.Index,
                Depth: frame.Depth,
                IsSequence: frame.IsSequence,
                StartLine: startLine,
                StartColumn: startColumn));

            return Attach(frame.Build(line, column), frame.Start);
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

                // The root slot was pushed before parsing began, so the finished root node claims
                // it here.
                if (pendingSlots.Count > 0)
                {
                    slotToNode[pendingSlots.Pop()] = node;
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

            var attachedSlot = pendingSlots.Pop();

            slotToNode[attachedSlot] = node;

            if (frame.IsSequence)
            {
                var (headerLine, headerColumn) = At(at);
                var effectiveHeaderColumn = frame.IsFlow ? headerColumn : At(frame.Start).Column;

                entries.Add(new CommentEntry(
                    ValueSlot: attachedSlot,
                    ContainerSlot: frame.OwnSlot,
                    Depth: frames.Count,
                    HeaderIndex: at.Index,
                    HeaderEndIndex: at.Index,
                    HeaderLine: headerLine,
                    HeaderColumn: effectiveHeaderColumn,
                    IsSequenceItem: true));

                // Reserve the slot for a possible next sequence item. LeaveSequence discards it if
                // no item consumes it before SequenceEnd arrives.
                pendingSlots.Push(NextSequenceItemSlot(frame));
            }

            frame.Add(node);
            return true;
        }

        /// <summary>The Section 22 position of a YamlDotNet mark.</summary>
        /// <param name="at">The mark to convert.</param>
        /// <remarks>
        /// <para>
        /// Neither of <c>Mark</c>'s two coordinates may be used as reported. <c>Mark.Column</c> is
        /// a UTF-16 code-unit column, so a supplementary scalar earlier on the line has already
        /// advanced it by two where Section 22 advances by one. <c>Mark.Line</c> is counted by a
        /// YAML 1.1 scanner, in which U+0085, U+2028 and U+2029 are line breaks; Section 22 says a
        /// line ends "by LF, CRLF, or a lone CR, and by nothing else", so one of those characters
        /// anywhere in a document makes every later <c>Mark.Line</c> too large -- and makes a
        /// column converted against that line the column of some other line entirely.
        /// </para>
        /// <para>
        /// <c>Mark.Index</c> is neither: it is a raw UTF-16 offset into the decoded text, verified
        /// to survive CRLF, a lone CR and supplementary scalars without normalization. Rebuilding
        /// both coordinates from it makes the reported position independent of how the host counts
        /// lines. Every host position enters through here, so the units cannot be confused at a
        /// call site.
        /// </para>
        /// </remarks>
        private (int Line, int Column) At(Mark at) => lines.PositionOf((int)at.Index);

        private bool Reject(string message, Mark at, string spec)
        {
            var (line, column) = At(at);

            Fail(message, line, column, spec);
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

        private void RecordAnchor(long ownerSlot, Mark position, CommentAnchorKind kind)
        {
            var (line, column) = At(position);
            anchors.Add(new CommentAnchor(ownerSlot, position.Index, line, column, eventOrdinal, kind));
        }

        private long NextSequenceItemSlot(Frame _) => ++slotCounter;

        /// <summary>
        /// Whether a scalar's End mark names a position on its lexical last character's line.
        /// </summary>
        /// <param name="scalar">The scalar event.</param>
        /// <remarks>
        /// A single-line scalar always does. A multi-line block scalar (literal or folded) reports
        /// its End at column one of the following line, which is not on the scalar's own line and
        /// would misclassify a comment written past the block's last content character. The spike's
        /// rule (start line equals end line, or end column beyond one) recovers both single-line
        /// and same-line multiline scalars while excluding the block-scalar case.
        /// </remarks>
        private static bool ScalarHasLexicalEnd(Scalar scalar) =>
            scalar.Start.Line == scalar.End.Line || scalar.End.Column > 1;

        private StructuredNode BindComments(StructuredNode start)
        {
            if (pendingComments.Count == 0)
            {
                return start;
            }

            var bindings = new Dictionary<long, List<StructuredComment>>();

            foreach (var comment in pendingComments)
            {
                var (slot, placement) = AssociateComment(comment);

                if (!bindings.TryGetValue(slot, out var list))
                {
                    list = [];
                    bindings.Add(slot, list);
                }

                list.Add(new StructuredComment(comment.Text, placement));
            }

            var byRef = new Dictionary<StructuredNode, ImmutableArray<StructuredComment>>(
                ReferenceEqualityComparer.Instance);

            foreach (var (slot, list) in bindings)
            {
                if (slotToNode.TryGetValue(slot, out var owner))
                {
                    byRef[owner] = [.. list];
                }
            }

            return Rewrite(start, byRef);
        }

        private (long Slot, CommentPlacement Placement) AssociateComment(PendingComment comment)
        {
            var (commentLine, commentColumn) = At(comment.Start);

            // Section 4.5 "inline comment belongs to the entry or item on the same logical line".
            // "Same logical line" is compared in Section 22 line coordinates, not YamlDotNet's
            // YAML 1.1 line count, so a U+0085/U+2028/U+2029 elsewhere in the source does not
            // silently move an anchor off the comment's line. The rightmost anchor at or before
            // the comment on that line, breaking a tie by the later structural event, is the one
            // Section 4.5 calls the owner.
            CommentAnchor? sameLineAnchor = null;

            foreach (var anchor in anchors)
            {
                if (anchor.Section22Line != commentLine || anchor.Index > comment.Start.Index)
                {
                    continue;
                }

                if (sameLineAnchor is null
                    || anchor.Index > sameLineAnchor.Index
                    || (anchor.Index == sameLineAnchor.Index
                        && anchor.EventOrdinal > sameLineAnchor.EventOrdinal))
                {
                    sameLineAnchor = anchor;
                }
            }

            if (sameLineAnchor is not null)
            {
                return (sameLineAnchor.OwnerSlot, CommentPlacement.Inline);
            }

            if (comment.Start.Index < RootStartIndex())
            {
                // Section 20: "a comment before the first payload or item is document-leading".
                // That classification precedes Section 4.5's binding rule for a leading comment,
                // which Section 20 scopes to "a comment between two payloads or items". A document
                // -leading comment has no value owner, so it neither moves with a re-addressed
                // value nor disappears when the following entry is ignored.
                return (rootSlot, CommentPlacement.Leading);
            }

            var next = FindNextEntry(comment, commentColumn);

            if (next is not null)
            {
                // Section 20: "a comment between two payloads or items becomes a leading comment
                // of the following payload or item", which is Section 4.5's binding rule.
                return (next.ValueSlot, CommentPlacement.Leading);
            }

            var deepest = FindDeepestContaining(comment, commentColumn);

            if (deepest is not null)
            {
                var previous = FindPreviousEntry(deepest, comment, commentColumn);

                if (previous is not null)
                {
                    return (previous.ValueSlot, CommentPlacement.Trailing);
                }
            }

            // Document-trailing: the comment starts after the first content, no entry follows it,
            // and none precedes it at a matching column. Section 20: "a comment after the final
            // payload or item is document-trailing", which Section 4.5 leaves without a value
            // owner; the root is the only slot that survives every re-addressing.
            return (rootSlot, CommentPlacement.Trailing);
        }

        private long RootStartIndex()
        {
            if (anchors.Count == 0)
            {
                return long.MaxValue;
            }

            var min = long.MaxValue;

            foreach (var anchor in anchors)
            {
                if (anchor.Index < min)
                {
                    min = anchor.Index;
                }
            }

            return min;
        }

        private CommentEntry? FindNextEntry(PendingComment comment, int commentColumn)
        {
            CommentEntry? best = null;

            foreach (var entry in entries)
            {
                if (entry.HeaderIndex <= comment.End.Index)
                {
                    continue;
                }

                if (entry.HeaderColumn < commentColumn)
                {
                    continue;
                }

                if (!IsPlausiblyInParent(comment, entry))
                {
                    continue;
                }

                if (best is null
                    || entry.HeaderIndex < best.HeaderIndex
                    || (entry.HeaderIndex == best.HeaderIndex && entry.Depth < best.Depth))
                {
                    best = entry;
                }
            }

            return best;
        }

        private bool IsPlausiblyInParent(PendingComment comment, CommentEntry entry)
        {
            var container = FindCollection(entry.ContainerSlot);

            if (container is null)
            {
                return false;
            }

            if (container.StartIndex <= comment.Start.Index && comment.Start.Index < container.EndIndex)
            {
                return true;
            }

            // The first entry of a container that has not yet emitted its start event: the parent's
            // owner-entry has been read but the container's own header does not exist as a mark
            // yet. Section 4.5 still treats such a comment as "leading" for that first entry, so a
            // between-owner-end-and-first-header comment is considered inside the container.
            var firstInContainer = FirstEntryOf(container.OwnSlot);

            if (!ReferenceEquals(firstInContainer, entry))
            {
                return false;
            }

            var owner = FindEntryByValueSlot(container.OwnSlot);

            if (owner is null)
            {
                // Section 4.5's leading rule is unconditional at the top level too. YamlDotNet
                // places the root implicit mapping's start mark at the first key's position, so a
                // comment written above that key is technically before the container. Whenever
                // that first key is the only entry that could plausibly own the comment, treat it
                // as inside the root container.
                return comment.Start.Index < entry.HeaderIndex;
            }

            return comment.Start.Index > owner.HeaderEndIndex
                && comment.Start.Index < entry.HeaderIndex;
        }

        private CommentCollection? FindDeepestContaining(PendingComment comment, int commentColumn)
        {
            CommentCollection? best = null;

            foreach (var collection in collections)
            {
                if (comment.Start.Index < collection.StartIndex
                    || comment.Start.Index >= collection.EndIndex)
                {
                    continue;
                }

                if (!HasEntryNotDeeperThan(collection, commentColumn))
                {
                    continue;
                }

                if (best is null || collection.Depth > best.Depth)
                {
                    best = collection;
                }
            }

            return best;
        }

        private bool HasEntryNotDeeperThan(CommentCollection collection, int commentColumn)
        {
            foreach (var entry in entries)
            {
                if (entry.ContainerSlot == collection.OwnSlot && entry.HeaderColumn <= commentColumn)
                {
                    return true;
                }
            }

            return false;
        }

        private CommentEntry? FindPreviousEntry(
            CommentCollection collection, PendingComment comment, int commentColumn)
        {
            CommentEntry? best = null;

            foreach (var entry in entries)
            {
                if (entry.ContainerSlot != collection.OwnSlot)
                {
                    continue;
                }

                if (entry.HeaderColumn > commentColumn)
                {
                    continue;
                }

                if (entry.HeaderIndex >= comment.Start.Index)
                {
                    continue;
                }

                if (best is null || entry.HeaderIndex > best.HeaderIndex)
                {
                    best = entry;
                }
            }

            return best;
        }

        private CommentCollection? FindCollection(long slot)
        {
            foreach (var collection in collections)
            {
                if (collection.OwnSlot == slot)
                {
                    return collection;
                }
            }

            return null;
        }

        private CommentEntry? FirstEntryOf(long containerSlot)
        {
            CommentEntry? first = null;

            foreach (var entry in entries)
            {
                if (entry.ContainerSlot != containerSlot)
                {
                    continue;
                }

                if (first is null || entry.HeaderIndex < first.HeaderIndex)
                {
                    first = entry;
                }
            }

            return first;
        }

        private CommentEntry? FindEntryByValueSlot(long valueSlot)
        {
            foreach (var entry in entries)
            {
                if (entry.ValueSlot == valueSlot)
                {
                    return entry;
                }
            }

            return null;
        }

        private static StructuredNode Rewrite(
            StructuredNode node,
            IReadOnlyDictionary<StructuredNode, ImmutableArray<StructuredComment>> bindings)
        {
            var updated = node switch
            {
                StructuredMapping mapping => RewriteMapping(mapping, bindings),
                StructuredSequence sequence => RewriteSequence(sequence, bindings),
                _ => node,
            };

            return bindings.TryGetValue(node, out var comments)
                ? updated with { Comments = updated.Comments.AddRange(comments) }
                : updated;
        }

        private static StructuredMapping RewriteMapping(
            StructuredMapping mapping,
            IReadOnlyDictionary<StructuredNode, ImmutableArray<StructuredComment>> bindings)
        {
            if (mapping.Properties.IsEmpty)
            {
                return mapping;
            }

            var builder = ImmutableArray.CreateBuilder<StructuredProperty>(mapping.Properties.Length);
            var changed = false;

            foreach (var property in mapping.Properties)
            {
                var rewritten = Rewrite(property.Value, bindings);

                if (!ReferenceEquals(rewritten, property.Value))
                {
                    changed = true;
                }

                builder.Add(property with { Value = rewritten });
            }

            return changed ? mapping with { Properties = builder.MoveToImmutable() } : mapping;
        }

        private static StructuredSequence RewriteSequence(
            StructuredSequence sequence,
            IReadOnlyDictionary<StructuredNode, ImmutableArray<StructuredComment>> bindings)
        {
            if (sequence.Items.IsEmpty)
            {
                return sequence;
            }

            var builder = ImmutableArray.CreateBuilder<StructuredNode>(sequence.Items.Length);
            var changed = false;

            foreach (var item in sequence.Items)
            {
                var rewritten = Rewrite(item, bindings);

                if (!ReferenceEquals(rewritten, item))
                {
                    changed = true;
                }

                builder.Add(rewritten);
            }

            return changed ? sequence with { Items = builder.MoveToImmutable() } : sequence;
        }

        /// <summary>One open collection.</summary>
        private sealed class Frame(
            Mark start, bool sequence, long ownSlot, bool isFlow, int depth)
        {
            private readonly ImmutableArray<StructuredProperty>.Builder properties =
                ImmutableArray.CreateBuilder<StructuredProperty>();

            private readonly ImmutableArray<StructuredNode>.Builder items =
                ImmutableArray.CreateBuilder<StructuredNode>();

            public Mark Start { get; } = start;

            public Mark End { get; set; } = start;

            public HashSet<string> Keys { get; } = new(StringComparer.Ordinal);

            public StructuredProperty? Pending { get; set; }

            public long OwnSlot { get; } = ownSlot;

            public bool IsFlow { get; } = isFlow;

            public bool IsSequence => sequence;

            public int Depth { get; } = depth;

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

            public StructuredNode Build(int line, int column) =>
                sequence
                    ? new StructuredSequence(items.ToImmutable(), line, column)
                    : new StructuredMapping(properties.ToImmutable(), line, column);
        }

        private readonly record struct PendingComment(string Text, Mark Start, Mark End);

        private enum CommentAnchorKind
        {
            KeyEnd,
            ScalarStart,
            ScalarEnd,
            CollectionStart,
            FlowCollectionEnd,
        }

        private sealed record CommentAnchor(
            long OwnerSlot,
            long Index,
            int Section22Line,
            int Section22Column,
            long EventOrdinal,
            CommentAnchorKind Kind);

        private sealed record CommentEntry(
            long ValueSlot,
            long ContainerSlot,
            int Depth,
            long HeaderIndex,
            long HeaderEndIndex,
            int HeaderLine,
            int HeaderColumn,
            bool IsSequenceItem);

        private sealed record CommentCollection(
            long OwnSlot,
            long? ContainingSlot,
            long StartIndex,
            long EndIndex,
            int Depth,
            bool IsSequence,
            int StartLine,
            int StartColumn);
    }
}
