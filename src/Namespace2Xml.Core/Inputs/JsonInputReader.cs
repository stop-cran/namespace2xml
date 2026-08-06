using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Namespace2Xml.Budgets;
using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using Namespace2Xml.Scalars;

namespace Namespace2Xml.Inputs;

/// <summary>
/// The Section 9 JSON reader: one document into the Section 4.2 shapes.
/// </summary>
/// <remarks>
/// <para>
/// Numbers are read from their source lexeme rather than through a binary floating-point type.
/// Section 9.1 admits "arbitrary-precision JSON numbers" and decides integer against decimal on
/// "whether the lexical form contains a fraction part or exponent", so <c>1e0</c> is a decimal and
/// <c>10000000000000000000000000</c> is an integer that no <see cref="double"/> can hold. Reading a
/// number as a <see cref="double"/> would answer both questions wrongly and silently.
/// </para>
/// <para>
/// The walk keeps its own stack rather than recursing. Section 25 lets <c>--max-depth</c> be raised,
/// and a recursive reader turns a raised bound into a stack overflow, which is a crash rather than
/// the <c>LIMIT001</c> Section 7.3 asks for. For the same reason the depth this charges is its own
/// source's nesting and nothing else: Section 7.3 forbids a parser from observing a global total.
/// </para>
/// </remarks>
public static class JsonInputReader
{
    /// <summary>Reads one JSON document.</summary>
    /// <param name="text">The decoded document text.</param>
    /// <param name="limits">The Section 25 bounds.</param>
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

        var positions = new PositionMap(text);

        if (!text.AsSpan().ContainsAnyExcept(JsonWhitespace))
        {
            // The runtime's own message for this names isFinalBlock, an internal reader concept.
            // A document holding no value is worth saying plainly. JSON's whitespace set is the
            // one tested here, so a document of non-breaking spaces is still a parse error rather
            // than an empty one.
            Fail(
                "a JSON document is exactly one value, and this one holds none.",
                1,
                1,
                origin,
                phase,
                diagnostics,
                key);
            return null;
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(
            bytes,
            new JsonReaderOptions
            {
                // Section 9.2: comments and trailing commas are errors, not options.
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = DepthCeiling(limits),
            });

        var frames = new Stack<Frame>();
        StructuredNode? root = null;
        var order = 0L;

        try
        {
            while (reader.Read())
            {
                var (line, column) = positions.Locate(reader.TokenStartIndex);
                order++;

                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        if (!ReadPropertyName(
                            reader.GetString()!, frames.Peek(), line, column,
                            origin, phase, diagnostics, key))
                        {
                            return null;
                        }

                        break;

                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        if (!Charge(frames.Count, order, budget))
                        {
                            return null;
                        }

                        frames.Push(new Frame(
                            reader.TokenType == JsonTokenType.StartObject, line, column));
                        break;

                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        var closed = frames.Pop();

                        if (!Attach(closed.ToNode(), frames, ref root, origin, phase, diagnostics, key))
                        {
                            return null;
                        }

                        break;

                    default:
                        if (!TryReadScalar(ref reader, line, column, origin, phase, diagnostics, key, out var scalar)
                            || !Charge(frames.Count, order, budget)
                            || !Attach(scalar!, frames, ref root, origin, phase, diagnostics, key))
                        {
                            return null;
                        }

                        break;
                }
            }
        }
        catch (JsonException error)
        {
            var (line, column) = positions.Locate(error);

            Fail(
                Explain(error) + " Section 9.2 accepts no nonstandard JSON extension.",
                line,
                column,
                origin,
                phase,
                diagnostics,
                key);
            return null;
        }

        if (root is null)
        {
            Fail(
                "a JSON document is exactly one value, and this one holds none.",
                1,
                1,
                origin,
                phase,
                diagnostics,
                key);
            return null;
        }

        return root;
    }

    /// <summary>RFC 8259 whitespace: space, horizontal tab, line feed, carriage return.</summary>
    private static readonly System.Buffers.SearchValues<char> JsonWhitespace =
        System.Buffers.SearchValues.Create(" \t\n\r");

    /// <summary>
    /// The reportable part of a <see cref="JsonException"/>'s prose.
    /// </summary>
    /// <param name="error">The exception.</param>
    /// <returns>The message with the runtime's own trailer removed.</returns>
    /// <remarks>
    /// <para>
    /// The runtime appends <c>LineNumber</c> and <c>BytePositionInLine</c> to every message. Both
    /// are zero-based, and both restate a position this diagnostic already carries structurally in
    /// Section 22's one-based form, so leaving them in prints the same place twice in two
    /// conventions and invites the reader to trust the wrong one.
    /// </para>
    /// <para>
    /// It also advises changing "the reader options", which names a <c>System.Text.Json</c> API no
    /// user of this tool can reach. Section 9.2 fixes the answer instead: the extension is not
    /// accepted, and no option turns it on.
    /// </para>
    /// </remarks>
    private static string Explain(JsonException error)
    {
        var text = error.Message;
        var trailer = text.IndexOf(" LineNumber:", StringComparison.Ordinal);

        if (trailer >= 0)
        {
            text = text[..trailer];
        }

        return text.Replace(" Change the reader options.", string.Empty, StringComparison.Ordinal)
            .Replace(", when isFinalBlock is true", string.Empty, StringComparison.Ordinal)
            .TrimEnd();
    }

    /// <summary>
    /// The depth at which <see cref="Utf8JsonReader"/> gives up, set so it never decides anything.
    /// </summary>
    /// <remarks>
    /// The same reasoning Section 11.1 applies to XML entity expansion applies here: a host knob
    /// that could become the effective limit would make the answer depend on the host rather than on
    /// <c>--max-depth</c>. Two levels of headroom are needed, not one. This reader charges a
    /// container the depth of the path that names it, so the root value — which no path names — is
    /// depth 0 while <see cref="Utf8JsonReader"/> already counts it as one level. Refusing at
    /// <c>--max-depth</c> plus one therefore happens while the host is at <c>--max-depth</c> plus
    /// two, and a ceiling of plus one would have the host throw <c>PARSE001</c> on the very token
    /// this reader means to answer with <c>LIMIT001</c>.
    /// </remarks>
    private static int DepthCeiling(ResourceLimits limits) =>
        limits.MaxDepth >= int.MaxValue - 2 ? int.MaxValue : (int)Math.Max(1, limits.MaxDepth + 2);

    private static bool Charge(int depth, long order, SourceBudget budget)
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

    private static bool ReadPropertyName(
        string name,
        Frame frame,
        int line,
        int column,
        ProfileSource origin,
        DiagnosticPhase phase,
        DiagnosticBuffer diagnostics,
        StableOrderingKey key)
    {
        // Section 9.3: "Standard mode rejects duplicate keys within one JSON object", and the
        // comparison is on the decoded name, so "a" and "\u0061" are the same key.
        if (!frame.Keys.Add(name))
        {
            // Section 9.3 is the dedicated clause, so it is the anchor, even though Section 9.2's
            // list of rejected extensions names duplicate keys too.
            Fail(
                $"'{name}' appears twice in one JSON object, and Section 9.3 rejects that rather "
                + "than letting parser order decide which one wins.",
                line,
                column,
                origin,
                phase,
                diagnostics,
                key,
                "\u00A79.3");
            return false;
        }

        var lexed = QualifiedNameLexer.LexNativePart(name);

        if (lexed.Name is null)
        {
            var fault = lexed.Fault!.Value;

            diagnostics.Add(new BufferedDiagnostic(
                fault.IsWildcardFault
                    ? DiagnosticCodes.Wildcard001(
                        phase,
                        "\u00A79.1",
                        fault.Message,
                        cardinalityKey: origin.Key(line, column),
                        source: origin.File,
                        line: origin.LineOf(line),
                        column: origin.ColumnOf(column))
                    : DiagnosticCodes.Parse001(
                        phase,
                        "\u00A79.1",
                        fault.Message,
                        cardinalityKey: origin.SourceKey,
                        source: origin.File,
                        line: origin.LineOf(line),
                        column: origin.ColumnOf(column)),
                key));
            return false;
        }

        frame.Pending = new StructuredProperty(
            name, lexed.Name.Parts[0], NullPlaceholder, line, column);
        return true;
    }

    private static bool TryReadScalar(
        ref Utf8JsonReader reader,
        int line,
        int column,
        ProfileSource origin,
        DiagnosticPhase phase,
        DiagnosticBuffer diagnostics,
        StableOrderingKey key,
        out StructuredScalar? scalar)
    {
        scalar = null;

        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                scalar = StructuredScalar.OfNativeString(reader.GetString()!, line, column);
                return true;

            case JsonTokenType.True:
            case JsonTokenType.False:
                scalar = StructuredScalar.OfTyped(
                    ScalarPayload.OfBoolean(reader.TokenType == JsonTokenType.True), line, column);
                return true;

            case JsonTokenType.Null:
                scalar = StructuredScalar.OfTyped(ScalarPayload.Null, line, column);
                return true;

            case JsonTokenType.Number:
                return TryReadNumber(
                    Encoding.UTF8.GetString(reader.ValueSpan),
                    line,
                    column,
                    origin,
                    phase,
                    diagnostics,
                    key,
                    out scalar);

            default:
                throw new InvalidOperationException(
                    $"a JSON value token is not {reader.TokenType}.");
        }
    }

    private static bool TryReadNumber(
        string lexeme,
        int line,
        int column,
        ProfileSource origin,
        DiagnosticPhase phase,
        DiagnosticBuffer diagnostics,
        StableOrderingKey key,
        out StructuredScalar? scalar)
    {
        scalar = null;

        // Section 9.1 decides on the written form, not on the value: "a JSON number whose lexical
        // form contains a fraction part or exponent is an arbitrary-precision decimal. Every other
        // valid JSON number is an arbitrary-precision integer." So 1e0 is a decimal and 1 is not.
        var isDecimal = lexeme.Contains('.', StringComparison.Ordinal)
            || lexeme.Contains('e', StringComparison.Ordinal)
            || lexeme.Contains('E', StringComparison.Ordinal);

        if (isDecimal)
        {
            if (!BigDecimal.TryParse(lexeme, out var value))
            {
                Fail(
                    $"'{lexeme}' is not a JSON number this reader can represent exactly.",
                    line, column, origin, phase, diagnostics, key);
                return false;
            }

            scalar = StructuredScalar.OfTyped(ScalarPayload.OfDecimal(value), line, column);
            return true;
        }

        if (!BigInteger.TryParse(
            lexeme, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
        {
            Fail(
                $"'{lexeme}' is not a JSON number this reader can represent exactly.",
                line, column, origin, phase, diagnostics, key);
            return false;
        }

        scalar = StructuredScalar.OfTyped(ScalarPayload.OfInteger(integer), line, column);
        return true;
    }

    private static bool Attach(
        StructuredNode node,
        Stack<Frame> frames,
        ref StructuredNode? root,
        ProfileSource origin,
        DiagnosticPhase phase,
        DiagnosticBuffer diagnostics,
        StableOrderingKey key)
    {
        if (frames.Count == 0)
        {
            if (root is not null)
            {
                Fail(
                    "a JSON document is exactly one value, and this one holds a second.",
                    node.Line, node.Column, origin, phase, diagnostics, key);
                return false;
            }

            root = node;
            return true;
        }

        frames.Peek().Add(node);
        return true;
    }

    private static void Fail(
        string message,
        int line,
        int column,
        ProfileSource origin,
        DiagnosticPhase phase,
        DiagnosticBuffer diagnostics,
        StableOrderingKey key,
        string spec = "\u00A79.2") =>
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

    /// <summary>
    /// Stands in for a property's value until its value token arrives, and is never observed.
    /// </summary>
    private static readonly StructuredScalar NullPlaceholder =
        StructuredScalar.OfTyped(ScalarPayload.Null, 1, 1);

    /// <summary>One open container.</summary>
    private sealed class Frame
    {
        private readonly ImmutableArray<StructuredProperty>.Builder properties =
            ImmutableArray.CreateBuilder<StructuredProperty>();

        private readonly ImmutableArray<StructuredNode>.Builder items =
            ImmutableArray.CreateBuilder<StructuredNode>();

        private readonly bool isMapping;
        private readonly int line;
        private readonly int column;

        public Frame(bool isMapping, int line, int column)
        {
            this.isMapping = isMapping;
            this.line = line;
            this.column = column;
        }

        /// <summary>The decoded property names seen so far, for the Section 9.3 duplicate test.</summary>
        public HashSet<string> Keys { get; } = new(StringComparer.Ordinal);

        /// <summary>The property whose value has not arrived yet.</summary>
        public StructuredProperty? Pending { get; set; }

        public void Add(StructuredNode node)
        {
            if (isMapping)
            {
                properties.Add(Pending! with { Value = node });
                Pending = null;
                return;
            }

            items.Add(node);
        }

        public StructuredNode ToNode() => isMapping
            ? new StructuredMapping(properties.ToImmutable(), line, column)
            : new StructuredSequence(items.ToImmutable(), line, column);
    }

    /// <summary>
    /// Converts a UTF-8 byte offset into the Section 22 line and column of the same position.
    /// </summary>
    /// <remarks>
    /// <see cref="Utf8JsonReader"/> counts bytes and Section 22 counts Unicode scalars, so the two
    /// disagree on every line holding a character outside ASCII. Converting through this table is
    /// what keeps a diagnostic's column pointing at the character a reader would count to.
    /// </remarks>
    private sealed class PositionMap
    {
        private readonly string text;
        private readonly List<int> lineStartBytes = [0];
        private readonly List<int> lineStartChars = [0];

        public PositionMap(string text)
        {
            this.text = text;

            var position = 0;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];

                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    position += 4;
                    i++;
                    continue;
                }

                position += c < 0x80 ? 1 : c < 0x800 ? 2 : 3;

                if (c is not ('\r' or '\n'))
                {
                    continue;
                }

                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    position++;
                    i++;
                }

                lineStartBytes.Add(position);
                lineStartChars.Add(i + 1);
            }
        }

        /// <summary>The one-based line and column of a UTF-8 byte offset.</summary>
        /// <param name="offset">The offset, as <see cref="Utf8JsonReader"/> reports it.</param>
        public (int Line, int Column) Locate(long offset)
        {
            var line = lineStartBytes.BinarySearch((int)Math.Clamp(offset, 0, int.MaxValue));

            if (line < 0)
            {
                line = ~line - 1;
            }

            var position = lineStartBytes[line];
            var index = lineStartChars[line];
            var column = 1;

            while (position < offset && index < text.Length)
            {
                var c = text[index];

                if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
                {
                    position += 4;
                    index += 2;
                }
                else
                {
                    position += c < 0x80 ? 1 : c < 0x800 ? 2 : 3;
                    index++;
                }

                column++;
            }

            return (line + 1, column);
        }

        /// <summary>The one-based line and column a <see cref="JsonException"/> reports.</summary>
        /// <param name="error">The exception.</param>
        /// <remarks>
        /// Its own <c>BytePositionInLine</c> is a byte count, so it goes back through the table
        /// rather than being reported as a column.
        /// </remarks>
        public (int Line, int Column) Locate(JsonException error)
        {
            if (error.LineNumber is not { } line || error.BytePositionInLine is not { } position)
            {
                return (1, 1);
            }

            var index = (int)Math.Clamp(line, 0, lineStartBytes.Count - 1);

            return Locate(lineStartBytes[index] + position);
        }
    }
}
