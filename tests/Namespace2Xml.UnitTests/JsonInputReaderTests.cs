using System.Collections.Immutable;
using Namespace2Xml.Budgets;
using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Inputs;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 9 tests for reading a JSON document into the format-neutral structured model.
/// </summary>
/// <remarks>
/// Every expectation here is authored from the specification clause named in the test, never from
/// what the reader currently produces.
/// </remarks>
[TestFixture]
public class JsonInputReaderTests
{
    private static StructuredNode? Read(
        string document,
        DiagnosticBuffer diagnostics,
        ResourceLimits? limits = null,
        SourceBudget? budget = null)
    {
        var effective = limits ?? ResourceLimits.Defaults;

        return JsonInputReader.Read(
            document,
            effective,
            budget ?? new SourceBudget(effective, 0),
            ProfileSource.OfFile("d.json"),
            DiagnosticPhase.Input,
            diagnostics,
            StableOrderingKey.FromSource(0, 1));
    }

    private static StructuredNode Read(string document)
    {
        var buffer = new DiagnosticBuffer();
        var node = Read(document, buffer);

        buffer.Drain().ShouldBeEmpty();

        return node.ShouldNotBeNull();
    }

    private static ImmutableArray<Diagnostic> Diagnose(
        string document, ResourceLimits? limits = null)
    {
        var buffer = new DiagnosticBuffer();

        Read(document, buffer, limits).ShouldBeNull();

        return buffer.Drain();
    }

    /// <summary>
    /// The bound this reader crossed, if any.
    /// </summary>
    /// <param name="document">The document to read.</param>
    /// <param name="maxDepth">The <c>--max-depth</c> to read it under.</param>
    /// <returns>The crossed bound, or <see langword="null"/> when none was.</returns>
    /// <remarks>
    /// A budget crossing is reported by the loader, not by the reader: Section 7.3 makes the reader
    /// accumulate its own source's contribution and nothing more, so it records the fault on the
    /// budget and stops. Asserting on the buffer here would assert on an empty buffer and pass for
    /// the wrong reason.
    /// </remarks>
    private static ResourceBound? Crossed(string document, int maxDepth)
    {
        var limits = ResourceLimits.Defaults with { MaxDepth = maxDepth };
        var budget = new SourceBudget(limits, 0);
        var buffer = new DiagnosticBuffer();

        Read(document, buffer, limits, budget);
        buffer.Drain().ShouldBeEmpty();

        return budget.Fault?.Bound;
    }

    private static StructuredNode Property(StructuredNode node, string name) =>
        node.ShouldBeOfType<StructuredMapping>()
            .Properties.Single(property => property.Key == name)
            .Value;

    private static string Text(StructuredNode node)
    {
        var scalar = node.ShouldBeOfType<StructuredScalar>();

        return scalar.Payload is { } payload ? payload.ToCanonicalText() : scalar.NativeString!;
    }

    /// <summary>
    /// Section 9.1: "A JSON number whose lexical form contains a fraction part or exponent is an
    /// arbitrary-precision decimal. Every other valid JSON number is an arbitrary-precision
    /// integer." The rule is lexical, so <c>1e0</c> is a decimal despite denoting a whole number,
    /// and Section 18's canonical decimal text then renders it with the fraction it now has.
    /// </summary>
    [TestCase("1", "1")]
    [TestCase("-0", "0")]
    [TestCase("1.0", "1.0")]
    [TestCase("1.50", "1.5")]
    [TestCase("1e0", "1.0")]
    [TestCase("1E2", "100.0")]
    [TestCase("123456789012345678901234567890", "123456789012345678901234567890")]
    [TestCase("-0.000000000000000000001", "-1.0e-21")]
    public void ANumbersKindFollowsItsLexicalForm(string literal, string expected) =>
        Text(Property(Read($"{{\"n\":{literal}}}"), "n")).ShouldBe(expected);

    /// <summary>
    /// Section 9.1 lists integers and decimals separately, so a lexical decimal that denotes a whole
    /// number must not collapse into the integer that Section 18 renders without a fraction.
    /// </summary>
    [Test]
    public void ALexicalDecimalIsNotAnInteger()
    {
        var document = Read("{\"i\":1,\"d\":1e0}");

        Text(Property(document, "i")).ShouldBe("1");
        Text(Property(document, "d")).ShouldBe("1.0");
    }

    /// <summary>Section 9.1 admits Booleans and null as typed scalars in their own right.</summary>
    [TestCase("true", ScalarKind.Boolean)]
    [TestCase("false", ScalarKind.Boolean)]
    [TestCase("null", ScalarKind.Null)]
    [TestCase("1", ScalarKind.Integer)]
    [TestCase("1.0", ScalarKind.Decimal)]
    public void ATypedScalarKeepsItsKind(string literal, ScalarKind expected) =>
        Property(Read($"{{\"v\":{literal}}}"), "v")
            .ShouldBeOfType<StructuredScalar>().Payload!.Kind.ShouldBe(expected);

    /// <summary>
    /// Section 9.1: "Each object-property name becomes one literal qualified-name part. Dots and
    /// backslashes in the native property name remain literal characters."
    /// </summary>
    [Test]
    public void ADottedPropertyNameIsOnePart()
    {
        var name = Read("{\"a.b\":1}").ShouldBeOfType<StructuredMapping>().Properties.Single().Name;

        name.ShouldBeOfType<OrdinaryPart>().Tokens.Single()
            .ShouldBeOfType<LiteralToken>().Text.ShouldBe("a.b");
    }

    /// <summary>Section 9.1 keeps a backslash literal in a native property name.</summary>
    [Test]
    public void ABackslashInAPropertyNameIsLiteral()
    {
        var name = Read("{\"c\\\\d\":1}").ShouldBeOfType<StructuredMapping>()
            .Properties.Single().Name;

        name.ShouldBeOfType<OrdinaryPart>().Tokens.Single()
            .ShouldBeOfType<LiteralToken>().Text.ShouldBe("c\\d");
    }

    /// <summary>Section 9.1 preserves object property order.</summary>
    [Test]
    public void PropertyOrderIsPreserved() =>
        Read("{\"z\":1,\"a\":2,\"m\":3}").ShouldBeOfType<StructuredMapping>()
            .Properties.Select(property => property.Key)
            .ShouldBe(["z", "a", "m"]);

    /// <summary>
    /// Section 9.3: "Standard mode rejects duplicate keys within one JSON object." The comparison is
    /// on the decoded name, so an escaped spelling of the same character is the same key.
    /// </summary>
    [TestCase("{\"a\":1,\"a\":2}")]
    [TestCase("{\"a\":1,\"\\u0061\":2}")]
    public void ADuplicateKeyIsRejected(string document) =>
        Diagnose(document).Single().Code.ShouldBe("PARSE001");

    /// <summary>
    /// Section 9.3 scopes the rule to one object, so the same key in a sibling object is fine.
    /// </summary>
    [Test]
    public void TheSameKeyInASiblingObjectIsAccepted() =>
        Read("{\"x\":{\"a\":1},\"y\":{\"a\":2}}").ShouldBeOfType<StructuredMapping>()
            .Properties.Length.ShouldBe(2);

    /// <summary>
    /// Section 9.2: "Trailing commas, duplicate object keys, non-finite numbers, and other
    /// nonstandard extensions are errors", and "JSON comments are not supported".
    /// </summary>
    [TestCase("{\"a\":1,}")]
    [TestCase("[1,]")]
    [TestCase("{\"a\":1} // trailing")]
    [TestCase("{/* inline */\"a\":1}")]
    [TestCase("{\"a\":NaN}")]
    [TestCase("{\"a\":Infinity}")]
    [TestCase("{'a':1}")]
    [TestCase("{a:1}")]
    public void ANonstandardExtensionIsRejected(string document) =>
        Diagnose(document).Single().Code.ShouldBe("PARSE001");

    /// <summary>A JSON document is exactly one value, so a second root value is an error.</summary>
    [TestCase("")]
    [TestCase("   \n\t ")]
    [TestCase("{\"a\":1} {\"b\":2}")]
    [TestCase("{\"a\":")]
    public void ADocumentIsExactlyOneValue(string document) =>
        Diagnose(document).Single().Code.ShouldBe("PARSE001");

    /// <summary>
    /// Section 22 positions are one-based character positions, so a multi-byte character before a
    /// fault must advance the column by one, not by its UTF-8 length.
    /// </summary>
    [Test]
    public void AColumnCountsCharactersRatherThanBytes()
    {
        // The Cyrillic key is four characters and eight UTF-8 bytes; the trailing '}' that the
        // trailing comma makes unexpected is the eleventh character.
        var diagnostic = Diagnose("{\"ключ\":1,}").Single();

        diagnostic.Line.ShouldBe(1);
        diagnostic.Column.ShouldBe(11);
    }

    /// <summary>A fault on a later line reports that line, counting from one.</summary>
    [Test]
    public void ALineCountsFromOne()
    {
        var diagnostic = Diagnose("{\n  \"a\": 1,\n}").Single();

        diagnostic.Line.ShouldBe(3);
        diagnostic.Column.ShouldBe(1);
    }

    /// <summary>
    /// Section 16.2 counts nesting depth against <c>--max-depth</c>, and a JSON container is named
    /// by the path that reaches it, so <c>{"a":{"b":1}}</c> reaches depth two. Crossing the bound is
    /// <c>LIMIT001</c> under Section 23, never the host reader's own depth error.
    /// </summary>
    [TestCase("{\"a\":1}", 1, false)]
    [TestCase("{\"a\":{\"b\":1}}", 1, true)]
    [TestCase("{\"a\":{\"b\":1}}", 2, false)]
    [TestCase("{\"a\":{\"b\":{\"c\":1}}}", 2, true)]
    [TestCase("{\"a\":{\"b\":{\"c\":1}}}", 3, false)]
    [TestCase("{\"a\":[[1]]}", 2, true)]
    public void DepthIsBoundedByMaxDepth(string document, int maxDepth, bool crossed) =>
        Crossed(document, maxDepth).ShouldBe(crossed ? ResourceBound.MaxDepth : null);

    /// <summary>
    /// The host reader's own <c>MaxDepth</c> must never be the effective limit, so a document nested
    /// far past the bound must still cross <c>--max-depth</c> rather than fail to parse. Were the
    /// host knob set only one level clear of the bound it would throw on the very token this reader
    /// means to refuse, and the answer would be <c>PARSE001</c> from the host instead.
    /// </summary>
    [Test]
    public void TheHostDepthKnobNeverDecides()
    {
        var document = string.Concat(Enumerable.Repeat("{\"n\":", 40))
            + "1"
            + new string('}', 40);

        Crossed(document, 3).ShouldBe(ResourceBound.MaxDepth);
    }

    /// <summary>Section 9.1 admits arrays, whose items keep document order.</summary>
    [Test]
    public void AnArrayKeepsItemOrder() =>
        Property(Read("{\"t\":[\"a\",\"b\",\"c\"]}"), "t")
            .ShouldBeOfType<StructuredSequence>()
            .Items.Select(Text)
            .ShouldBe(["a", "b", "c"]);

    /// <summary>
    /// Section 4.4 makes an empty container an explicit shape contribution, so the reader must
    /// preserve the difference between an empty mapping and an empty sequence rather than dropping
    /// both as "nothing".
    /// </summary>
    [Test]
    public void AnEmptyContainerSurvivesReading()
    {
        var document = Read("{\"m\":{},\"s\":[]}");

        Property(document, "m").ShouldBeOfType<StructuredMapping>().Properties.ShouldBeEmpty();
        Property(document, "s").ShouldBeOfType<StructuredSequence>().Items.ShouldBeEmpty();
    }

    /// <summary>
    /// Section 9.1 hands a string value to the namespace value lexer rather than decoding it here,
    /// so the reader carries the decoded JSON text and no scalar payload.
    /// </summary>
    [Test]
    public void AStringIsCarriedForTheValueLexer()
    {
        var scalar = Property(Read("{\"s\":\"a\\tb\"}"), "s").ShouldBeOfType<StructuredScalar>();

        scalar.Payload.ShouldBeNull();
        scalar.NativeString.ShouldBe("a\tb");
    }

    /// <summary>A document whose root is an array or a scalar is still exactly one value.</summary>
    [Test]
    public void ARootMayBeAnyValue()
    {
        Read("[1,2]").ShouldBeOfType<StructuredSequence>();
        Read("42").ShouldBeOfType<StructuredScalar>();
    }
}
