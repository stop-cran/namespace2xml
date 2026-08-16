using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Xml;
using Namespace2Xml.Budgets;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using Namespace2Xml.Text;

namespace Namespace2Xml.Inputs;

/// <summary>
/// The Section 11 XML reader: one document element into the Section 4.2 shapes.
/// </summary>
/// <remarks>
/// <para>
/// Section 11.1 fixes the parser's posture before it fixes anything else: DTDs, external entities,
/// network retrieval, and external schema retrieval are all prohibited. The five predefined
/// entities and numeric character references are the whole of what this reader expands, and because
/// a DTD is the only way to introduce another, refusing the DTD refuses the entity-expansion attack
/// outright rather than bounding it.
/// </para>
/// <para>
/// Section 11.1 is explicit that entity expansion "must not impose" a budget of its own, so
/// <see cref="XmlReaderSettings.MaxCharactersFromEntities"/> is left unlimited. A host knob that
/// could become the effective limit would decide an invocation's outcome in place of Section 23's
/// bounds, which is the failure <see cref="JsonInputReader"/> avoids for depth by the same means.
/// </para>
/// <para>
/// The walk keeps its own stack rather than recursing, for the reason Section 7.3 gives in
/// <see cref="JsonInputReader"/>: <c>--max-depth</c> can be raised, and a recursive reader turns a
/// raised bound into a stack overflow instead of a <c>LIMIT001</c>.
/// </para>
/// <para>
/// Section 11.4 evaluates mixedness and repeated-child classification "at concrete merge time
/// across all input contributions", which this preview reduces to one document at a time. See
/// <c>KNOWN-LIMITS.md</c>: a reader that cannot see the other contributions cannot classify against
/// them, and inventing an answer would be worse than declaring the reduction.
/// </para>
/// </remarks>
public static class XmlInputReader
{
    /// <summary>The Section 11.2 XML namespace for namespace declarations, which are not data.</summary>
    private const string XmlnsNamespace = "http://www.w3.org/2000/xmlns/";

    /// <summary>Reads one XML document.</summary>
    /// <param name="text">The decoded document text.</param>
    /// <param name="encoding">The encoding Section 7.4 selected, which Section 11.2 checks.</param>
    /// <param name="options">The Section 16.8 whitespace mode.</param>
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
        SourceEncoding encoding,
        XmlInputOptions options,
        SourceBudget budget,
        ProfileSource origin,
        DiagnosticPhase phase,
        DiagnosticBuffer diagnostics,
        StableOrderingKey key)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var reader = new Reader(
            encoding, options, origin, phase, diagnostics, key, budget, new SourceLines(text));

        return reader.Run(text);
    }

    private sealed class Reader(
        SourceEncoding encoding,
        XmlInputOptions options,
        ProfileSource origin,
        DiagnosticPhase phase,
        DiagnosticBuffer diagnostics,
        StableOrderingKey key,
        SourceBudget budget,
        SourceLines lines)
    {
        private readonly Stack<Frame> frames = new();
        private StructuredNode? root;
        private long order;
        private long elements;
        private long instructions;
        private bool normalized;

        public StructuredNode? Run(string text)
        {
            // Section 11.1: "a DTD is rejected rather than partially processed". Refusing it before
            // the parser starts is the strongest reading of that -- no internal subset is read, so
            // no entity is ever defined, let alone expanded. Prohibit below is the second line.
            if (Prolog.FindDoctype(text) is { } doctype)
            {
                Refuse(
                    "this document declares a DTD, and Section 11.1 prohibits DTDs, external "
                    + "entities, and external resource retrieval. Only the five predefined "
                    + "entities and numeric character references are expanded.",
                    doctype.Line,
                    doctype.Column);
                return null;
            }

            using var reader = XmlReader.Create(new StringReader(text), Settings);
            var position = (IXmlLineInfo)reader;

            try
            {
                while (reader.Read())
                {
                    if (!Step(reader, position))
                    {
                        return null;
                    }
                }
            }
            catch (XmlException error)
            {
                Fail(Explain(error), error.LineNumber, lines.ColumnOf(error.LineNumber, error.LinePosition));
                return null;
            }

            if (root is null)
            {
                // Unreachable while ConformanceLevel.Document stands: a stream with no document
                // element raises "Root element is missing" above. Kept because the alternative to
                // an unreachable diagnostic here is a source that vanishes without one.
                Fail("an XML document is exactly one document element, and this source holds none.", 1, 1);
                return null;
            }

            if (instructions > 0)
            {
                // Section 11.2: processing instructions "are discarded with a summarized warning".
                diagnostics.Add(new BufferedDiagnostic(
                    DiagnosticCodes.Warn006(
                        phase,
                        "\u00A711.2",
                        $"{instructions} processing instruction"
                        + (instructions == 1 ? " was" : "s were")
                        + " discarded: Section 11.8 places them outside this version's "
                        + "preservation contract.",
                        cardinalityKey: origin.SourceKey,
                        source: origin.File),
                    key));
            }

            if (normalized)
            {
                // Section 11.7: enabling NormalizeFormattingWhitespace "weakens the normalized
                // same-format round-trip guarantee and emits one warning per input document when
                // whitespace is discarded". The warning is what carries that weakening to the
                // operator, so it is emitted only when something was actually discarded -- asking
                // for the mode and getting no indentation to drop costs nothing and says nothing.
                diagnostics.Add(new BufferedDiagnostic(
                    DiagnosticCodes.Warn007(
                        phase,
                        "\u00A711.7",
                        "whitespace-only text between element children was discarded as formatting "
                        + "indentation, because 'xmlinputoptions=NormalizeFormattingWhitespace' "
                        + "asked for it. Section 11.7 weakens the normalized same-format "
                        + "round-trip guarantee for this document.",
                        cardinalityKey: origin.SourceKey,
                        source: origin.File),
                    key));
            }

            return root;
        }

        /// <summary>The Section 11.1 parser posture.</summary>
        /// <remarks>
        /// Every member here is a refusal or a preservation guarantee, so none of them is a default
        /// worth inheriting silently. <c>MaxCharactersFromEntities</c> and
        /// <c>MaxCharactersInDocument</c> are zero, which is this API's spelling of "no limit":
        /// Section 11.1 forbids an entity-expansion budget, and Section 23's
        /// <c>--max-input-bytes</c> is the only bound that may end a document.
        /// </remarks>
        private static XmlReaderSettings Settings => new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            ValidationType = ValidationType.None,
            ConformanceLevel = ConformanceLevel.Document,
            IgnoreWhitespace = false,
            IgnoreComments = false,
            IgnoreProcessingInstructions = false,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = 0,
            CheckCharacters = true,
            CloseInput = true,
        };

        private bool Step(XmlReader reader, IXmlLineInfo position)
        {
            order++;

            switch (reader.NodeType)
            {
                case XmlNodeType.XmlDeclaration:
                    return Declaration(reader, position);

                case XmlNodeType.Element:
                    return StartElement(reader, position);

                case XmlNodeType.EndElement:
                    return Close();

                case XmlNodeType.Text:
                case XmlNodeType.Whitespace:
                case XmlNodeType.SignificantWhitespace:
                    return Content(reader.Value, cdata: false, reader.XmlSpace, position);

                case XmlNodeType.CDATA:
                    return Content(reader.Value, cdata: true, reader.XmlSpace, position);

                case XmlNodeType.Comment:
                    return Comment(reader.Value, position);

                case XmlNodeType.ProcessingInstruction:
                    instructions++;
                    return true;

                default:
                    return true;
            }
        }

        /// <summary>Checks Section 11.2's agreement rule for a declared encoding name.</summary>
        /// <param name="reader">The parser, positioned on the XML declaration.</param>
        /// <param name="position">The parser's position reporter.</param>
        /// <remarks>
        /// The declaration itself is not retained -- Section 11.2 says so -- but a name that
        /// disagrees with the encoding Section 7.4 already selected describes a different document
        /// from the one being read, and Section 11.2 makes that blocking rather than advisory.
        ///
        /// The code is <c>PARSE002</c> and not <c>XML002</c>. Section 11.2 says so directly, and
        /// Appendix B assigns "XML declaration encoding inconsistent with decoded input" to
        /// <c>PARSE002</c>, carves that condition out of <c>XML002</c> in the same table -- "other
        /// than byte-encoding disagreement" -- and then states the split a third time: "encoding
        /// disagreement is `PARSE002`, while an otherwise invalid XML declaration is `XML002`".
        /// Section 22 requires the most specific code, and both codes are scoped once per failing
        /// source, so nothing else distinguishes them.
        ///
        /// Returning <c>false</c> stops the parse, which Section 11.2 requires: the disagreement is
        /// diagnosed "before the document is parsed", so a source that is also malformed reports
        /// only this. Every position and name an <c>XmlException</c> could report is derived from
        /// characters the decoder may have produced incorrectly, so a syntax message from a
        /// wrongly-decoded document points the reader at a line that may not be wrong.
        /// </remarks>
        private bool Declaration(XmlReader reader, IXmlLineInfo position)
        {
            var declared = reader.GetAttribute("encoding");

            if (declared is null || Agrees(declared))
            {
                return true;
            }

            diagnostics.Add(new BufferedDiagnostic(
                DiagnosticCodes.Parse002(
                    phase,
                    "\u00A711.2",
                    $"the XML declaration names the encoding '{declared}', but Section 7.4 read "
                    + $"this source as {Describe(encoding)}. Section 11.2 makes a declared encoding "
                    + "that disagrees with the selected one a blocking error.",
                    cardinalityKey: origin.SourceKey,
                    source: origin.File,
                    line: origin.LineOf(position.LineNumber),
                    // The position names the declaration, not the reader's token. Section 22 fixes
                    // how a column is measured but not which construct anchors it, so the corpus
                    // convention decides: the XML001 for a document type declaration reports the
                    // '<' of '<!DOCTYPE', and this reports the '<' of '<?xml' for the same reason.
                    // XmlReader points LinePosition at the target name, two scalars further on.
                    column: origin.ColumnOf(lines.ColumnOf(position.LineNumber, position.LinePosition - 2))),
                key));

            return false;
        }

        /// <summary>Whether a declared encoding name denotes the encoding Section 7.4 selected.</summary>
        /// <param name="declared">The <c>encoding</c> pseudo-attribute, as written.</param>
        /// <remarks>
        /// A byte-order mark distinguishes UTF-16LE from UTF-16BE and the declaration does not have
        /// to, so plain <c>UTF-16</c> agrees with either. Encoding names are case-insensitive.
        /// </remarks>
        private bool Agrees(string declared) => encoding switch
        {
            SourceEncoding.Utf8 => Named(declared, "utf-8"),
            SourceEncoding.Utf16LittleEndian => Named(declared, "utf-16") || Named(declared, "utf-16le"),
            SourceEncoding.Utf16BigEndian => Named(declared, "utf-16") || Named(declared, "utf-16be"),
            _ => false,
        };

        private static bool Named(string declared, string name) =>
            string.Equals(declared, name, StringComparison.OrdinalIgnoreCase);

        private static string Describe(SourceEncoding value) => value switch
        {
            SourceEncoding.Utf8 => "UTF-8",
            SourceEncoding.Utf16LittleEndian => "UTF-16LE",
            SourceEncoding.Utf16BigEndian => "UTF-16BE",
            _ => throw new ArgumentOutOfRangeException(
                nameof(value), value, "Section 7.4 permits three encodings."),
        };

        /// <summary>The Section 22 position of what the parser is looking at.</summary>
        /// <param name="position">The parser's position reporter.</param>
        /// <remarks>
        /// <see cref="IXmlLineInfo.LinePosition"/> is a UTF-16 code-unit column, so a supplementary
        /// scalar earlier on the line has already advanced it by two where Section 22 advances by
        /// one. Every host position enters through here, so the two units cannot be confused at a
        /// call site. Positions this reader computes itself -- the prolog scan, and the fallbacks
        /// at column 1 -- are already Section 22 positions and do not pass through it.
        /// </remarks>
        private (int Line, int Column) At(IXmlLineInfo position) =>
            (position.LineNumber, lines.ColumnOf(position.LineNumber, position.LinePosition));

        private bool StartElement(XmlReader reader, IXmlLineInfo position)
        {
            var depth = frames.Count + 1;
            var (line, column) = At(position);
            var empty = reader.IsEmptyElement;

            if (!Charge(depth))
            {
                return false;
            }

            // Section 11.1 counts namespace declarations against --max-xml-attributes even though
            // Section 11.3 does not project them, so the count charged is the one written.
            if (!budget.TryAddXmlAttributes(reader.AttributeCount, order, ++elements))
            {
                return false;
            }

            var frame = new Frame(Component(reader.NamespaceURI, reader.LocalName), line, column);

            if (!Attributes(reader, position, frame, depth + 1))
            {
                return false;
            }

            frames.Push(frame);

            // An empty element has no end tag, so nothing else will close it.
            return !empty || Close();
        }

        private bool Attributes(
            XmlReader reader, IXmlLineInfo position, Frame frame, int depth)
        {
            if (!reader.MoveToFirstAttribute())
            {
                return true;
            }

            do
            {
                // Section 11.8 places "exact namespace declaration placement" outside the
                // preservation contract, and Section 11.3 projects a prefixed name as its URI. A
                // declaration is therefore spent rather than stored: keeping it would give the
                // model two disagreeing spellings of one namespace binding.
                if (string.Equals(reader.NamespaceURI, XmlnsNamespace, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!Charge(depth))
                {
                    return false;
                }

                var name = new AttributePart(Component(reader.NamespaceURI, reader.LocalName));
                var at = At(position);

                frame.AddAttribute(new StructuredProperty(
                    Spell(name),
                    name,
                    StructuredScalar.OfNativeString(reader.Value, at.Line, at.Column),
                    at.Line,
                    at.Column));
            }
            while (reader.MoveToNextAttribute());

            reader.MoveToElement();
            return true;
        }

        private bool Content(string text, bool cdata, XmlSpace space, IXmlLineInfo position)
        {
            if (frames.Count == 0)
            {
                // Whitespace outside the document element is not content of anything.
                return true;
            }

            var frame = frames.Peek();

            // Section 11.6 coalesces adjacent text and adjacent CDATA separately, and never with
            // each other. An extended run is one node that was already charged.
            if (frame.TryExtend(text, cdata))
            {
                return true;
            }

            if (!Charge(frames.Count + 1))
            {
                return false;
            }

            var at = At(position);

            frame.OpenText(text, cdata, space == XmlSpace.Preserve, at.Line, at.Column);
            return true;
        }

        /// <summary>Retains a comment as a Section 11.5 ordered content node.</summary>
        /// <param name="value">The comment's text, as the parser decoded it.</param>
        /// <param name="at">The parser's position reporter.</param>
        /// <remarks>
        /// Section 11.5 keeps comments "as ordered comment nodes", and explicitly does not force
        /// them "into a 'leading comment for the next value' representation because a comment may
        /// occur between mixed-content nodes or after the final child". A comment therefore takes a
        /// Section 11.4 content-token ordering value like any other content node — that is why
        /// <c>b</c> is <c>a.#2</c> in <c>&lt;a&gt;t&lt;!--c--&gt;&lt;b/&gt;&lt;/a&gt;</c> — and
        /// never becomes a <see cref="BoundComment"/>.
        /// </remarks>
        private bool Comment(string value, IXmlLineInfo at)
        {
            // Section 11.1: decoded character data "still consumes --max-nodes, --max-comments,
            // and --max-comment-bytes exactly as other formats do", and Section 23 lists comments
            // among the things that consume the corresponding global budget. The bound counts
            // comments read, not comments retained, so a prolog or trailing comment -- which
            // produces no overlay node -- is charged here too, before the node check below.
            budget.AddComments(1, Encoding.UTF8.GetByteCount(value));

            if (frames.Count == 0)
            {
                return true;
            }

            // Section 11.1 has "every element, attribute, text, comment, and CDATA overlay node"
            // consume --max-nodes.
            if (!Charge(frames.Count + 1))
            {
                return false;
            }

            frames.Peek().AddComment(value, at.LineNumber, at.LinePosition);
            return true;
        }

        /// <summary>Charges one node against Section 23's bounds.</summary>
        /// <param name="depth">
        /// The depth of the node. Section 11.1 counts the document element as depth 1, unlike the
        /// JSON and YAML roots, which are not nodes their source could name.
        /// </param>
        /// <returns><see langword="false"/> when this source crossed <c>--max-depth</c>.</returns>
        private bool Charge(int depth)
        {
            budget.AddNodes(1);
            return budget.TryEnterDepth(depth, order);
        }

        private bool Close()
        {
            var frame = frames.Pop();

            if (options.NormalizesWhitespace() && frame.Normalize())
            {
                normalized = true;
            }

            var node = frame.Build();

            if (frames.Count == 0)
            {
                // Section 11.3 addresses the document element by name: '<a>text</a>' projects
                // 'a.#0', not '#0'. The document therefore contributes a mapping holding it.
                root = new StructuredMapping(
                    [Property(frame.Name, node, frame.Line, frame.Column)], frame.Line, frame.Column);
                return true;
            }

            frames.Peek().AddElement(frame.Name, node, frame.Line, frame.Column);
            return true;
        }

        private void Refuse(string message, int line, int column) =>
            diagnostics.Add(new BufferedDiagnostic(
                DiagnosticCodes.Xml001(
                    phase,
                    "\u00A711.1",
                    message,
                    cardinalityKey: origin.SourceKey,
                    source: origin.File,
                    line: origin.LineOf(line),
                    column: origin.ColumnOf(column)),
                key));

        private void Fail(string message, int line, int column) =>
            diagnostics.Add(new BufferedDiagnostic(
                DiagnosticCodes.Parse001(
                    phase,
                    "\u00A711.2",
                    message,
                    cardinalityKey: origin.SourceKey,
                    source: origin.File,
                    // A DTD refusal and a missing root element are both reported at 0:0, which is
                    // not a Section 22 position. Section 22 counts from one.
                    line: origin.LineOf(Math.Max(line, 1)),
                    column: origin.ColumnOf(Math.Max(column, 1))),
                key));

        private static StructuredProperty Property(
            NamePart name, StructuredNode value, int line, int column) =>
            new(Spell(name), name, value, line, column);

        private static XmlNameComponent Component(string uri, string local) =>
            uri.Length == 0
                ? new OrdinaryPart([new LiteralToken(local)])
                : new QualifiedElementPart(uri, [new LiteralToken(local)]);

        /// <summary>
        /// Removes the parts of an <see cref="XmlException"/> message that belong to the host
        /// rather than to this tool: the position trailer, and advice naming an API knob.
        /// </summary>
        /// <param name="error">The parser's exception.</param>
        /// <remarks>
        /// <para>
        /// The trailer repeats the position in the library's own words, beside the Section 22 one
        /// this reader reports. Two spellings of one position invite a reader to trust the wrong
        /// one, so only ours is kept. A message whose tail is not exactly the trailer -- "Root
        /// element is missing." has none -- is left alone.
        /// </para>
        /// <para>
        /// The DTD refusal carries a second sentence telling the reader to set
        /// <c>XmlReaderSettings.DtdProcessing</c>. Nobody running this tool has an
        /// <c>XmlReaderSettings</c>, and Section 11.1 prohibits DTDs outright rather than behind a
        /// setting, so the advice describes a remedy that does not exist and would be actively
        /// misleading if followed. <c>JsonInputReader.Explain</c> drops " Change the reader
        /// options." for the same reason.
        /// </para>
        /// </remarks>
        private static string Explain(XmlException error)
        {
            const string Opening = " Line ";
            const string Separator = ", position ";
            const string Advice =
                " To enable DTD processing set the DtdProcessing property on XmlReaderSettings to "
                + "Parse and pass the settings into XmlReader.Create method.";

            var text = error.Message.Replace(Advice, string.Empty, StringComparison.Ordinal);
            var trailer = text.LastIndexOf(Opening, StringComparison.Ordinal);

            if (trailer < 0 || !text.EndsWith('.'))
            {
                return text.TrimEnd();
            }

            var tail = text.AsSpan(trailer + Opening.Length, text.Length - trailer - Opening.Length - 1);
            var separator = tail.IndexOf(Separator, StringComparison.Ordinal);

            if (separator < 0
                || !Counted(tail[..separator])
                || !Counted(tail[(separator + Separator.Length)..]))
            {
                return text.TrimEnd();
            }

            return text[..trailer].TrimEnd();
        }

        private static bool Counted(ReadOnlySpan<char> text) =>
            long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out _);

        /// <summary>Writes a name the way Appendix A.2 spells it, for diagnostics.</summary>
        /// <param name="part">The name.</param>
        private static string Spell(NamePart part) => part switch
        {
            OrdinaryPart ordinary => Literal(ordinary.Tokens),
            QualifiedElementPart qualified => $"Q{{{qualified.Uri}}}{Literal(qualified.Local)}",
            AttributePart attribute => "@" + Spell(attribute.Name),
            ContentPart content => "#" + content.Ordinal.ToString(CultureInfo.InvariantCulture),
            _ => throw new ArgumentOutOfRangeException(
                nameof(part), part, $"'{part.GetType().Name}' is not a {nameof(NamePart)}."),
        };

        private static string Literal(ImmutableArray<NameToken> tokens) =>
            tokens is [LiteralToken single] ? single.Text : string.Concat(tokens);

        /// <summary>One open element.</summary>
        /// <param name="name">Its Section 11.4 name component.</param>
        /// <param name="line">The one-based line its start tag begins on.</param>
        /// <param name="column">The one-based Section 22 column its start tag begins at.</param>
        private sealed class Frame(XmlNameComponent name, int line, int column)
        {
            private readonly ImmutableArray<StructuredProperty>.Builder attributes =
                ImmutableArray.CreateBuilder<StructuredProperty>();

            private readonly List<Token> tokens = [];
            private long next;

            public XmlNameComponent Name { get; } = name;

            public int Line { get; } = line;

            public int Column { get; } = column;

            public void AddAttribute(StructuredProperty attribute) => attributes.Add(attribute);

            public void AddElement(XmlNameComponent child, StructuredNode node, int at, int column) =>
                tokens.Add(new ElementToken(next++, at, column, child, node));

            /// <summary>Appends to the open run when Section 11.6 makes this text part of it.</summary>
            /// <param name="text">The decoded text.</param>
            /// <param name="cdata">Whether it arrived as CDATA.</param>
            public bool TryExtend(string text, bool cdata)
            {
                if (tokens.Count == 0 || tokens[^1] is not TextToken run || run.IsCData != cdata)
                {
                    return false;
                }

                run.Text.Append(text);
                return true;
            }

            public void OpenText(string text, bool cdata, bool preserve, int at, int column) =>
                tokens.Add(new TextToken(next++, at, column, cdata, preserve, text));

            /// <summary>Records a Section 11.5 comment at its own content-token slot.</summary>
            /// <param name="text">The comment's text, as the parser decoded it.</param>
            /// <param name="at">The one-based line it begins on.</param>
            /// <param name="column">The one-based Section 22 column it begins at.</param>
            /// <remarks>
            /// Taking an ordering value also ends any open text run, which is what stops the two
            /// halves of <c>&lt;a&gt;t1&lt;!--c--&gt;t2&lt;/a&gt;</c> from coalescing into the one
            /// text node Section 11.4 would then expose at the element path.
            /// </remarks>
            public void AddComment(string text, int at, int column) =>
                tokens.Add(new CommentToken(next++, at, column, text));

            /// <summary>
            /// Discards the Section 11.7 formatting whitespace this element holds, if any.
            /// </summary>
            /// <returns>Whether anything was discarded.</returns>
            /// <remarks>
            /// <para>
            /// Section 11.7 discards "whitespace-only text between element children" and preserves
            /// "whitespace in mixed content", so the two conditions are read together: an element
            /// with element children whose every text run is whitespace was indented by a writer,
            /// and an element holding one non-whitespace run is mixed content whose whitespace is
            /// part of what it says. That makes the decision a property of the whole element and
            /// not of an individual run, which is why it is taken here, at close, rather than as
            /// each run arrives.
            /// </para>
            /// <para>
            /// Whitespace "under <c>xml:space="preserve"</c> is preserved" regardless, and CDATA is
            /// never formatting: an indenting writer emits a text node, so a whitespace-only CDATA
            /// section is something a person wrote on purpose. An element with no element children
            /// is left alone in full, because its whitespace is not "between element children" —
            /// <c>&lt;a&gt; &lt;/a&gt;</c> is a scalar whose value is a space.
            /// </para>
            /// <para>
            /// The ordering values the discarded runs spent are <em>not</em> reclaimed. Section
            /// 11.4 assigns them "across all child elements, text, CDATA, and comments", and
            /// renumbering the surviving children would move them relative to the addresses another
            /// contribution to the same element already used.
            /// </para>
            /// </remarks>
            public bool Normalize()
            {
                var texts = tokens.OfType<TextToken>().ToList();

                if (texts.Count == 0
                    || !tokens.OfType<ElementToken>().Any()
                    || texts.Any(text =>
                        text.IsCData
                        || text.Preserve
                        || !string.IsNullOrWhiteSpace(text.Text.ToString())))
                {
                    return false;
                }

                tokens.RemoveAll(token => token is TextToken);
                return true;
            }

            public StructuredNode Build()
            {
                var texts = tokens.OfType<TextToken>().ToList();
                var children = tokens.OfType<ElementToken>().ToList();
                var comments = tokens.OfType<CommentToken>().ToList();

                // Section 11.4: "an element with no child elements and exactly one non-comment text
                // or CDATA node also exposes that scalar at the element path". "Non-comment" is
                // load-bearing -- a comment beside the text does not stop the element being that
                // scalar, so the classification counts text runs and elements and ignores comments.
                if (children.Count == 0 && texts.Count <= 1 && comments.Count == 0)
                {
                    return Simple(texts);
                }

                var properties = ImmutableArray.CreateBuilder<StructuredProperty>();
                properties.AddRange(attributes);

                if (texts.Count == 0)
                {
                    // Section 17.4: "comments alone do not make a parent mixed-content", so an
                    // element whose only non-element content is a comment keeps element-only
                    // addressing for its children and places the comment at its own '#n' beside
                    // them.
                    ElementOnly(children, properties);
                    Comments(comments, properties);
                }
                else if (children.Count == 0 && texts.Count == 1)
                {
                    // One text run and a comment: Section 11.4 still exposes the scalar at the
                    // element path, and the comment still occupies the content token it took. The
                    // run's own token rides on the exposed scalar so Section 19.5 can place the
                    // comment on the side of the value it was written on.
                    Comments(comments, properties);

                    return new StructuredMapping(properties.ToImmutable(), Line, Column)
                    {
                        Scalar = Scalar(texts[0]) with { ContentToken = texts[0].Ordinal },
                    };
                }
                else
                {
                    Mixed(properties);
                }

                return new StructuredMapping(properties.ToImmutable(), Line, Column);
            }

            /// <summary>Places retained comments at their Section 11.4 content tokens.</summary>
            /// <param name="comments">The comments this element holds, in document order.</param>
            /// <param name="properties">The property list being built.</param>
            private static void Comments(
                List<CommentToken> comments,
                ImmutableArray<StructuredProperty>.Builder properties)
            {
                foreach (var comment in comments)
                {
                    properties.Add(Property(
                        new ContentPart(comment.Ordinal),
                        StructuredScalar.OfTyped(
                            ScalarPayload.OfString(comment.Text).AsComment(),
                            comment.Line,
                            comment.Column) with
                        {
                            ContentToken = comment.Ordinal,
                        },
                        comment.Line,
                        comment.Column));
                }
            }

            /// <summary>The scalar one text run contributes, under its Section 11.6 spelling.</summary>
            /// <param name="text">The run.</param>
            private static StructuredScalar Scalar(TextToken text) =>
                StructuredScalar.OfNativeString(
                    text.Text.ToString(), text.Line, text.Column) with
                {
                    IsCdata = text.IsCData,
                };

            /// <summary>Builds an element with no element children and at most one text run.</summary>
            /// <param name="texts">Its text runs.</param>
            /// <remarks>
            /// Section 11.4: "an element with no child elements and exactly one non-comment text or
            /// CDATA node also exposes that scalar at the element path". With no attributes that is
            /// the whole node, so <c>&lt;b&gt;two&lt;/b&gt;</c> is a scalar rather than a mapping
            /// wrapping one. An element with neither is Section 4.4's explicit mapping presence.
            /// </remarks>
            private StructuredNode Simple(List<TextToken> texts)
            {
                var scalar = texts.Count == 1 ? Scalar(texts[0]) : null;

                if (attributes.Count == 0)
                {
                    return scalar ?? (StructuredNode)new StructuredMapping([], Line, Column);
                }

                return new StructuredMapping(attributes.ToImmutable(), Line, Column)
                {
                    Scalar = scalar,
                };
            }

            /// <summary>Addresses element-only children by name, per Section 11.4.</summary>
            /// <param name="children">The child elements, in document order.</param>
            /// <param name="properties">The property list being built.</param>
            /// <remarks>
            /// <para>
            /// Repeated same-name children "form a sequence at <c>parent.child</c>", which is what
            /// makes <c>&lt;a&gt;&lt;b/&gt;&lt;b/&gt;&lt;/a&gt;</c> address as <c>a.b.0</c> and
            /// <c>a.b.1</c> while a lone <c>&lt;b&gt;</c> stays <c>a.b</c>.
            /// </para>
            /// <para>
            /// A repeat is therefore recorded once, at its first appearance, and the property order
            /// alone is first-appearance order rather than document order. Section 11.4 covers the
            /// difference by having every element-only child carry a content-token ordering value
            /// as well as its name, which is what tells a serializer that the second <c>b</c> of
            /// <c>&lt;a&gt;&lt;b/&gt;&lt;c/&gt;&lt;b/&gt;&lt;/a&gt;</c> follows <c>c</c>. Each
            /// occurrence carries its own, because the sequence has one position among its siblings
            /// and its items do not.
            /// </para>
            /// </remarks>
            private static void ElementOnly(
                List<ElementToken> children,
                ImmutableArray<StructuredProperty>.Builder properties)
            {
                var groups = new Dictionary<XmlNameComponent, List<ElementToken>>();
                var appearance = new List<XmlNameComponent>();

                foreach (var child in children)
                {
                    if (!groups.TryGetValue(child.Name, out var group))
                    {
                        group = [];
                        groups[child.Name] = group;
                        appearance.Add(child.Name);
                    }

                    group.Add(child);
                }

                foreach (var name in appearance)
                {
                    var group = groups[name];
                    var first = group[0];

                    StructuredNode value = group.Count == 1
                        ? Placed(first)
                        : new StructuredSequence(
                            [.. group.Select(Placed)], first.Line, first.Column);

                    properties.Add(Property(name, value, first.Line, first.Column));
                }
            }

            /// <summary>One element-only child, carrying its Section 11.4 content token.</summary>
            /// <param name="token">The child.</param>
            private static StructuredNode Placed(ElementToken token) =>
                token.Node with { ContentToken = token.Ordinal };

            /// <summary>Wraps every content node in its Section 11.3 <c>#n</c> address.</summary>
            /// <param name="properties">The property list being built.</param>
            /// <remarks>
            /// A child element becomes <c>#n.name</c> rather than <c>#n</c>, which is what keeps
            /// <c>a.#1.Q{urn:p}b.@x</c> distinguishable from a text node at the same position and
            /// is why "attribute and child-element names never collide".
            /// </remarks>
            private void Mixed(ImmutableArray<StructuredProperty>.Builder properties)
            {
                foreach (var token in tokens)
                {
                    switch (token)
                    {
                        case TextToken text:
                            properties.Add(Property(
                                new ContentPart(text.Ordinal),
                                Scalar(text) with { ContentToken = text.Ordinal },
                                text.Line,
                                text.Column));
                            break;

                        case ElementToken child:
                            properties.Add(Property(
                                new ContentPart(child.Ordinal),
                                new StructuredMapping(
                                    [Property(child.Name, child.Node, child.Line, child.Column)],
                                    child.Line,
                                    child.Column)
                                {
                                    ContentToken = child.Ordinal,
                                },
                                child.Line,
                                child.Column));
                            break;

                        case CommentToken comment:
                            Comments([comment], properties);
                            break;

                        default:
                            break;
                    }
                }
            }
        }

        /// <summary>One of an element's Section 11.4 content tokens.</summary>
        /// <param name="Ordinal">Its content-token ordering value within its parent.</param>
        private abstract record Token(long Ordinal);

        /// <summary>A run of adjacent text or of adjacent CDATA, coalesced per Section 11.6.</summary>
        /// <param name="Ordinal">Its content-token ordering value within its parent.</param>
        /// <param name="Line">The one-based line the run begins on.</param>
        /// <param name="Column">The one-based Section 22 column the run begins at.</param>
        /// <param name="IsCData">Whether the run is CDATA rather than ordinary text.</param>
        /// <param name="Preserve">Whether Section 11.7's <c>xml:space="preserve"</c> covers it.</param>
        /// <param name="Initial">Its first segment, which later adjacent ones are appended to.</param>
        private sealed record TextToken(
            long Ordinal,
            int Line,
            int Column,
            bool IsCData,
            bool Preserve,
            string Initial) : Token(Ordinal)
        {
            public StringBuilder Text { get; } = new(Initial);
        }

        /// <summary>A child element.</summary>
        /// <param name="Ordinal">Its content-token ordering value within its parent.</param>
        /// <param name="Line">The one-based line its start tag begins on.</param>
        /// <param name="Column">The one-based Section 22 column its start tag begins at.</param>
        /// <param name="Name">Its Section 11.4 name component.</param>
        /// <param name="Node">The node it built.</param>
        private sealed record ElementToken(
            long Ordinal,
            int Line,
            int Column,
            XmlNameComponent Name,
            StructuredNode Node) : Token(Ordinal);

        /// <summary>A Section 11.5 comment, retained as an ordered content node.</summary>
        /// <param name="Ordinal">Its content-token ordering value within its parent.</param>
        /// <param name="Line">The one-based line it begins on.</param>
        /// <param name="Column">The one-based Section 22 column it begins at.</param>
        /// <param name="Text">Its text, as the parser decoded it.</param>
        private sealed record CommentToken(
            long Ordinal,
            int Line,
            int Column,
            string Text) : Token(Ordinal);
    }

    /// <summary>Scans the XML prolog, which is the only place a DTD may appear.</summary>
    /// <remarks>
    /// Before the document element an XML document may hold only whitespace, comments, processing
    /// instructions, and one document type declaration. That grammar is small enough to scan
    /// exactly, which is what lets Section 11.1's refusal happen before a parser is constructed. A
    /// prolog this scanner cannot make sense of is left to the parser, whose error is the better
    /// one.
    /// </remarks>
    private static class Prolog
    {
        /// <summary>Finds the document type declaration.</summary>
        /// <param name="text">The decoded document text.</param>
        /// <returns>Its one-based position, or <see langword="null"/> when there is none.</returns>
        public static (int Line, int Column)? FindDoctype(string text)
        {
            var index = 0;
            var line = 1;
            var column = 1;

            while (index < text.Length)
            {
                var current = text[index];

                if (current == '\n')
                {
                    index++;
                    line++;
                    column = 1;
                    continue;
                }

                if (current == '\r')
                {
                    index += index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;
                    line++;
                    column = 1;
                    continue;
                }

                if (current is ' ' or '\t')
                {
                    index++;
                    column++;
                    continue;
                }

                if (Matches(text, index, "<!DOCTYPE"))
                {
                    return (line, column);
                }

                var close =
                    Matches(text, index, "<!--") ? "-->"
                    : Matches(text, index, "<?") ? "?>"
                    : null;

                if (close is null)
                {
                    return null;
                }

                var end = text.IndexOf(close, index, StringComparison.Ordinal);

                if (end < 0)
                {
                    return null;
                }

                // The skipped span may hold line breaks, so it is walked rather than jumped -- and
                // under the same Section 22 rules the loop above uses, or this function disagrees
                // with itself about where line 2 starts. A lone CR ends a line, and a scalar
                // outside the Basic Multilingual Plane occupies one column although it is stored
                // as two UTF-16 code units.
                var stop = end + close.Length;

                for (var scan = index; scan < stop;)
                {
                    var skipped = text[scan];

                    if (skipped == '\n')
                    {
                        scan++;
                        line++;
                        column = 1;
                    }
                    else if (skipped == '\r')
                    {
                        scan += scan + 1 < text.Length && text[scan + 1] == '\n' ? 2 : 1;
                        line++;
                        column = 1;
                    }
                    else
                    {
                        scan += char.IsSurrogatePair(text, scan) ? 2 : 1;
                        column++;
                    }
                }

                index = stop;
            }

            return null;
        }

        private static bool Matches(string text, int index, string token) =>
            text.AsSpan(index).StartsWith(token, StringComparison.Ordinal);
    }
}
