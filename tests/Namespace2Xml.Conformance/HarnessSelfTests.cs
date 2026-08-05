using System.Text;
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

    private static string One(string tail, string? spec = null)
    {
        var anchor = spec is null ? string.Empty : $"\"spec\":\"{spec}\",";
        return $"[\n{{\"code\":\"CLI001\",\"severity\":\"error\",\"phase\":\"cli\",{anchor}{tail}}}\n]\n";
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
