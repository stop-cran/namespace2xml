using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Specification section 6.4.1. The pre-scan must be total: every argument vector resolves
/// exactly one encoding and no vector makes it throw, because it runs before validation.
/// </summary>
[TestFixture]
public class DiagnosticsFormatPreScanTests
{
    [Test]
    public void DefaultsToTextWhenAbsent() =>
        DiagnosticsFormatPreScan.Resolve(["-i", "a.properties"]).ShouldBe(DiagnosticFormat.Text);

    [TestCase("json")]
    [TestCase("JSON")]
    [TestCase("Json")]
    public void ResolvesJsonCaseInsensitively(string value) =>
        DiagnosticsFormatPreScan.Resolve(["--diagnostics-format", value]).ShouldBe(DiagnosticFormat.Json);

    [Test]
    public void ResolvesInlineForm() =>
        DiagnosticsFormatPreScan.Resolve(["--diagnostics-format=json"]).ShouldBe(DiagnosticFormat.Json);

    [Test]
    public void ResolvesEmptyInlineValueAsText() =>
        DiagnosticsFormatPreScan.Resolve(["--diagnostics-format="]).ShouldBe(DiagnosticFormat.Text);

    [Test]
    public void LastOccurrenceWins()
    {
        DiagnosticsFormatPreScan.Resolve(["--diagnostics-format", "json", "--diagnostics-format", "text"])
            .ShouldBe(DiagnosticFormat.Text);

        DiagnosticsFormatPreScan.Resolve(["--diagnostics-format=text", "--diagnostics-format", "json"])
            .ShouldBe(DiagnosticFormat.Json);
    }

    [Test]
    public void StopsAtBareDoubleDash() =>
        DiagnosticsFormatPreScan.Resolve(["-i", "--", "--diagnostics-format", "json"])
            .ShouldBe(DiagnosticFormat.Text);

    [Test]
    public void TrailingOptionWithoutValueResolvesToText() =>
        DiagnosticsFormatPreScan.Resolve(["--diagnostics-format"]).ShouldBe(DiagnosticFormat.Text);

    [Test]
    public void OptionFollowedByDoubleDashContributesNoValue() =>
        DiagnosticsFormatPreScan.Resolve(["--diagnostics-format", "--", "json"])
            .ShouldBe(DiagnosticFormat.Text);

    [Test]
    public void ValueIsTakenVerbatimEvenWhenItLooksLikeAnOption() =>
        // The value is consumed, so the token that follows is not rescanned as an option.
        DiagnosticsFormatPreScan.Resolve(["--diagnostics-format", "--diagnostics-format=json"])
            .ShouldBe(DiagnosticFormat.Text);

    [Test]
    public void UnrecognizedValueResolvesToText() =>
        DiagnosticsFormatPreScan.Resolve(["--diagnostics-format", "jsonn"]).ShouldBe(DiagnosticFormat.Text);

    [Test]
    public void NonAsciiCaseFoldingIsNotApplied() =>
        // Turkish dotless forms must not fold to ASCII, or the result would depend on locale.
        DiagnosticsFormatPreScan.Resolve(["--diagnostics-format", "JSOＮ"]).ShouldBe(DiagnosticFormat.Text);

    [Test]
    public void EmptyVectorIsTotal() =>
        DiagnosticsFormatPreScan.Resolve([]).ShouldBe(DiagnosticFormat.Text);

    [Test]
    public void HelpOutranksVersion() =>
        DiagnosticsFormatPreScan.ResolveInformationalMode(["--version", "--help"])
            .ShouldBe(InformationalMode.Help);

    [Test]
    public void InformationalModeStopsAtBareDoubleDash() =>
        DiagnosticsFormatPreScan.ResolveInformationalMode(["-i", "--", "--help"])
            .ShouldBe(InformationalMode.None);

    /// <summary>
    /// Section 6.1: the presence scan "applies no other part of the grammar; in particular it does
    /// not work out which tokens are option values". Treating this <c>--version</c> as a value
    /// would contradict the Section 6.2 rule that a detached value may not be an option token —
    /// which is exactly what the parser reports when it reaches the same tokens.
    /// </summary>
    [Test]
    public void AnOptionTokenInValuePositionStillSelectsTheInformationalMode() =>
        DiagnosticsFormatPreScan.ResolveInformationalMode(["--diagnostics-format", "--version"])
            .ShouldBe(InformationalMode.Version);

    [Test]
    public void HelpInValuePositionStillOutranksEverything() =>
        DiagnosticsFormatPreScan.ResolveInformationalMode(["--diagnostics-format", "--help"])
            .ShouldBe(InformationalMode.Help);

    /// <summary>
    /// Section 6.2 makes the inline form uniform, and Section 6.1 decides the mode from presence,
    /// so an inline value is ignored rather than turning the token into something else.
    /// </summary>
    [TestCase("--help", InformationalMode.Help)]
    [TestCase("--help=", InformationalMode.Help)]
    [TestCase("--help=anything", InformationalMode.Help)]
    [TestCase("--version", InformationalMode.Version)]
    [TestCase("--version=", InformationalMode.Version)]
    [TestCase("--version=anything", InformationalMode.Version)]
    public void InformationalModeRecognizesTheInlineForm(string token, InformationalMode expected) =>
        DiagnosticsFormatPreScan.ResolveInformationalMode([token]).ShouldBe(expected);

    /// <summary>
    /// The inline form carries its own value, so it must not additionally swallow the next token
    /// the way the detached form does.
    /// </summary>
    [Test]
    public void AnInlineDiagnosticsFormatDoesNotConsumeTheFollowingToken() =>
        DiagnosticsFormatPreScan.ResolveInformationalMode(["--diagnostics-format=json", "--version"])
            .ShouldBe(InformationalMode.Version);

    [Test]
    public void InformationalModeToleratesANullToken() =>
        DiagnosticsFormatPreScan.ResolveInformationalMode([null!, "--help"])
            .ShouldBe(InformationalMode.Help);
}
