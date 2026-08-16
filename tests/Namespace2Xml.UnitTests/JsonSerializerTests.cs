using System.Collections.Immutable;
using System.Text;
using Namespace2Xml.Budgets;
using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Output;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 19.3 JSON serialization and its Section 16.9 options.
/// </summary>
/// <remarks>
/// Every expectation here is authored from the specification clause named in the test, never from
/// what the serializer currently produces.
/// </remarks>
[TestFixture]
public class JsonSerializerTests
{
    private DiagnosticBuffer diagnostics = null!;

    [SetUp]
    public void SetUp() => diagnostics = new DiagnosticBuffer();

    private static DocumentScalar Scalar(ScalarPayload payload) => new(payload, []);

    private static DocumentScalar Text(string value) => Scalar(ScalarPayload.OfString(value));

    private static DocumentMapping Map(params (string Key, DocumentNode Value)[] members) =>
        new([.. members.Select(member => new DocumentMember(member.Key, member.Value))], []);

    private static DocumentSequence Seq(params DocumentNode[] items) => new([.. items], []);

    private static BoundComment Comment(string text) =>
        new(text, CommentPlacement.Leading, StableOrderingKey.First);

    private string Serialize(JsonOutputOptions options, DocumentNode document)
    {
        var writer = new OutputBufferWriter(new GlobalBudget(ResourceLimits.Defaults));

        new JsonSerializer(options, diagnostics, new DestinationRef("out.json", 0))
            .TrySerialize(document, writer)
            .ShouldBeTrue();

        return Encoding.UTF8.GetString(writer.Build().ToArray());
    }

    // ---- Section 19.3 layout ---------------------------------------------------------------------

    /// <summary>
    /// Section 19.3 "uses indented output by default", and Section 16.9 fixes that indent at two
    /// ASCII spaces per nesting level.
    /// </summary>
    [Test]
    public void TheDefaultLayoutIndentsTwoSpacesPerLevel() =>
        Serialize(JsonOutput.Default, Map(("a", Map(("b", Text("v"))))))
            .ShouldBe("{\n  \"a\": {\n    \"b\": \"v\"\n  }\n}\n");

    /// <summary>
    /// Section 16.9's <c>Compact</c> emits "no insignificant spaces or line breaks", which includes
    /// the space after a key's colon and the space after a separating comma.
    /// </summary>
    [Test]
    public void CompactEmitsNoInsignificantWhitespace() =>
        Serialize(
            JsonOutputOptions.Compact,
            Map(("a", Text("1")), ("b", Seq(Text("x"), Text("y")))))
            .ShouldBe("{\"a\":\"1\",\"b\":[\"x\",\"y\"]}\n");

    /// <summary>
    /// Section 19.3 "preserves ordered mapping order", so the writer emits members in the order the
    /// document carries rather than sorting them by key.
    /// </summary>
    [Test]
    public void MappingOrderIsThePreservedDocumentOrderNotKeyOrder() =>
        Serialize(
            JsonOutputOptions.Compact,
            Map(("z", Text("1")), ("a", Text("2")), ("m", Text("3"))))
            .ShouldBe("{\"z\":\"1\",\"a\":\"2\",\"m\":\"3\"}\n");

    /// <summary>
    /// Section 14.1: an output view with nothing in it emits an empty mapping. An empty container
    /// has no interior to indent, so it collapses onto one pair of brackets.
    /// </summary>
    /// <param name="document">The empty container.</param>
    /// <param name="expected">Its Section 19.3 spelling.</param>
    [TestCaseSource(nameof(EmptyContainers))]
    public void AnEmptyContainerCollapses(DocumentNode document, string expected) =>
        Serialize(JsonOutput.Default, document).ShouldBe(expected);

    private static IEnumerable<TestCaseData> EmptyContainers()
    {
        yield return new TestCaseData(Map(), "{}\n").SetName("AnEmptyMappingCollapses");
        yield return new TestCaseData(Seq(), "[]\n").SetName("AnEmptySequenceCollapses");
    }

    /// <summary>
    /// Section 14.1 permits JSON to "emit a scalar document", so a bare scalar view is written as a
    /// top-level scalar rather than being wrapped in a synthesized key.
    /// </summary>
    [Test]
    public void ABareScalarIsAScalarDocument() =>
        Serialize(JsonOutput.Default, Text("bare")).ShouldBe("\"bare\"\n");

    // ---- Section 19.3 typed scalars --------------------------------------------------------------

    /// <summary>
    /// Section 19.3 "preserves typed scalars", so a Boolean, an integer, a decimal and a null are
    /// emitted as JSON literals rather than as quoted strings.
    /// </summary>
    /// <param name="payload">The typed payload.</param>
    /// <param name="expected">Its JSON literal.</param>
    [TestCaseSource(nameof(TypedScalars))]
    public void TypedScalarsAreEmittedAsJsonLiterals(ScalarPayload payload, string expected) =>
        Serialize(JsonOutputOptions.Compact, Map(("k", Scalar(payload)))).ShouldBe(
            "{\"k\":" + expected + "}\n");

    private static IEnumerable<TestCaseData> TypedScalars()
    {
        yield return new TestCaseData(ScalarPayload.OfBoolean(true), "true").SetName("TrueIsALiteral");
        yield return new TestCaseData(ScalarPayload.OfBoolean(false), "false").SetName("FalseIsALiteral");
        yield return new TestCaseData(ScalarPayload.OfInteger(42), "42").SetName("AnIntegerIsALiteral");
        yield return new TestCaseData(ScalarPayload.Null, "null").SetName("NullIsALiteral");
        yield return new TestCaseData(ScalarPayload.OfString("42"), "\"42\"").SetName("AStringStaysQuoted");
    }

    /// <summary>
    /// Section 19.3 serializes "logical line breaks as JSON <c>\n</c> escapes", so a multiline value
    /// never breaks the physical line it is written on.
    /// </summary>
    [Test]
    public void ALogicalLineBreakBecomesAnEscape() =>
        Serialize(JsonOutputOptions.Compact, Map(("k", Text("a\nb"))))
            .ShouldBe("{\"k\":\"a\\nb\"}\n");

    /// <summary>
    /// JSON requires the quote, the backslash, and every character below U+0020 to be escaped. The
    /// five with short forms use them; the rest use <c>\u</c>.
    /// </summary>
    /// <param name="value">The string being written.</param>
    /// <param name="expected">Its escaped spelling.</param>
    [TestCase("a\"b", "a\\\"b")]
    [TestCase("a\\b", "a\\\\b")]
    [TestCase("a\bb", "a\\bb")]
    [TestCase("a\fb", "a\\fb")]
    [TestCase("a\rb", "a\\rb")]
    [TestCase("a\tb", "a\\tb")]
    [TestCase("a\u0000b", "a\\u0000b")]
    [TestCase("a\u001Fb", "a\\u001Fb")]
    [TestCase("a/b", "a/b")]
    public void ControlCharactersAreEscaped(string value, string expected) =>
        Serialize(JsonOutputOptions.Compact, Map(("k", Text(value))))
            .ShouldBe("{\"k\":\"" + expected + "\"}\n");

    /// <summary>
    /// The escaping rules apply to keys as well as to values, because a key is a JSON string.
    /// </summary>
    [Test]
    public void KeysAreEscapedLikeValues() =>
        Serialize(JsonOutputOptions.Compact, Map(("a\"b", Text("v"))))
            .ShouldBe("{\"a\\\"b\":\"v\"}\n");

    // ---- Section 16.9 EscapeNonAscii -------------------------------------------------------------

    /// <summary>
    /// JSON output is UTF-8 by default, so Section 16.9's escaping is opt-in and non-ASCII text is
    /// otherwise emitted literally.
    /// </summary>
    [Test]
    public void NonAsciiTextIsLiteralWithoutTheOption() =>
        Serialize(JsonOutputOptions.Compact, Map(("k", Text("caf\u00e9"))))
            .ShouldBe("{\"k\":\"caf\u00e9\"}\n");

    /// <summary>
    /// Section 16.9's <c>EscapeNonAscii</c> emits "every scalar above U+007F as an uppercase
    /// hexadecimal <c>\uXXXX</c> escape".
    /// </summary>
    [Test]
    public void EscapeNonAsciiUsesUppercaseHexadecimal() =>
        Serialize(
            JsonOutputOptions.Compact | JsonOutputOptions.EscapeNonAscii,
            Map(("k", Text("caf\u00e9"))))
            .ShouldBe("{\"k\":\"caf\\u00E9\"}\n");

    /// <summary>
    /// A supplementary-plane character has no single <c>\uXXXX</c> escape, so JSON spells it as the
    /// surrogate pair. Splitting the pair would produce two unpaired surrogates and lose the
    /// character.
    /// </summary>
    [Test]
    public void ASupplementaryCharacterIsEscapedAsItsSurrogatePair() =>
        Serialize(
            JsonOutputOptions.Compact | JsonOutputOptions.EscapeNonAscii,
            Map(("k", Text("\U0001F600"))))
            .ShouldBe("{\"k\":\"\\uD83D\\uDE00\"}\n");

    /// <summary>
    /// U+007F is ASCII and is not a JSON control character, so it is emitted literally unless the
    /// option asks otherwise. The boundary Section 16.9 names is "above U+007F".
    /// </summary>
    [Test]
    public void TheEscapingBoundaryIsAboveDeleteNotAtIt() =>
        Serialize(
            JsonOutputOptions.Compact | JsonOutputOptions.EscapeNonAscii,
            Map(("k", Text("\u007f\u0080"))))
            .ShouldBe("{\"k\":\"\u007f\\u0080\"}\n");

    /// <summary>
    /// The option applies to keys too, so a document written with it holds no byte above U+007F.
    /// </summary>
    [Test]
    public void EscapeNonAsciiAppliesToKeys() =>
        Serialize(
            JsonOutputOptions.Compact | JsonOutputOptions.EscapeNonAscii,
            Map(("caf\u00e9", Text("v"))))
            .ShouldBe("{\"caf\\u00E9\":\"v\"}\n");

    // ---- Section 19.3 comments -------------------------------------------------------------------

    /// <summary>
    /// Section 19.3 "renders comments nowhere and emits a summarized discard warning when comments
    /// exist". Summarized means one diagnostic for the whole document, not one per comment.
    /// </summary>
    [Test]
    public void DiscardedCommentsProduceOneSummarizedWarning()
    {
        var document = new DocumentMapping(
            [new DocumentMember("a", new DocumentScalar(ScalarPayload.OfString("1"), [Comment("one")]))],
            [Comment("two"), Comment("three")]);

        Serialize(JsonOutputOptions.Compact, document).ShouldBe("{\"a\":\"1\"}\n");

        var warning = diagnostics.Drain().ShouldHaveSingleItem();

        warning.Code.ShouldBe("WARN003");
        warning.Severity.ShouldBe(DiagnosticSeverity.Warning);
        warning.Message.ShouldContain("3");
    }

    /// <summary>
    /// A document with no comments has nothing to discard, so Section 19.3's warning condition
    /// ("when comments exist") is not met and the run is silent.
    /// </summary>
    [Test]
    public void ADocumentWithoutCommentsWarnsNothing()
    {
        Serialize(JsonOutputOptions.Compact, Map(("a", Text("1"))));

        diagnostics.Drain().ShouldBeEmpty();
    }

    // ---- Section 16.9 option validation ----------------------------------------------------------

    /// <summary>
    /// Section 16.9 makes the two layout modes a mutually exclusive group, so naming both is the
    /// contradiction <c>SCHEME001</c> reports.
    /// </summary>
    [Test]
    public void IndentAndCompactContradictEachOther()
    {
        (JsonOutputOptions.Indent | JsonOutputOptions.Compact)
            .TryValidate(out var contradiction)
            .ShouldBeFalse();

        contradiction.ShouldNotBeNull().ShouldContain("mutually exclusive");
    }

    /// <summary>
    /// Section 16.9: "When a replacement omits every flag from a mutually exclusive mode group,
    /// that group's documented default is reapplied." Section 19.3 documents indentation as that
    /// default, so an options value naming only <c>EscapeNonAscii</c> still indents.
    /// </summary>
    [Test]
    public void OmittingBothLayoutFlagsReappliesIndentation()
    {
        JsonOutputOptions.EscapeNonAscii.Indents().ShouldBeTrue();
        JsonOutputOptions.None.Indents().ShouldBeTrue();
        JsonOutputOptions.Compact.Indents().ShouldBeFalse();
    }

    /// <summary>
    /// Every legal combination validates, so <c>SCHEME001</c> is reserved for the one contradiction
    /// Section 16.9 defines.
    /// </summary>
    /// <param name="options">A legal combination.</param>
    [TestCase(JsonOutputOptions.None)]
    [TestCase(JsonOutputOptions.Indent)]
    [TestCase(JsonOutputOptions.Compact)]
    [TestCase(JsonOutputOptions.EscapeNonAscii)]
    [TestCase(JsonOutputOptions.Indent | JsonOutputOptions.EscapeNonAscii)]
    [TestCase(JsonOutputOptions.Compact | JsonOutputOptions.EscapeNonAscii)]
    public void LegalCombinationsValidate(JsonOutputOptions options) =>
        options.TryValidate(out _).ShouldBeTrue();
}
