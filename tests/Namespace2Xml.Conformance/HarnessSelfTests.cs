using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.Conformance;

/// <summary>
/// Tests of the harness itself. A conformance corpus is only as trustworthy as the machine that
/// reads it, so the tokenizer and the comparers are verified against cases that must fail as well
/// as cases that must pass. A comparer that never fails would make every fixture vacuous.
/// </summary>
[TestFixture]
public class HarnessSelfTests
{
    private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    [Test]
    public void EmptyArgsFileYieldsNoTokens() =>
        ArgsFile.Parse([], "test").ShouldBeEmpty();

    [Test]
    public void EachLineIsOneExactToken() =>
        ArgsFile.Parse(Utf8("-i\ninputs/a.properties\n-s\nschemes/s.properties\n"), "test")
            .ShouldBe(["-i", "inputs/a.properties", "-s", "schemes/s.properties"]);

    [Test]
    public void BlankLineIsABlankToken() =>
        ArgsFile.Parse(Utf8("-v\n\n-i\n"), "test").ShouldBe(["-v", "", "-i"]);

    [Test]
    public void RepeatedOptionsAndOrderArePreserved() =>
        ArgsFile.Parse(Utf8("-i\na\n-i\nb\n-s\nc\n"), "test").ShouldBe(["-i", "a", "-i", "b", "-s", "c"]);

    [Test]
    public void BareDoubleDashIsAnOrdinaryToken() =>
        ArgsFile.Parse(Utf8("-i\n--\nb\n"), "test").ShouldBe(["-i", "--", "b"]);

    [Test]
    public void TokensAreNeverSubstituted() =>
        // A value that looks like a shell or environment reference must survive untouched.
        ArgsFile.Parse(Utf8("-v\n$HOME\n-v\n%PATH%\n-v\n${X}\n"), "test")
            .ShouldBe(["-v", "$HOME", "-v", "%PATH%", "-v", "${X}"]);

    [Test]
    public void WhitespaceInsideATokenIsPreserved() =>
        ArgsFile.Parse(Utf8("-v\na.b = two words\n"), "test").ShouldBe(["-v", "a.b = two words"]);

    [Test]
    public void CarriageReturnIsRejected() =>
        Should.Throw<ConformanceFormatException>(() => ArgsFile.Parse(Utf8("-i\r\na\r\n"), "test"));

    [Test]
    public void ByteOrderMarkIsRejected() =>
        Should.Throw<ConformanceFormatException>(() => ArgsFile.Parse([0xEF, 0xBB, 0xBF, (byte)'a', (byte)'\n'], "test"));

    [Test]
    public void MissingFinalNewlineIsRejected() =>
        Should.Throw<ConformanceFormatException>(() => ArgsFile.Parse(Utf8("-i\na"), "test"));

    [Test]
    public void OutputTreeComparerDetectsMissingUnexpectedAndDifferingFiles()
    {
        using var expected = new TemporaryDirectory();
        using var actual = new TemporaryDirectory();

        expected.Write("same.txt", "x\n");
        expected.Write("only-expected.txt", "x\n");
        expected.Write("nested/differs.txt", "expected\n");

        actual.Write("same.txt", "x\n");
        actual.Write("only-actual.txt", "x\n");
        actual.Write("nested/differs.txt", "actual\n");

        var failures = OutputTreeComparer.Compare(expected.Path, actual.Path);

        failures.Count.ShouldBe(3);
        failures.ShouldContain(failure => failure.Contains("missing output 'only-expected.txt'"));
        failures.ShouldContain(failure => failure.Contains("unexpected output 'only-actual.txt'"));
        failures.ShouldContain(failure => failure.Contains("nested/differs.txt"));
    }

    [Test]
    public void OutputTreeComparerIsCaseSensitive()
    {
        using var expected = new TemporaryDirectory();
        using var actual = new TemporaryDirectory();

        expected.Write("Output.json", "{}\n");
        actual.Write("output.json", "{}\n");

        OutputTreeComparer.Compare(expected.Path, actual.Path).Count.ShouldBe(2);
    }

    [Test]
    public void OutputTreeComparerAcceptsAnIdenticalTree()
    {
        using var expected = new TemporaryDirectory();
        using var actual = new TemporaryDirectory();

        expected.Write("a/b.json", "{\"x\":1}\n");
        actual.Write("a/b.json", "{\"x\":1}\n");

        OutputTreeComparer.Compare(expected.Path, actual.Path).ShouldBeEmpty();
    }

    [Test]
    public void AbsentExpectedTreeMeansNoDestinationMayBeCreated()
    {
        using var actual = new TemporaryDirectory();
        actual.Write("created.json", "{}\n");

        OutputTreeComparer.Compare(null, actual.Path)
            .ShouldContain(failure => failure.Contains("unexpected output 'created.json'"));
    }

    [Test]
    public void EmptyDiagnosticStreamMatches() =>
        DiagnosticComparer.Compare(Utf8("[]\n"), Utf8("[]\n")).ShouldBeEmpty();

    [Test]
    public void DiagnosticStreamFramingIsChecked()
    {
        var expected = Utf8("[]\n");

        DiagnosticComparer.Compare(expected, Utf8("[]")).ShouldNotBeEmpty();
        DiagnosticComparer.Compare(expected, Utf8("[ ]\n")).ShouldNotBeEmpty();
        DiagnosticComparer.Compare(expected, Utf8("[]\r\n")).ShouldNotBeEmpty();
    }

    // The expected file is a diagnostic stream, so it is held to the Section 6.4.3 layout the tool
    // must produce. Accepting a shape the tool may never emit would let a fixture assert something
    // no conforming implementation can satisfy, and the failure would name the tool. These are
    // fixture defects, so they throw rather than returning a comparison failure.
    [TestCase("[\n]\n", TestName = "EmptyArrayWrittenWithMultiElementFraming")]
    [TestCase("[]", TestName = "MissingFinalNewline")]
    [TestCase("[ ]\n", TestName = "InsignificantWhitespace")]
    public void ANonCanonicalExpectedStreamIsAFixtureDefect(string expected) =>
        Should.Throw<ConformanceFormatException>(
                () => DiagnosticComparer.Compare(Utf8(expected), Utf8("[]\n")))
            .Message.ShouldContain("expected-diagnostics.json is not canonical");

    [Test]
    public void APrettyPrintedExpectedStreamIsAFixtureDefect() =>
        Should.Throw<ConformanceFormatException>(
                () => DiagnosticComparer.Compare(
                    Utf8("[\n  {\n    \"code\": \"CLI001\"\n  }\n]\n"),
                    Utf8("[]\n")))
            .Message.ShouldContain("expected-diagnostics.json is not canonical");

    [Test]
    public void PrettyPrintedStreamIsRejected()
    {
        var actual = Utf8("[\n  {\n    \"code\": \"CLI001\"\n  }\n]\n");

        DiagnosticComparer.Compare(Utf8("[]\n"), actual).ShouldNotBeEmpty();
    }

    // The following cases are the ones a dual-model review found the comparer accepting. Each is a
    // stream that is wrong in exactly one normative dimension, so a comparer that stops checking
    // that dimension fails here rather than silently blessing every future fixture.

    [Test]
    public void TwoElementsOnOneLineAreRejected()
    {
        var element = "{\"code\":\"CLI001\",\"severity\":\"error\",\"phase\":\"cli\",\"spec\":\"§6.4.1\",\"message\":\"m\"}";

        var expected = Utf8($"[\n{element},\n{element}\n]\n");
        var actual = Utf8($"[\n{element},{element}\n]\n");

        DiagnosticComparer.Compare(expected, actual)
            .ShouldContain(failure => failure.Contains("one element on each line", StringComparison.Ordinal));
    }

    [Test]
    public void InsignificantWhitespaceInsideAnElementIsRejected()
    {
        var expected = Utf8(One("\"message\":\"m\"", "§6.4.1"));
        var actual = Utf8(
            "[\n{\"code\": \"CLI001\", \"severity\": \"error\", \"phase\": \"cli\", " +
            "\"spec\": \"§6.4.1\", \"message\": \"m\"}\n]\n");

        DiagnosticComparer.Compare(expected, actual)
            .ShouldContain(failure => failure.Contains("insignificant whitespace", StringComparison.Ordinal));
    }

    [Test]
    public void EmptyArrayWrittenWithMultiElementFramingIsRejected()
    {
        // Must report a framing failure rather than throwing: the slice that reads the body of a
        // populated stream computes a negative length on this input.
        DiagnosticComparer.Compare(Utf8("[]\n"), Utf8("[\n]\n"))
            .ShouldContain(failure => failure.Contains("framing is not normative", StringComparison.Ordinal));

        DiagnosticComparer.Compare(Utf8("[]\n"), Utf8("[\n\n]\n")).ShouldNotBeEmpty();
    }

    [Test]
    public void DuplicateMemberIsRejected()
    {
        var expected = Utf8(One("\"message\":\"m\"", "§6.4.1"));
        var actual = Utf8(
            "[\n{\"code\":\"CLI001\",\"code\":\"CLI002\",\"severity\":\"error\",\"phase\":\"cli\"," +
            "\"spec\":\"§6.4.1\",\"message\":\"m\"}\n]\n");

        DiagnosticComparer.Compare(expected, actual)
            .ShouldContain(failure => failure.Contains("appears more than once", StringComparison.Ordinal));
    }

    [TestCase("\"severity\":\"info\"", "severity")]
    [TestCase("\"code\":\"cli1\"", "code")]
    [TestCase("\"phase\":\"frobnicate\"", "phase")]
    [TestCase("\"spec\":\"not-an-anchor\"", "spec")]
    [TestCase("\"message\":123", "message")]
    public void SchemaValueConstraintsAreEnforced(string replacement, string member)
    {
        var members = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["code"] = "\"code\":\"CLI001\"",
            ["severity"] = "\"severity\":\"error\"",
            ["phase"] = "\"phase\":\"cli\"",
            ["spec"] = "\"spec\":\"§6.4.1\"",
            ["message"] = "\"message\":\"m\"",
        };

        var expected = Utf8("[\n{" + string.Join(",", members.Values) + "}\n]\n");
        members[member] = replacement;
        var actual = Utf8("[\n{" + string.Join(",", members.Values) + "}\n]\n");

        DiagnosticComparer.Compare(expected, actual)
            .ShouldContain(failure => failure.Contains($"member '{member}'", StringComparison.Ordinal));
    }

    [Test]
    public void NonPositivePositionIsRejected()
    {
        var body = "\"code\":\"CLI001\",\"severity\":\"error\",\"phase\":\"cli\",\"source\":\"a.properties\"";
        var tail = "\"spec\":\"§6.4.1\",\"message\":\"m\"";

        var expected = Utf8($"[\n{{{body},\"line\":1,{tail}}}\n]\n");
        var actual = Utf8($"[\n{{{body},\"line\":0,{tail}}}\n]\n");

        DiagnosticComparer.Compare(expected, actual)
            .ShouldContain(failure => failure.Contains("below the required minimum", StringComparison.Ordinal));
    }

    [Test]
    public void InvalidUtf8IsRejected()
    {
        var valid = Utf8(One("\"message\":\"m\"", "§6.4.1"));
        var corrupted = valid.ToArray();
        corrupted[^4] = 0xFF;

        DiagnosticComparer.Compare(valid, corrupted)
            .ShouldContain(failure => failure.Contains("not valid UTF-8", StringComparison.Ordinal));
    }

    [Test]
    public void MissingSeparatorCommaIsRejected()
    {
        var element = "{\"code\":\"CLI001\",\"severity\":\"error\",\"phase\":\"cli\",\"spec\":\"§6.4.1\",\"message\":\"m\"}";

        var expected = Utf8($"[\n{element},\n{element}\n]\n");
        var actual = Utf8($"[\n{element}\n{element}\n]\n");

        DiagnosticComparer.Compare(expected, actual).ShouldNotBeEmpty();
    }

    [Test]
    public void TheComparerAndThePublishedSchemaAgreeOnWhichMembersExist()
    {
        // Loading the schema throws when MemberOrder and the generated file disagree, which is the
        // drift this indirection exists to prevent.
        Should.NotThrow(() => DiagnosticComparer.Compare(Utf8("[]\n"), Utf8("[]\n")));
    }

    [Test]
    public void MessageIsNeverCompared()
    {
        var expected = Utf8(One("\"message\":\"expected prose\"", "§6.4.1"));
        var actual = Utf8(One("\"message\":\"совсем другая проза\"", "§6.4.1"));

        DiagnosticComparer.Compare(expected, actual).ShouldBeEmpty();
    }

    [Test]
    public void SpecAnchorIsComparedOnlyWhenPinned()
    {
        var withoutAnchor = Utf8(
            "[\n{\"code\":\"CLI001\",\"severity\":\"error\",\"phase\":\"cli\",\"message\":\"m\"}\n]\n");
        var actual = Utf8(One("\"message\":\"m\"", spec: "§6.4.1"));

        DiagnosticComparer.Compare(withoutAnchor, actual).ShouldBeEmpty();

        var pinnedElsewhere = Utf8(One("\"message\":\"m\"", spec: "§99.9"));

        DiagnosticComparer.Compare(pinnedElsewhere, actual).ShouldNotBeEmpty();
    }

    [Test]
    public void StructuredFieldsAreComparedExactly()
    {
        var expected = Utf8(One("\"message\":\"m\"", spec: "§6.4.1"));
        var different = Utf8(
            "[\n{\"code\":\"PARSE001\",\"severity\":\"error\",\"phase\":\"cli\",\"spec\":\"§6.4.1\"," +
            "\"message\":\"m\"}\n]\n");

        DiagnosticComparer.Compare(expected, different)
            .ShouldContain(failure => failure.Contains("'code'"));
    }

    [Test]
    public void ArrayOrderIsCompared()
    {
        var first = Utf8(
            "[\n{\"code\":\"CLI001\",\"severity\":\"error\",\"phase\":\"cli\",\"spec\":\"§6\",\"message\":\"m\"},\n" +
            "{\"code\":\"WARN001\",\"severity\":\"warning\",\"phase\":\"cli\",\"spec\":\"§6\",\"message\":\"m\"}\n]\n");
        var swapped = Utf8(
            "[\n{\"code\":\"WARN001\",\"severity\":\"warning\",\"phase\":\"cli\",\"spec\":\"§6\",\"message\":\"m\"},\n" +
            "{\"code\":\"CLI001\",\"severity\":\"error\",\"phase\":\"cli\",\"spec\":\"§6\",\"message\":\"m\"}\n]\n");

        DiagnosticComparer.Compare(first, swapped).ShouldNotBeEmpty();
    }

    [Test]
    public void UnknownMemberIsRejectedBecauseTheSchemaIsClosed()
    {
        var actual = Utf8(
            "[\n{\"code\":\"CLI001\",\"severity\":\"error\",\"phase\":\"cli\",\"spec\":\"§6\"," +
            "\"message\":\"m\",\"hint\":\"extra\"}\n]\n");

        DiagnosticComparer.Compare(actual, actual)
            .ShouldContain(failure => failure.Contains("closed"));
    }

    [Test]
    public void MembersOutOfNormativeOrderAreRejected()
    {
        var actual = Utf8(
            "[\n{\"severity\":\"error\",\"code\":\"CLI001\",\"phase\":\"cli\",\"spec\":\"§6\",\"message\":\"m\"}\n]\n");

        DiagnosticComparer.Compare(actual, actual)
            .ShouldContain(failure => failure.Contains("normative order"));
    }

    [Test]
    public void NullMemberIsRejected()
    {
        var actual = Utf8(
            "[\n{\"code\":\"CLI001\",\"severity\":\"error\",\"phase\":\"cli\",\"source\":null," +
            "\"spec\":\"§6\",\"message\":\"m\"}\n]\n");

        DiagnosticComparer.Compare(actual, actual)
            .ShouldContain(failure => failure.Contains("omitted"));
    }

    [Test]
    public void ColumnWithoutLineIsRejected()
    {
        var actual = Utf8(
            "[\n{\"code\":\"CLI001\",\"severity\":\"error\",\"phase\":\"cli\",\"source\":\"a\",\"column\":1," +
            "\"spec\":\"§6\",\"message\":\"m\"}\n]\n");

        DiagnosticComparer.Compare(actual, actual)
            .ShouldContain(failure => failure.Contains("'column' requires 'line'"));
    }

    /// <summary>
    /// Section 22 makes <c>rule</c> an array of canonical names. A writer that emits the joined
    /// string an earlier revision used decodes to a perfectly ordinary JSON value, so only a
    /// type check rejects it.
    /// </summary>
    [Test]
    public void AJoinedRuleStringIsRejected()
    {
        var actual = Utf8(
            "[\n{\"code\":\"WILDCARD002\",\"severity\":\"error\",\"phase\":\"input\"," +
            "\"rule\":\"a.*.b, a.*.*.c\",\"spec\":\"§12.4\",\"message\":\"m\"}\n]\n");

        DiagnosticComparer.Compare(actual, actual)
            .ShouldContain(failure => failure.Contains("must be an array"));
    }

    /// <summary>
    /// The schema's <c>minItems</c> makes an empty array a wrong spelling of absent, which is the
    /// shape a writer produces when it emits the member unconditionally.
    /// </summary>
    [Test]
    public void AnEmptyRuleArrayIsRejected()
    {
        var actual = Utf8(
            "[\n{\"code\":\"WILDCARD002\",\"severity\":\"error\",\"phase\":\"input\"," +
            "\"rule\":[],\"spec\":\"§12.4\",\"message\":\"m\"}\n]\n");

        DiagnosticComparer.Compare(actual, actual)
            .ShouldContain(failure => failure.Contains("below the required minimum"));
    }

    /// <summary>A rule array holding anything but names is not the Section 22 member.</summary>
    [Test]
    public void ANonStringRuleElementIsRejected()
    {
        var actual = Utf8(
            "[\n{\"code\":\"WILDCARD002\",\"severity\":\"error\",\"phase\":\"input\"," +
            "\"rule\":[1],\"spec\":\"§12.4\",\"message\":\"m\"}\n]\n");

        DiagnosticComparer.Compare(actual, actual)
            .ShouldContain(failure => failure.Contains("every"));
    }

    /// <summary>
    /// Element order within <c>rule</c> is Section 12.4 source order, so two streams naming the
    /// same rules in different orders are different streams.
    /// </summary>
    [Test]
    public void RuleElementOrderIsCompared()
    {
        var expected = Utf8(
            "[\n{\"code\":\"WILDCARD002\",\"severity\":\"error\",\"phase\":\"input\"," +
            "\"rule\":[\"a.*.b\",\"a.*.*.c\"],\"spec\":\"§12.4\",\"message\":\"m\"}\n]\n");
        var actual = Utf8(
            "[\n{\"code\":\"WILDCARD002\",\"severity\":\"error\",\"phase\":\"input\"," +
            "\"rule\":[\"a.*.*.c\",\"a.*.b\"],\"spec\":\"§12.4\",\"message\":\"m\"}\n]\n");

        DiagnosticComparer.Compare(expected, actual)
            .ShouldContain(failure => failure.Contains("'rule'"));
    }

    private static string One(string tail, string? spec = null)
    {
        var anchor = spec is null ? string.Empty : $"\"spec\":\"{spec}\",";
        return $"[\n{{\"code\":\"CLI001\",\"severity\":\"error\",\"phase\":\"cli\",{anchor}{tail}}}\n]\n";
    }

    // ---- Section 6.4.3 escape spellings ----
    //
    // A JSON reader decodes "\u0041", "\/" and a literal "A" or "/" to the same string, so every
    // assertion made on decoded values is blind to the spelling. These cases are each wrong in
    // exactly one escape dimension while decoding to text the rest of the comparer accepts, so a
    // comparer that stops reading raw bytes fails here instead of blessing a writer whose output
    // is not byte-identical.

    /// <summary>
    /// Each case decodes to the same diagnostic as the expected stream and differs only in how a
    /// scalar is spelled, so nothing but the raw-byte check can tell them apart. <c>message</c> is
    /// used because the comparer never compares its value, which leaves the spelling as the only
    /// thing under test.
    /// </summary>
    /// <param name="canonical">The <c>message</c> member as Section 6.4.3 requires it.</param>
    /// <param name="emitted">The same text spelled a way Section 6.4.3 does not admit.</param>
    /// <param name="reason">The clause the spelling violates.</param>
    [TestCase("\"A\"", "\"\\u0041\"", "literally as UTF-8", TestName = "AnAsciiLetterEscapedAsU0041")]
    [TestCase("\"é\"", "\"\\u00e9\"", "literally as UTF-8", TestName = "ANonAsciiScalarEscapedRatherThanEmitted")]
    [TestCase("\"a/b\"", "\"a\\/b\"", "permits only", TestName = "ASolidusEscaped")]
    [TestCase("\"a\\nb\"", "\"a\\u000ab\"", "named escape", TestName = "ALineFeedSpelledAsAUnicodeEscape")]
    [TestCase("\"a\\tb\"", "\"a\\u0009b\"", "named escape", TestName = "ATabSpelledAsAUnicodeEscape")]
    [TestCase("\"a\\u001fb\"", "\"a\\u001Fb\"", "uppercase", TestName = "AControlEscapedWithUppercaseHexadecimal")]
    public void ANonNormativeEscapeSpellingIsRejected(string canonical, string emitted, string reason)
    {
        var expected = Utf8(One($"\"message\":{canonical}", "§6.4.1"));
        var actual = Utf8(One($"\"message\":{emitted}", "§6.4.1"));

        DiagnosticComparer.Compare(expected, actual)
            .ShouldContain(failure => failure.Contains(reason, StringComparison.Ordinal));
    }

    /// <summary>
    /// A fixture whose own expected stream carries a non-normative escape is a fixture defect, and
    /// must throw rather than be reported against the tool: it asserts a spelling no conforming
    /// implementation may produce, so the resulting failure would name the wrong party.
    /// </summary>
    [Test]
    public void ANonNormativeEscapeInTheExpectedStreamIsAFixtureDefect() =>
        Should.Throw<ConformanceFormatException>(
                () => DiagnosticComparer.Compare(
                    Utf8(One("\"message\":\"\\u0041\"", "§6.4.1")),
                    Utf8(One("\"message\":\"A\"", "§6.4.1"))))
            .Message.ShouldContain("expected-diagnostics.json is not canonical");


    /// <summary>
    /// The spellings Section 6.4.3 does fix must pass, or the check would be rejecting correct
    /// output — which is the failure mode that makes a corpus unusable rather than merely weak.
    /// </summary>
    /// <param name="message">A <c>message</c> member written the way Section 6.4.3 requires.</param>
    [TestCase("\"a\\\"b\"", TestName = "AQuoteTakesABackslash")]
    [TestCase("\"a\\\\b\"", TestName = "ABackslashTakesABackslash")]
    [TestCase("\"a\\nb\"", TestName = "ALineFeedTakesItsNamedEscape")]
    [TestCase("\"a\\tb\"", TestName = "ATabTakesItsNamedEscape")]
    [TestCase("\"a\\u0001b\"", TestName = "AnUnnamedControlTakesLowercaseHexadecimal")]
    [TestCase("\"héllo — Ω 𝔘\"", TestName = "EveryOtherScalarIsEmittedLiterally")]
    [TestCase("\"a/b\"", TestName = "ASolidusIsLiteral")]
    [TestCase("\"a\u007fb\"", TestName = "DeleteIsNotAC0ControlAndIsLiteral")]
    public void ANormativeEscapeSpellingIsAccepted(string message)
    {
        var actual = Utf8(One($"\"message\":{message}", "§6.4.1"));

        DiagnosticComparer.Compare(actual, actual).ShouldBeEmpty();
    }

    /// <summary>
    /// A raw C0 control inside a string is not whitespace the layout check looks for, because that
    /// check skips string contents entirely.
    /// </summary>
    /// <summary>
    /// A raw C0 control inside a string is rejected, though not by the escape check: RFC 8259
    /// forbids it outright, so the reader refuses the stream before any layout rule is consulted.
    /// The escape check therefore does not duplicate that arm, and this test records which
    /// mechanism actually closes the hole so a later reader does not add unreachable code back.
    /// </summary>
    [Test]
    public void ARawControlInsideAStringIsRejected()
    {
        var expected = Utf8(One("\"message\":\"a\\tb\"", "§6.4.1"));
        var actual = Utf8(One("\"message\":\"a\tb\"", "§6.4.1"));

        DiagnosticComparer.Compare(expected, actual)
            .ShouldContain(failure => failure.Contains("is invalid within a JSON string", StringComparison.Ordinal));
    }

    // ---- Appendix C.5 standard output ----
    //
    // Every case below must fail the comparer. The rule this suite defends is that the corpus
    // could previously claim Section 26 item 85 while asserting nothing a silent binary would
    // violate, so a comparer that is merely present is not enough: it has to reject.

    /// <summary>Writes an <c>expected-stdout.txt</c> and compares the given output against it.</summary>
    private static IEnumerable<string> CompareStdout(TemporaryDirectory directory, string? expected, string actual)
    {
        if (expected is not null)
        {
            directory.Write("expected-stdout.txt", expected);
        }

        return StandardOutputComparer.Compare(
            System.IO.Path.Combine(directory.Path, "expected-stdout.txt"),
            Utf8(actual));
    }

    [Test]
    public void RequiredStandardOutputLinesAreMatchedInOrder()
    {
        using var directory = new TemporaryDirectory();

        CompareStdout(directory, "name: namespace2xml\nversion: 3.0\n", "name: namespace2xml\nversion: 3.0\n")
            .ShouldBeEmpty();
    }

    [Test]
    public void AMissingRequiredLineFails()
    {
        using var directory = new TemporaryDirectory();

        CompareStdout(directory, "contract-bundle: r1\n", "name: namespace2xml\n")
            .ShouldContain(failure => failure.Contains("does not contain the line 'contract-bundle: r1'"));
    }

    [Test]
    public void SilentStandardOutputFailsEveryRequiredLine()
    {
        // The defect that motivated this comparer: a binary printing nothing satisfied the exit
        // code and the empty output tree, and the case still claimed to cover --version.
        using var directory = new TemporaryDirectory();

        CompareStdout(directory, "name: namespace2xml\ncontract-bundle: r1\n", string.Empty)
            .Count().ShouldBe(2);
    }

    [Test]
    public void RequiredLinesOutOfOrderFail()
    {
        using var directory = new TemporaryDirectory();

        CompareStdout(directory, "first\nsecond\n", "second\nfirst\n")
            .ShouldContain(failure => failure.Contains("out of the expected order"));
    }

    [Test]
    public void ARequiredLineMustMatchAWholeLine()
    {
        // Substring matching would accept 'contract-bundle: r13-tampered' for 'contract-bundle: r13'.
        using var directory = new TemporaryDirectory();

        CompareStdout(directory, "contract-bundle: r13\n", "contract-bundle: r13-tampered\n")
            .ShouldNotBeEmpty();
    }

    [Test]
    public void RequiredLinesAreCaseSensitive()
    {
        using var directory = new TemporaryDirectory();

        CompareStdout(directory, "USAGE\n", "usage\n").ShouldNotBeEmpty();
    }

    [Test]
    public void AForbiddenLineThatOccursFails()
    {
        using var directory = new TemporaryDirectory();

        CompareStdout(directory, "USAGE\n!version: 3.0\n", "USAGE\nversion: 3.0\n")
            .ShouldContain(failure => failure.Contains("forbidden line 'version: 3.0'"));
    }

    [Test]
    public void AForbiddenLineThatIsAbsentPasses()
    {
        using var directory = new TemporaryDirectory();

        CompareStdout(directory, "USAGE\n!version: 3.0\n", "USAGE\n").ShouldBeEmpty();
    }

    [Test]
    public void ABackslashEscapesALiteralExclamationMark()
    {
        using var directory = new TemporaryDirectory();

        CompareStdout(directory, "\\!literal\n", "!literal\n").ShouldBeEmpty();
        CompareStdout(directory, "\\!literal\n", "literal\n").ShouldNotBeEmpty();
    }

    [Test]
    public void BlankLinesInTheExpectationAreIgnored()
    {
        using var directory = new TemporaryDirectory();

        CompareStdout(directory, "\nfirst\n\n\nsecond\n\n", "first\nsecond\n").ShouldBeEmpty();
    }

    [Test]
    public void AnAbsentExpectationRequiresEmptyStandardOutput()
    {
        using var directory = new TemporaryDirectory();

        CompareStdout(directory, null, string.Empty).ShouldBeEmpty();
        CompareStdout(directory, null, "generated content\n")
            .ShouldContain(failure => failure.Contains("standard output must be empty"));
    }

    [Test]
    public void PlaceholdersResolveFromTheContractBundleAndNotFromTheTool()
    {
        using var directory = new TemporaryDirectory();

        using var bundle = JsonDocument.Parse(File.ReadAllBytes(CorpusLayout.ContractBundle));
        var revision = bundle.RootElement.GetProperty("revision").GetString();

        CompareStdout(directory, "contract-bundle: ${contract-bundle}\n", $"contract-bundle: {revision}\n")
            .ShouldBeEmpty();

        CompareStdout(directory, "contract-bundle: ${contract-bundle}\n", "contract-bundle: r0+deadbeef\n")
            .ShouldNotBeEmpty();
    }

    [Test]
    public void AnUnknownPlaceholderIsAFixtureDefectRatherThanAToolDefect()
    {
        using var directory = new TemporaryDirectory();

        Should.Throw<ConformanceFormatException>(
            () => CompareStdout(directory, "x: ${no-such-field}\n", "x: y\n").ToList());
    }

    [Test]
    public void CarriageReturnInTheExpectationIsRejected()
    {
        using var directory = new TemporaryDirectory();

        CompareStdout(directory, "USAGE\r\n", "USAGE\n")
            .ShouldContain(failure => failure.Contains("LF line endings"));
    }

    [Test]
    public void InvalidUtf8OnStandardOutputIsRejected()
    {
        using var directory = new TemporaryDirectory();
        directory.Write("expected-stdout.txt", "USAGE\n");

        StandardOutputComparer
            .Compare(System.IO.Path.Combine(directory.Path, "expected-stdout.txt"), [0xC3, 0x28])
            .ShouldContain(failure => failure.Contains("not valid UTF-8"));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "n2x-" + Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        internal void Write(string relative, string content)
        {
            var target = System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, new UTF8Encoding(false).GetBytes(content));
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // A leaked temporary directory must never fail a conformance run.
            }
        }
    }
}
