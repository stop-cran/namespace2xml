using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// The Section 6.2 option-token grammar. Every expectation here is authored from the
/// specification text, not from what the parser happens to do.
/// </summary>
[TestFixture]
public sealed class CommandLineParserTests
{
    /// <summary>A minimal well-formed vector, so a test can vary one thing at a time.</summary>
    private static readonly string[] Minimal = ["-i", "in.txt", "-s", "scheme.txt"];

    private static readonly string[] EveryLongOptionTakingAValue =
    [
        "--input", "--scheme", "--variables", "--output", "--verbosity", "--diagnostics-format",
        "--max-input-bytes", "--max-total-input-bytes", "--max-depth", "--max-nodes",
        "--max-xml-attributes", "--max-comments", "--max-comment-bytes", "--max-wildcard-rules",
        "--max-wildcard-candidates", "--max-generated", "--max-wildcard-iterations",
        "--max-reference-depth", "--max-outputs", "--max-total-output-bytes",
    ];

    private static CommandLine ParseOk(params string[] arguments)
    {
        var result = CommandLineParser.Parse(arguments);
        result.Diagnostic.ShouldBeNull();
        return result.CommandLine.ShouldNotBeNull();
    }

    private static Diagnostic ParseFail(params string[] arguments)
    {
        var result = CommandLineParser.Parse(arguments);
        result.CommandLine.ShouldBeNull();
        result.Succeeded.ShouldBeFalse();
        return result.Diagnostic.ShouldNotBeNull();
    }

    /// <summary>
    /// A structural rendering. <c>ImmutableArray&lt;T&gt;</c> equality compares the underlying
    /// array reference, so the compiler-generated record equality on <see cref="CommandLine"/>
    /// would report two identically-populated results as different.
    /// </summary>
    private static string Describe(CommandLine line) => string.Join(
        '\u001f',
        string.Join(',', line.Inputs),
        string.Join(',', line.Schemes),
        string.Join(',', line.Variables),
        line.OutputRoot,
        line.Verbosity.ToString(),
        line.DiagnosticsFormat.ToString(),
        line.Limits.ToString());

    // ---- required options and defaults -------------------------------------------------

    [Test]
    public void InputIsRequired() =>
        ParseFail("-s", "scheme.txt").Code.ShouldBe("CLI001");

    [Test]
    public void SchemeIsRequired() =>
        ParseFail("-i", "in.txt").Code.ShouldBe("CLI001");

    [Test]
    public void EmptyVectorIsRejected() =>
        ParseFail().Code.ShouldBe("CLI001");

    [Test]
    public void DefaultsComeFromTheOptionsTable()
    {
        var line = ParseOk(Minimal);

        line.OutputRoot.ShouldBe(".");
        line.Verbosity.ShouldBe(Verbosity.Information);
        line.DiagnosticsFormat.ShouldBe(DiagnosticFormat.Text);
        line.Limits.ShouldBe(ResourceLimits.Defaults);
        line.Variables.ShouldBeEmpty();
    }

    // ---- list options ------------------------------------------------------------------

    [Test]
    public void AListOptionAcceptsValuesUntilTheNextOptionToken() =>
        ParseOk("-i", "a", "b", "c", "-s", "scheme.txt").Inputs.ShouldBe(["a", "b", "c"]);

    /// <summary>
    /// Section 6.2: repeated occurrences "concatenate their values in exact command-line token
    /// order". The later occurrence appends; it does not replace, and it does not sort.
    /// </summary>
    [Test]
    public void RepeatedListOccurrencesConcatenateInTokenOrder() =>
        ParseOk("-i", "b", "-s", "scheme.txt", "--input", "a").Inputs.ShouldBe(["b", "a"]);

    [Test]
    public void LongAndShortSpellingsAreTheSameOption() =>
        ParseOk("-i", "a", "--input", "b", "-s", "scheme.txt").Inputs.ShouldBe(["a", "b"]);

    [Test]
    public void VariablesAreAListOption() =>
        ParseOk([.. Minimal, "-v", "a=1", "b=2"]).Variables.ShouldBe(["a=1", "b=2"]);

    // ---- single-valued options ---------------------------------------------------------

    [Test]
    public void ALaterSingleValuedOccurrenceOverridesAnEarlierOne() =>
        ParseOk([.. Minimal, "-o", "first", "--output", "second"]).OutputRoot.ShouldBe("second");

    /// <summary>"second" is attached to no option once <c>--output</c> is satisfied.</summary>
    [Test]
    public void ASingleValuedOptionStopsAcceptingAfterOneValue() =>
        ParseFail([.. Minimal, "--output", "first", "second"]).Code.ShouldBe("CLI001");

    // ---- the inline form ---------------------------------------------------------------

    /// <summary>
    /// The inline form is available to <em>every</em> long option, which is the whole point of
    /// the amendment. A value each option accepts detached must also be accepted inline, and
    /// must produce the identical result.
    /// </summary>
    [Test]
    public void TheInlineFormWorksOnEveryLongOption(
        [ValueSource(nameof(EveryLongOptionTakingAValue))] string option)
    {
        var detached = CommandLineParser.Parse([.. Minimal, option, ValueFor(option)]);
        var inline = CommandLineParser.Parse([.. Minimal, $"{option}={ValueFor(option)}"]);

        detached.Succeeded.ShouldBeTrue($"'{option}' should accept its value detached");
        inline.Succeeded.ShouldBeTrue($"'{option}' should accept its value inline");
        Describe(inline.CommandLine!).ShouldBe(Describe(detached.CommandLine!));

        static string ValueFor(string option) => option switch
        {
            "--verbosity" => "warning",
            "--diagnostics-format" => "json",
            _ when option.StartsWith("--max-", StringComparison.Ordinal) => "7",
            _ => "x",
        };
    }

    [Test]
    public void TheFirstEqualsSeparatesAndTheRemainderIsVerbatim() =>
        ParseOk([.. Minimal, "--output=a=b=c"]).OutputRoot.ShouldBe("a=b=c");

    [Test]
    public void AnEmptyRemainderIsAnEmptyValue() =>
        ParseOk([.. Minimal, "--output="]).OutputRoot.ShouldBe(string.Empty);

    /// <summary>
    /// "--input=--" supplies the literal value "--"; the following "-o" is still an option.
    /// </summary>
    [Test]
    public void AnInlineValueIsNeverTheEndOfOptionsMarker()
    {
        var line = ParseOk("--input=--", "-s", "scheme.txt", "-o", "out");

        line.Inputs.ShouldBe(["--"]);
        line.OutputRoot.ShouldBe("out");
    }

    [Test]
    public void AnInlineValueOnAListOptionLeavesItAcceptingMore() =>
        ParseOk("--input=a", "b", "-s", "scheme.txt").Inputs.ShouldBe(["a", "b"]);

    /// <summary>
    /// The whole token is the name, so it names an option that does not exist. It must not
    /// quietly create an input file called "a" or "=a".
    /// </summary>
    [Test]
    public void ShortOptionsHaveNoInlineForm()
    {
        var diagnostic = ParseFail("-i=a", "-s", "scheme.txt");

        diagnostic.Code.ShouldBe("CLI001");
        diagnostic.Message.ShouldContain("-i=a");
    }

    // ---- values, "-", and unattached tokens ---------------------------------------------

    [Test]
    public void ASingleHyphenIsAnOrdinaryValue() =>
        ParseOk("-i", "-", "-s", "scheme.txt").Inputs.ShouldBe(["-"]);

    [Test]
    public void AValueAttachedToNoOptionIsRejected() =>
        ParseFail("stray", "-i", "a", "-s", "scheme.txt").Code.ShouldBe("CLI001");

    [Test]
    public void AnOptionTokenMayNotSatisfyAPendingValue() =>
        ParseFail([.. Minimal, "--output", "--verbosity", "debug"]).Code.ShouldBe("CLI001");

    [Test]
    public void AnOptionThatEndsTheVectorStillRequiringAValueIsRejected() =>
        ParseFail([.. Minimal, "--output"]).Code.ShouldBe("CLI001");

    [Test]
    public void AnUnrecognizedOptionIsRejected() =>
        ParseFail([.. Minimal, "--nonesuch", "x"]).Code.ShouldBe("CLI001");

    // ---- the end-of-options marker ------------------------------------------------------

    [Test]
    public void ABareDoubleHyphenHandsEveryFollowingTokenToThePrecedingListOption()
    {
        var line = ParseOk("-s", "scheme.txt", "-i", "a", "--", "-o", "--nonesuch", "--");

        line.Inputs.ShouldBe(["a", "-o", "--nonesuch", "--"]);
        line.OutputRoot.ShouldBe(".");
    }

    [Test]
    public void ABareDoubleHyphenNeedsNoPrecedingValueOnTheListOption() =>
        ParseOk("-s", "scheme.txt", "-i", "--", "a", "-o").Inputs.ShouldBe(["a", "-o"]);

    [Test]
    public void ABareDoubleHyphenAfterASingleValuedOptionIsRejected() =>
        ParseFail("-i", "a", "-s", "scheme.txt", "-o", "out", "--", "x").Code.ShouldBe("CLI001");

    [Test]
    public void ABareDoubleHyphenWithNoPrecedingOptionIsRejected() =>
        ParseFail("--", "a").Code.ShouldBe("CLI001");

    // ---- informational options take no value --------------------------------------------

    /// <summary>
    /// Section 6.1 short-circuits these before parsing, so what matters here is only that they
    /// never swallow an operational argument if parsing is reached.
    /// </summary>
    [Test]
    public void HelpAndVersionTakeNoValueAndDoNotConsumeTheNextToken()
    {
        ParseOk("--help", "-i", "a", "-s", "scheme.txt").Inputs.ShouldBe(["a"]);
        ParseOk("--version", "-i", "a", "-s", "scheme.txt").Inputs.ShouldBe(["a"]);
    }

    // ---- enumerated values ---------------------------------------------------------------

    [TestCase("trace", Verbosity.Trace)]
    [TestCase("debug", Verbosity.Debug)]
    [TestCase("information", Verbosity.Information)]
    [TestCase("warning", Verbosity.Warning)]
    [TestCase("error", Verbosity.Error)]
    [TestCase("critical", Verbosity.Critical)]
    [TestCase("none", Verbosity.None)]
    [TestCase("WARNING", Verbosity.Warning)]
    [TestCase("Critical", Verbosity.Critical)]
    public void VerbosityIsParsedCaseInsensitively(string text, Verbosity expected) =>
        ParseOk([.. Minimal, "--verbosity", text]).Verbosity.ShouldBe(expected);

    [TestCase("")]
    [TestCase("verbose")]
    [TestCase("info")]
    [TestCase(" warning")]
    public void AnUnrecognizedVerbosityIsRejected(string text) =>
        ParseFail([.. Minimal, "--verbosity", text]).Code.ShouldBe("CLI001");

    [TestCase("text", DiagnosticFormat.Text)]
    [TestCase("json", DiagnosticFormat.Json)]
    [TestCase("JSON", DiagnosticFormat.Json)]
    [TestCase("Text", DiagnosticFormat.Text)]
    public void DiagnosticsFormatIsParsedCaseInsensitively(string text, DiagnosticFormat expected) =>
        ParseOk([.. Minimal, "--diagnostics-format", text]).DiagnosticsFormat.ShouldBe(expected);

    [TestCase("")]
    [TestCase("xml")]
    [TestCase("json ")]
    public void AnUnrecognizedDiagnosticsFormatIsRejected(string text) =>
        ParseFail([.. Minimal, "--diagnostics-format", text]).Code.ShouldBe("CLI001");

    // ---- limits ---------------------------------------------------------------------------

    [Test]
    public void CountLimitsAreApplied()
    {
        var line = ParseOk([.. Minimal, "--max-depth", "7", "--max-outputs", "31"]);

        line.Limits.MaxDepth.ShouldBe(7);
        line.Limits.MaxOutputs.ShouldBe(31);
        line.Limits.MaxNodes.ShouldBe(ResourceLimits.Defaults.MaxNodes);
    }

    [Test]
    public void ByteLimitsApplyTheirBinaryMultiplier() =>
        ParseOk([.. Minimal, "--max-input-bytes", "3MiB"]).Limits.MaxInputBytes.ShouldBe(3L * 1024 * 1024);

    [TestCase("--max-depth", "0")]
    [TestCase("--max-depth", "-1")]
    [TestCase("--max-depth", "+8")]
    [TestCase("--max-depth", "01")]
    [TestCase("--max-depth", "1MiB")]
    [TestCase("--max-input-bytes", "1.5GiB")]
    [TestCase("--max-input-bytes", "2 MiB")]
    [TestCase("--max-input-bytes", "10MB")]
    [TestCase("--max-input-bytes", "9223372036854775808")]
    [TestCase("--max-input-bytes", "9000000000GiB")]
    public void AMalformedLimitValueIsRejected(string option, string value) =>
        ParseFail([.. Minimal, option, value]).Code.ShouldBe("CLI001");

    /// <summary>
    /// The reported fault is the first in token order, not the first the accumulator happens to
    /// enumerate. Validating after the whole vector was read made this depend on dictionary order.
    /// </summary>
    [Test]
    public void TheFirstFaultInTokenOrderIsTheOneReported()
    {
        ParseFail([.. Minimal, "--max-outputs", "0", "--max-depth", "0"])
            .Message.ShouldContain("--max-outputs");

        ParseFail([.. Minimal, "--max-depth", "0", "--max-outputs", "0"])
            .Message.ShouldContain("--max-depth");
    }

    /// <summary>A malformed value is malformed even when a later occurrence overrides it.</summary>
    [Test]
    public void AnOverriddenMalformedValueIsStillRejected() =>
        ParseFail([.. Minimal, "--max-depth", "0", "--max-depth", "5"]).Code.ShouldBe("CLI001");

    // ---- diagnostic shape -------------------------------------------------------------------

    [Test]
    public void EveryFailureIsOneCli001AnchoredAtTheOptionsSection()
    {
        var diagnostic = ParseFail("--nonesuch");

        diagnostic.Code.ShouldBe("CLI001");
        diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
        diagnostic.Phase.ShouldBe(DiagnosticPhase.Cli);
        diagnostic.Spec.ShouldBe("§6.2");
        diagnostic.Message.ShouldNotBeNullOrWhiteSpace();
    }

    // ---- totality ----------------------------------------------------------------------------

    [Test]
    public void ParsingNeverThrowsForAnyVector()
    {
        string?[][] hostile =
        [
            [null],
            ["-i", null, "-s", "s"],
            ["--=x"],
            ["--"],
            ["-"],
            ["---"],
            ["-i", "-s"],
            ["=", "=="],
            [string.Empty],
            ["-i", string.Empty, "-s", "s"],
        ];

        foreach (var vector in hostile)
        {
            Should.NotThrow(() => CommandLineParser.Parse(vector!));
        }
    }

    [Test]
    public void ANullArgumentVectorIsAProgrammingError() =>
        Should.Throw<ArgumentNullException>(() => CommandLineParser.Parse(null!));
}
