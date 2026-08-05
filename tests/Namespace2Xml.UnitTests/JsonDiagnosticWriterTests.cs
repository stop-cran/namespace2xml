using Namespace2Xml.Diagnostics;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Specification section 6.4.3 fixes the byte layout of the JSON diagnostic stream so that it
/// satisfies the section 24 byte-identity requirement. These assertions are on bytes, not on a
/// parsed model, because a parsed model would not catch a framing regression.
/// </summary>
[TestFixture]
public class JsonDiagnosticWriterTests
{
    private static string Render(params Diagnostic[] diagnostics) =>
        System.Text.Encoding.UTF8.GetString(JsonDiagnosticWriter.Render(diagnostics));

    private static Diagnostic Sample(
        string code = "CLI001",
        string message = "example",
        string? source = null,
        int? line = null,
        int? column = null) => new(
        code,
        DiagnosticSeverity.Error,
        DiagnosticPhase.Cli,
        "§6.4.1",
        message,
        source,
        line,
        column);

    [Test]
    public void EmptyStreamIsExactlyThreeBytes()
    {
        var bytes = JsonDiagnosticWriter.Render([]);

        bytes.ShouldBe("[]\n"u8.ToArray());
    }

    [Test]
    public void SingleElementUsesTheNormativeFraming() =>
        Render(Sample()).ShouldBe(
            "[\n{\"code\":\"CLI001\",\"severity\":\"error\",\"phase\":\"cli\",\"spec\":\"§6.4.1\"," +
            "\"message\":\"example\"}\n]\n");

    [Test]
    public void ElementsAreSeparatedByCommaAndNewline() =>
        Render(Sample(), Sample("PARSE001")).ShouldContain("},\n{");

    [Test]
    public void MembersAppearInNormativeOrder()
    {
        var rendered = Render(new Diagnostic(
            "TYPE001",
            DiagnosticSeverity.Error,
            DiagnosticPhase.Input,
            "§16.6",
            "prose",
            source: "inputs/example.properties",
            line: 3,
            column: 1,
            path: "a.b",
            declaration: "a.b.type=multiline",
            rule: "a.*",
            destination: "output.json"));
        var expectedOrder = new[]
        {
            "\"code\"", "\"severity\"", "\"phase\"", "\"source\"", "\"line\"", "\"column\"",
            "\"path\"", "\"declaration\"", "\"rule\"", "\"destination\"", "\"spec\"", "\"message\"",
        };

        var positions = expectedOrder.Select(member => rendered.IndexOf(member, StringComparison.Ordinal)).ToArray();

        positions.ShouldAllBe(position => position >= 0);
        positions.ShouldBe(positions.OrderBy(position => position).ToArray());
    }

    [Test]
    public void OmittedMembersAreAbsentRatherThanNull() =>
        Render(Sample()).ShouldNotContain("null");

    [Test]
    public void IntegersCarryNoQuotesSignOrPadding() =>
        Render(Sample(source: "a.properties", line: 7, column: 12))
            .ShouldContain("\"line\":7,\"column\":12");

    [Test]
    public void ControlCharactersUseShortEscapesThenLowercaseHex() =>
        Render(Sample(message: "a\tb\nc\u0001d\"e\\f"))
            .ShouldContain("\"message\":\"a\\tb\\nc\\u0001d\\\"e\\\\f\"");

    [Test]
    public void NonAsciiIsEmittedLiterallyAsUtf8()
    {
        var bytes = JsonDiagnosticWriter.Render([Sample(message: "проверка")]);

        System.Text.Encoding.UTF8.GetString(bytes).ShouldContain("проверка");
        bytes.ShouldNotContain((byte)'u');
    }

    [Test]
    public void StreamHasNoByteOrderMark()
    {
        var bytes = JsonDiagnosticWriter.Render([Sample()]);

        bytes[0].ShouldBe((byte)'[');
    }

    [Test]
    public void EveryPhaseAndSeverityHasAWireName()
    {
        foreach (var phase in Enum.GetValues<DiagnosticPhase>())
        {
            foreach (var severity in Enum.GetValues<DiagnosticSeverity>())
            {
                Should.NotThrow(() => JsonDiagnosticWriter.Render(
                    [new Diagnostic("WARN001", severity, phase, "§22", "m")]));
            }
        }
    }
}
