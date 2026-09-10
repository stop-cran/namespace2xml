using System.Globalization;
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

    private static IEnumerable<string> EveryLongOptionTakingAValue =>
        CommandLineOptions.All
            .Where(option => option.Arity is CommandLineOptionArity.List or CommandLineOptionArity.Single)
            .Select(option => option.Name);

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
        line.Limits.ToString(),
        line.FailOnWarning.ToString(CultureInfo.InvariantCulture));

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
        line.FailOnWarning.ShouldBeFalse();
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

    /// <summary>
    /// Section 6.2 starts at the host argument vector. Characters that a shell might otherwise
    /// interpret are data inside one host token and the tool never tokenizes them again.
    /// </summary>
    [Test]
    public void HostTokensAreNeverRetokenized()
    {
        const string input = "folder with space\\name\"quoted";
        const string scheme = "scheme with space\\name'quoted";

        var line = ParseOk("-i", input, "-s", scheme);

        line.Inputs.ShouldBe([input]);
        line.Schemes.ShouldBe([scheme]);
    }

    /// <summary>
    /// A .NET string can carry an unpaired surrogate even though no strict UTF-8 argument file
    /// can. Section 6.2 makes that host-boundary fault observable as one CLI diagnostic.
    /// </summary>
    [Test]
    public void AnIllFormedUnicodeHostTokenIsCli001()
    {
        var diagnostic = ParseFail("-i", "\ud800", "-s", "scheme.txt");

        diagnostic.Code.ShouldBe("CLI001");
        diagnostic.Message.ShouldContain("Unicode");
    }

    /// <summary>
    /// Each list occurrence owns its own arity fault. The first fault in token order wins, and the
    /// post-token required-option check retains input-before-scheme order.
    /// </summary>
    [Test]
    public void RequiredListOccurrenceFaultsHaveDeterministicPrecedence()
    {
        ParseFail("-i", "-s", "scheme.txt").Message.ShouldContain("'--input'");
        ParseFail("-s", "-i", "input.txt").Message.ShouldContain("'--scheme'");
        ParseFail("-i", "input.txt", "-i", "-s", "scheme.txt").Message.ShouldContain("'--input'");
        ParseFail("--fail-on-warning").Message.ShouldContain("'--input'");
    }

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

    /// <summary>
    /// The inline form separates at the first <c>=</c>, so <c>--output=</c> supplies an empty value
    /// rather than no value. Section 7.2 then rejects it: an empty token names nothing, and every
    /// path option treats it alike, so the tokenizer's willingness to produce an empty remainder is
    /// visible only as the diagnostic that follows.
    /// </summary>
    [Test]
    public void AnEmptyRemainderIsRejectedAsAPathValue()
    {
        var diagnostic = ParseFail([.. Minimal, "--output="]);

        diagnostic.Code.ShouldBe("CLI001");
        diagnostic.Spec.ShouldBe("\u00a76.2");
    }

    [Test]
    public void EveryPathOptionRejectsAnEmptyValue()
    {
        foreach (var argument in new[] { "--input=", "--scheme=", "--variables=", "--output=" })
        {
            ParseFail([.. Minimal, argument]).Code.ShouldBe("CLI001");
        }
    }

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

    [Test]
    public void ALimitNameTypoIsOneCli001RatherThanAPrefixMatch()
    {
        var result = CommandLineParser.Parse([.. Minimal, "--max-dept", "10"]);

        result.Succeeded.ShouldBeFalse();
        result.CommandLine.ShouldBeNull();
        result.Diagnostic.ShouldNotBeNull().Code.ShouldBe("CLI001");
        result.Diagnostic.Message.ShouldContain("'--max-dept'");
    }

    /// <summary>
    /// A catalog descriptor is executable metadata, not merely documentation. Adding an option
    /// without wiring its arity-specific parser path must fail this gate rather than ship an
    /// accepted-looking help row backed by an exception.
    /// </summary>
    [Test]
    public void EveryCatalogedOptionHasAParserHandler()
    {
        foreach (var option in CommandLineOptions.All)
        {
            string[] arguments =
                option.Arity is CommandLineOptionArity.List or CommandLineOptionArity.Single
                    ? [.. Minimal, option.Name, ValueFor(option)]
                    : [.. Minimal, option.Name];

            Should.NotThrow(() =>
            {
                var result = CommandLineParser.Parse(arguments);
                result.Succeeded.ShouldBeTrue($"'{option.Name}' has no working parser handler");
            });
        }

        static string ValueFor(CommandLineOption option) => option.Name switch
        {
            "--verbosity" => "warning",
            "--diagnostics-format" => "json",
            _ when option.Limit?.Kind == ResourceLimitKind.Bytes => "7KiB",
            _ when option.Limit is not null => "7",
            _ => "x",
        };
    }

    // ---- valueless operational flags ---------------------------------------------------

    [Test]
    public void FailOnWarningIsAnIdempotentValuelessFlag()
    {
        ParseOk([.. Minimal, "--fail-on-warning"]).FailOnWarning.ShouldBeTrue();
        ParseOk([.. Minimal, "--fail-on-warning", "--fail-on-warning"]).FailOnWarning.ShouldBeTrue();
    }

    [TestCase("--fail-on-warning=")]
    [TestCase("--fail-on-warning=true")]
    [TestCase("--fail-on-warning=false")]
    public void FailOnWarningRejectsEveryInlineValue(string argument)
    {
        var diagnostic = ParseFail([.. Minimal, argument]);

        diagnostic.Code.ShouldBe("CLI001");
        diagnostic.Spec.ShouldBe("\u00a76.2");
    }

    [Test]
    public void FailOnWarningDoesNotConsumeADetachedValue() =>
        ParseFail([.. Minimal, "--fail-on-warning", "true"]).Code.ShouldBe("CLI001");

    // ---- the end-of-options marker ------------------------------------------------------

    [Test]
    public void ABareDoubleHyphenHandsEveryFollowingTokenToThePrecedingListOption()
    {
        var line = ParseOk("-s", "scheme.txt", "-i", "a", "--", "-o", "--nonesuch", "--");

        line.Inputs.ShouldBe(["a", "-o", "--nonesuch", "--"]);
        line.OutputRoot.ShouldBe(".");
    }

    [Test]
    public void FailOnWarningAfterDoubleHyphenIsListData()
    {
        var line = ParseOk("-s", "scheme.txt", "-i", "a", "--", "--fail-on-warning");

        line.Inputs.ShouldBe(["a", "--fail-on-warning"]);
        line.FailOnWarning.ShouldBeFalse();
    }

    [Test]
    public void ABareDoubleHyphenNeedsNoPrecedingValueOnTheListOption() =>
        ParseOk("-s", "scheme.txt", "-i", "--", "a", "-o").Inputs.ShouldBe(["a", "-o"]);

    [Test]
    public void ABareDoubleHyphenAfterASingleValuedOptionIsRejected() =>
        ParseFail("-i", "a", "-s", "scheme.txt", "-o", "out", "--", "x").Code.ShouldBe("CLI001");

    [Test]
    public void ABareDoubleHyphenCannotSatisfyAPendingSingleValuedOption()
    {
        var diagnostic = ParseFail("-i", "a", "-s", "scheme.txt", "-o", "--", "x");

        diagnostic.Code.ShouldBe("CLI001");
        diagnostic.Spec.ShouldBe("\u00a76.2");
        diagnostic.Message.ShouldContain("'--output' requires a value");
        diagnostic.Message.ShouldContain("end-of-options marker");
    }

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

    /// <summary>
    /// Section 6.1 resolves informational modes before operational parsing. Each malformed form is
    /// therefore irrelevant when help or version appears before the end-of-options marker.
    /// </summary>
    [Test]
    public void InformationalModesBypassEveryMalformedOperationalForm()
    {
        string[][] malformed =
        [
            [],
            ["-i"],
            ["-s"],
            ["-i", "-s", "scheme.txt"],
            ["-s", "scheme.txt", "-i"],
            ["-i", "one", "-i", "-s", "scheme.txt"],
            ["-s", "one", "-s"],
            ["-i", "--"],
        ];

        foreach (var mode in new[] { "--help", "--version" })
        {
            foreach (var suffix in malformed)
            {
                DiagnosticsFormatPreScan.ResolveInformationalMode([mode, .. suffix])
                    .ShouldBe(mode == "--help" ? InformationalMode.Help : InformationalMode.Version);
            }
        }
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

    /// <summary>
    /// The catalog accessor used to print a default also identifies the property a parser handler
    /// must update. This catches both a missing limit switch arm and an arm that updates the wrong
    /// bound without maintaining a second option-name inventory in the tests.
    /// </summary>
    [Test]
    public void EveryCatalogedLimitHasTheMatchingParserHandler()
    {
        foreach (var option in CommandLineOptions.All.Where(option => option.Limit is not null))
        {
            var value = option.Limit!.Kind == ResourceLimitKind.Bytes ? "7KiB" : "7";
            var expected = option.Limit.Kind == ResourceLimitKind.Bytes ? 7L * 1024 : 7L;
            var parsed = ParseOk([.. Minimal, option.Name, value]);

            option.Limit.ReadValue(parsed.Limits).ShouldBe(
                expected,
                $"'{option.Name}' did not update the ResourceLimits member declared by its catalog entry");
        }
    }

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
    /// Section 6.2: "a value exceeding an implementation's documented hard safety ceiling is
    /// <c>CLI001</c>". Several phases walk the overlay tree by recursion, so a depth the pipeline
    /// cannot survive has to be refused where a refusal can still be reported — accepting it trades
    /// this diagnostic for a stack overflow, which Section 6.3 does not define and which cannot
    /// carry a diagnostic at all.
    /// </summary>
    /// <param name="value">A depth at or beyond the ceiling.</param>
    /// <param name="accepted">Whether Section 6.2 admits it.</param>
    [TestCase("4096", true)]
    [TestCase("4097", false)]
    [TestCase("100000", false)]
    [TestCase("9223372036854775807", false)]
    public void ADepthBeyondTheDocumentedCeilingIsRejected(string value, bool accepted)
    {
        if (accepted)
        {
            ParseOk([.. Minimal, "--max-depth", value]).Limits.MaxDepth
                .ShouldBe(LimitValue.MaxDepthCeiling);
            return;
        }

        var fault = ParseFail([.. Minimal, "--max-depth", value]);

        fault.Code.ShouldBe("CLI001");
        fault.Message.ShouldContain(LimitValue.MaxDepthCeiling.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The ceiling must leave the Section 6.2 default usable, and leave room above it: a ceiling
    /// equal to the default would make the option settable only downwards.
    /// </summary>
    [Test]
    public void TheCeilingIsAboveTheDefaultDepth() =>
        LimitValue.MaxDepthCeiling.ShouldBeGreaterThan(ResourceLimits.Defaults.MaxDepth);

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
