using System.Collections.Immutable;
using Namespace2Xml.Budgets;
using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Inputs;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using Namespace2Xml.Scalars;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 10 tests for reading one <c>RestrictedYaml1</c> document into the structured model.
/// </summary>
/// <remarks>
/// Every expectation here is authored from the specification clause named in the test, never from
/// what the reader currently produces. Section 10.1 makes <c>RestrictedYaml1</c> normative "rather
/// than an underlying library's advertised YAML 1.1 or 1.2 mode", so these tests are also the
/// evidence that the host library's schema never decides.
/// </remarks>
[TestFixture]
public class YamlInputReaderTests
{
    private static StructuredNode? Read(
        string document,
        DiagnosticBuffer diagnostics,
        ResourceLimits? limits = null,
        SourceBudget? budget = null)
    {
        var effective = limits ?? ResourceLimits.Defaults;

        return YamlInputReader.Read(
            document,
            effective,
            budget ?? new SourceBudget(effective, 0),
            ProfileSource.OfFile("d.yaml"),
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
    /// A budget crossing is recorded on the budget rather than reported here, for the Section 7.3
    /// reason given in <see cref="JsonInputReaderTests"/>: asserting on the diagnostic buffer would
    /// assert on an empty buffer and pass for the wrong reason. Recording the fault is only half of
    /// the contract, so this also asserts the reader <em>stopped</em>. A reader that noted the
    /// crossing and kept walking would still report <c>LIMIT001</c> while doing exactly the
    /// unbounded work the bound exists to prevent.
    /// </remarks>
    private static ResourceBound? Crossed(string document, int maxDepth)
    {
        var limits = ResourceLimits.Defaults with { MaxDepth = maxDepth };
        var budget = new SourceBudget(limits, 0);
        var buffer = new DiagnosticBuffer();

        var node = Read(document, buffer, limits, budget);
        buffer.Drain().ShouldBeEmpty();

        if (budget.Fault is null)
        {
            node.ShouldNotBeNull();
            return null;
        }

        node.ShouldBeNull();
        return budget.Fault.Value.Bound;
    }

    /// <summary>Reads a document holding one mapping key <c>v</c> and returns that value.</summary>
    /// <param name="value">The YAML text of the value, on the same line as its key.</param>
    private static StructuredNode Value(string value) =>
        Read("v: " + value + "\n").ShouldBeOfType<StructuredMapping>()
            .Properties.Single().Value;

    private static string Text(StructuredNode node)
    {
        var scalar = node.ShouldBeOfType<StructuredScalar>();

        return scalar.Payload is { } payload ? payload.ToCanonicalText() : scalar.NativeString!;
    }

    /// <summary>Asserts that a value resolved to a string carrying exactly its written text.</summary>
    /// <param name="value">The YAML text of the value.</param>
    private static void ShouldStayAString(string value)
    {
        var scalar = Value(value).ShouldBeOfType<StructuredScalar>();

        scalar.Payload.ShouldBeNull();
        scalar.NativeString.ShouldBe(value);
    }

    /// <summary>
    /// Section 10.1: "'null', 'Null', 'NULL', '~', or an empty plain scalar are null". The list of
    /// spellings is exhaustive, so a spelling outside it is an ordinary string.
    /// </summary>
    [TestCase("null")]
    [TestCase("Null")]
    [TestCase("NULL")]
    [TestCase("~")]
    [TestCase("")]
    public void ANullSpellingResolvesToNull(string value) =>
        Value(value).ShouldBeOfType<StructuredScalar>().Payload.ShouldBe(ScalarPayload.Null);

    /// <summary>
    /// Section 10.1 spells the null forms out one by one, and unlike the Boolean rule beside it
    /// does not say "case-insensitively". A spelling that differs only in case is therefore a
    /// string, and reading the two rules alike would silently swallow it.
    /// </summary>
    [TestCase("nULL")]
    [TestCase("nuLl")]
    [TestCase("NuLL")]
    [TestCase("nil")]
    [TestCase("None")]
    [TestCase("nothing")]
    public void ANullLookalikeStaysAString(string value) => ShouldStayAString(value);

    /// <summary>
    /// Section 10.1 resolves the Boolean spellings "case-insensitively", illustrated with
    /// <c>tRuE</c>. Only <c>true</c> and <c>false</c> are Booleans: YAML 1.1's <c>yes</c>,
    /// <c>no</c>, <c>on</c> and <c>off</c> are not in the subset, and the host library would
    /// resolve several of them if its own schema were consulted.
    /// </summary>
    [TestCase("true", true)]
    [TestCase("True", true)]
    [TestCase("TRUE", true)]
    [TestCase("tRuE", true)]
    [TestCase("false", false)]
    [TestCase("False", false)]
    [TestCase("FALSE", false)]
    [TestCase("fAlSe", false)]
    public void ABooleanResolvesCaseInsensitively(string value, bool expected) =>
        Value(value).ShouldBeOfType<StructuredScalar>()
            .Payload.ShouldBe(ScalarPayload.OfBoolean(expected));

    /// <summary>
    /// Section 10.1 admits only <c>true</c> and <c>false</c> as Booleans, so every other YAML 1.1
    /// Boolean spelling is a string.
    /// </summary>
    [TestCase("yes")]
    [TestCase("Yes")]
    [TestCase("no")]
    [TestCase("on")]
    [TestCase("off")]
    [TestCase("y")]
    [TestCase("n")]
    public void AYamlOneOneBooleanStaysAString(string value) => ShouldStayAString(value);

    /// <summary>
    /// Section 10.1 admits "JSON-compatible decimal integers and floating-point values", and
    /// Section 9.1's lexical rule then decides the kind, so <c>1e0</c> is a decimal despite denoting
    /// a whole number and Section 18 renders it with the fraction it now has.
    /// </summary>
    [TestCase("0", "0")]
    [TestCase("1", "1")]
    [TestCase("-0", "0")]
    [TestCase("-17", "-17")]
    [TestCase("1.0", "1.0")]
    [TestCase("1.50", "1.5")]
    [TestCase("1e0", "1.0")]
    [TestCase("1E2", "100.0")]
    [TestCase("-1.5e-3", "-0.0015")]
    [TestCase("123456789012345678901234567890", "123456789012345678901234567890")]
    public void ANumberFollowsTheJsonGrammar(string value, string expected) =>
        Text(Value(value)).ShouldBe(expected);

    /// <summary>
    /// Section 10.1 admits only <em>JSON-compatible</em> numbers, so a form JSON rejects stays a
    /// string. Every one of these is accepted by at least one general-purpose numeric parse in the
    /// framework, which is why the grammar is checked rather than delegated.
    /// </summary>
    [TestCase("+1")]
    [TestCase(".5")]
    [TestCase("1.")]
    [TestCase("01")]
    [TestCase("00")]
    [TestCase("-.5")]
    [TestCase("1_000")]
    [TestCase("0x10")]
    [TestCase("0o17")]
    [TestCase("1e")]
    [TestCase("1e+")]
    [TestCase("1,5")]
    [TestCase("NaN")]
    [TestCase("Infinity")]
    [TestCase("12:30")]
    public void ANumberLookalikeStaysAString(string value) => ShouldStayAString(value);

    /// <summary>
    /// Section 10.1 resolves plain scalars only, so quoting is how a document says "this is the
    /// string, not the value it looks like". Without that the spelling <c>true</c> could not name
    /// the string at all.
    /// </summary>
    [TestCase("\"true\"", "true")]
    [TestCase("'true'", "true")]
    [TestCase("\"null\"", "null")]
    [TestCase("'~'", "~")]
    [TestCase("\"1\"", "1")]
    [TestCase("'1.5'", "1.5")]
    [TestCase("\"\"", "")]
    public void AQuotedScalarIsAlwaysAString(string value, string expected)
    {
        var scalar = Value(value).ShouldBeOfType<StructuredScalar>();

        scalar.Payload.ShouldBeNull();
        scalar.NativeString.ShouldBe(expected);
    }

    /// <summary>
    /// A literal or folded block scalar is quoted by construction under the same Section 10.1 rule,
    /// so its content is a string whatever it spells.
    /// </summary>
    [Test]
    public void ABlockScalarIsAString()
    {
        var scalar = Read("v: |\n  true\n").ShouldBeOfType<StructuredMapping>()
            .Properties.Single().Value.ShouldBeOfType<StructuredScalar>();

        scalar.Payload.ShouldBeNull();
        scalar.NativeString.ShouldBe("true\n");
    }

    /// <summary>
    /// Section 10.1: "every plain or quoted scalar mapping key is treated as a string without scalar
    /// tag resolution". A key is a name, so the key <c>true</c> is the three-letter name and not a
    /// Boolean rendered back into one.
    /// </summary>
    [TestCase("true")]
    [TestCase("null")]
    [TestCase("1")]
    [TestCase("~")]
    [TestCase("1.50")]
    public void AKeyIsAStringWithoutResolution(string key) =>
        Read(key + ": x\n").ShouldBeOfType<StructuredMapping>()
            .Properties.Single().Key.ShouldBe(key);

    /// <summary>
    /// Section 10.1 makes duplicate mapping keys errors rather than letting parser order decide
    /// which one wins, which is the same hidden-override hazard Section 9.3 rejects for JSON.
    /// </summary>
    [Test]
    public void ADuplicateKeyIsRejected() =>
        Diagnose("a: 1\na: 2\n").Single().Code.ShouldBe("PARSE001");

    /// <summary>A key repeated in a sibling mapping is an ordinary distinct key.</summary>
    [Test]
    public void TheSameKeyInASiblingMappingIsAccepted() =>
        Read("x:\n  a: 1\ny:\n  a: 2\n").ShouldBeOfType<StructuredMapping>()
            .Properties.Length.ShouldBe(2);

    /// <summary>Section 10.1 admits sequences, whose items keep document order.</summary>
    [Test]
    public void ASequenceKeepsItemOrder() =>
        Read("t:\n  - a\n  - b\n  - c\n").ShouldBeOfType<StructuredMapping>()
            .Properties.Single().Value.ShouldBeOfType<StructuredSequence>()
            .Items.Select(Text)
            .ShouldBe(["a", "b", "c"]);

    /// <summary>
    /// Section 4.4 makes an empty container an explicit shape contribution, so the reader preserves
    /// the difference between an empty mapping and an empty sequence rather than dropping both.
    /// </summary>
    [Test]
    public void AnEmptyContainerSurvivesReading()
    {
        var document = Read("m: {}\ns: []\n").ShouldBeOfType<StructuredMapping>();

        document.Properties[0].Value.ShouldBeOfType<StructuredMapping>()
            .Properties.ShouldBeEmpty();
        document.Properties[1].Value.ShouldBeOfType<StructuredSequence>().Items.ShouldBeEmpty();
    }

    /// <summary>A YAML document is one value, and that value need not be a mapping.</summary>
    [Test]
    public void ADocumentRootNeedNotBeAMapping()
    {
        Read("- 1\n- 2\n").ShouldBeOfType<StructuredSequence>();
        Read("42\n").ShouldBeOfType<StructuredScalar>();
    }

    /// <summary>
    /// Section 10.2 makes anchors and aliases blocking input errors in this version, so neither
    /// half of the pair may pass. The anchor is refused where it is written, which is before the
    /// alias that would use it.
    /// </summary>
    [TestCase("a: &x 1\nb: 2\n")]
    [TestCase("a: &x 1\nb: *x\n")]
    [TestCase("a: &x\n  k: 1\n")]
    [TestCase("a: &x [1]\n")]
    public void AnAnchorIsRejected(string document) =>
        Diagnose(document).Single().Code.ShouldBe("PARSE001");

    /// <summary>
    /// Section 10.2 rejects "every explicit tag token -- including standard '!!' tags, verbatim
    /// tags, and local tags -- regardless of whether the tag would preserve the same basic value",
    /// so <c>!!str x</c> is refused even though <c>x</c> is a string either way.
    /// </summary>
    [TestCase("a: !!str x\n")]
    [TestCase("a: !!int 1\n")]
    [TestCase("a: !local x\n")]
    [TestCase("a: !<tag:example.com,2026:t> x\n")]
    [TestCase("a: !!map\n  k: 1\n")]
    [TestCase("a: !!seq [1]\n")]
    [TestCase("a: ! x\n")]
    [TestCase("a: !\n  k: 1\n")]
    public void AnExplicitTagIsRejected(string document) =>
        Diagnose(document).Single().Code.ShouldBe("PARSE001");

    /// <summary>
    /// Section 10.2 adds that "no tag is accepted implicitly", so the bare non-specific <c>!</c> is
    /// refused with the rest. It is a token the document wrote, and admitting it would be the one
    /// tag accepted without being named. An untagged node must stay unaffected by that rule.
    /// </summary>
    [Test]
    public void TheNonSpecificTagIsAnExplicitTag()
    {
        Diagnose("a: ! x\n").Single().Message.ShouldContain("'!'", Case.Sensitive);
        Read("a: x\n").ShouldBeOfType<StructuredMapping>().Properties.Length.ShouldBe(1);
    }

    /// <summary>
    /// Section 10.3 admits one implicit document per stream, so an explicit marker is refused even
    /// when the stream holds a single document, and a second document is refused with it.
    /// </summary>
    [TestCase("---\na: 1\n")]
    [TestCase("a: 1\n...\n")]
    [TestCase("---\na: 1\n---\nb: 2\n")]
    [TestCase("%YAML 1.2\n---\na: 1\n")]
    public void AnExplicitDocumentStructureIsRejected(string document) =>
        Diagnose(document).Single().Code.ShouldBe("PARSE001");

    /// <summary>
    /// A directive is only legal immediately before an explicit <c>---</c>, so Section 10.3's marker
    /// rule would refuse this document on its own. The directive is still named first, because
    /// "Section 10.2 does not preserve directives" is the reason the user needs and a report about
    /// a marker they did not think they wrote is not.
    /// </summary>
    [Test]
    public void ADirectiveIsNamedRatherThanTheMarkerItImplies()
    {
        var message = Diagnose("%YAML 1.2\n---\na: 1\n").Single().Message;

        message.ShouldContain("%YAML", Case.Sensitive);
        message.ShouldNotContain("---", Case.Sensitive);
    }

    /// <summary>
    /// Section 10.1 excludes merge keys from <c>RestrictedYaml1</c>. It does not say whether an
    /// unsupported merge key is an error or an ordinary key; refusing is the safer reading, because
    /// a merge key that silently became data would be the hidden override Section 9.3 rejects.
    /// </summary>
    [Test]
    public void AMergeKeyIsRejected() =>
        Diagnose("a: 1\n<<: {b: 2}\n").Single().Code.ShouldBe("PARSE001");

    /// <summary>
    /// A quoted key is a string by the Section 10.1 rule that quoting suppresses resolution, so
    /// <c>"&lt;&lt;"</c> names an ordinary key and the refusal above has an escape hatch.
    /// </summary>
    [Test]
    public void AQuotedMergeKeyIsAnOrdinaryKey() =>
        Read("a: 1\n\"<<\": 2\n").ShouldBeOfType<StructuredMapping>()
            .Properties.Select(property => property.Key)
            .ShouldBe(["a", "<<"]);

    /// <summary>
    /// Section 10.1 requires every mapping key to be a scalar, so a complex key is an error. It
    /// must be refused rather than reaching the code that attaches a value to the key that was
    /// never read.
    /// </summary>
    [TestCase("? [1, 2]\n: v\n")]
    [TestCase("? {a: 1}\n: v\n")]
    [TestCase("? - 1\n: v\n")]
    public void AComplexKeyIsRejected(string document) =>
        Diagnose(document).Single().Code.ShouldBe("PARSE001");

    /// <summary>
    /// A YAML document is exactly one value. Section 10.1 gives "an empty plain scalar" the null
    /// payload, but that is a scalar a document contains, not a stream holding no document at all.
    /// </summary>
    [TestCase("")]
    [TestCase("\n\n")]
    [TestCase("   \n\t\n")]
    [TestCase("# only a comment\n")]
    public void AStreamWithNoDocumentIsRejected(string document) =>
        Diagnose(document).Single().Code.ShouldBe("PARSE001");

    /// <summary>A malformed document is a blocking parse error rather than a partial read.</summary>
    [TestCase("a: [1, 2\n")]
    [TestCase("a: {b: 1\n")]
    [TestCase("a: 1\n b: 2\n")]
    [TestCase("\"unterminated\n")]
    public void AMalformedDocumentIsRejected(string document) =>
        Diagnose(document).Single().Code.ShouldBe("PARSE001");

    /// <summary>
    /// Section 22 positions are one-based, and the host library's own trailing "(Line: n, Col: n)"
    /// must not be repeated inside the message when the diagnostic already carries the position.
    /// </summary>
    [Test]
    public void AFaultReportsAOneBasedPosition()
    {
        var diagnostic = Diagnose("a: 1\nb: &x 2\n").Single();

        diagnostic.Line.ShouldBe(2);
        diagnostic.Column.ShouldBe(4);
        diagnostic.Message.ShouldNotContain("Line:", Case.Sensitive);
    }

    /// <summary>
    /// Section 16.2 counts nesting depth against <c>--max-depth</c>, and a container is named by the
    /// path that reaches it, so <c>a: {b: 1}</c> reaches depth two. Crossing the bound is
    /// <c>LIMIT001</c> under Section 23, never the host reader's own depth error. The empty
    /// container cases matter on their own: they are the only ones where a <em>container</em> rather
    /// than the scalar beneath it is what crosses, so they are what proves the walk stops there.
    /// </summary>
    [TestCase("a: 1\n", 1, false)]
    [TestCase("a:\n  b: 1\n", 1, true)]
    [TestCase("a:\n  b: 1\n", 2, false)]
    [TestCase("a:\n  b:\n    c: 1\n", 2, true)]
    [TestCase("a:\n  b:\n    c: 1\n", 3, false)]
    [TestCase("a:\n  - - 1\n", 2, true)]
    [TestCase("a:\n  b: {}\n", 1, true)]
    [TestCase("a:\n  b: []\n", 1, true)]
    [TestCase("a:\n  b: {}\n", 2, false)]
    public void DepthIsBoundedByMaxDepth(string document, int maxDepth, bool crossed) =>
        Crossed(document, maxDepth).ShouldBe(crossed ? ResourceBound.MaxDepth : null);

    /// <summary>
    /// The host parser's own nesting knob must never be the effective limit, so a document nested
    /// far past the bound still crosses <c>--max-depth</c> rather than failing to parse or
    /// overflowing the stack.
    /// </summary>
    [Test]
    public void TheHostDepthKnobNeverDecides()
    {
        var document = "v: " + new string('[', 400) + "1" + new string(']', 400) + "\n";

        Crossed(document, 3).ShouldBe(ResourceBound.MaxDepth);
    }

    /// <summary>
    /// Section 16.2 has "every element, attribute, text, comment, and CDATA overlay node" consume
    /// <c>--max-nodes</c>, so a scalar is charged as well as a container. Depth alone does not bound
    /// a document: a flat mapping of a million entries never nests past one. The tally is judged by
    /// the Section 7.3 join rather than here, so this asserts the charge, not a diagnostic.
    /// </summary>
    [TestCase("a: 1\n", 1)]
    [TestCase("a: 1\nb: 2\nc: 3\n", 3)]
    [TestCase("a:\n  b: 1\n", 2)]
    [TestCase("a:\n  - 1\n  - 2\n", 3)]
    [TestCase("42\n", 0)]
    public void EveryNodeBeneathTheRootIsCharged(string document, int nodes)
    {
        var budget = new SourceBudget(ResourceLimits.Defaults, 0);
        var buffer = new DiagnosticBuffer();

        Read(document, buffer, ResourceLimits.Defaults, budget).ShouldNotBeNull();
        buffer.Drain().ShouldBeEmpty();

        budget.Tally.Nodes.ShouldBe(nodes);
    }
}
