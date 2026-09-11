using Namespace2Xml.Budgets;
using Namespace2Xml.Cli;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Pipeline.Steps;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

[TestFixture]
public sealed class SchemePhaseTests
{
    [Test]
    public void AFailingSchemeSourceExposesNoPartialDirectiveProduct()
    {
        var parsed = CommandLineParser.Parse(
            [
                "-i", "unused.txt",
                "-s", "mixed.json",
                "-s", "other.json",
            ]).CommandLine.ShouldNotBeNull();
        var sources = new TransformationTests.Sources(
            ("mixed.json", "{\"app\":{\"output\":\"json\",\"filename\":{}}}\n"),
            ("other.json", "\"not-a-mapping\"\n"));
        var diagnostics = new DiagnosticBuffer();
        var outcome = SchemePhase.ParseSchemes(
            parsed,
            new SourceLoader(sources, ResourceLimits.Defaults),
            new GlobalBudget(ResourceLimits.Defaults),
            diagnostics);

        outcome.Faulted.ShouldBeTrue();
        outcome.Value.IsDefault.ShouldBeTrue();

        var observed = diagnostics.Drain();
        observed.Select(entry => entry.Code).ShouldBe(["SCHEME001", "SCHEME003"]);
        observed.Select(entry => entry.Source).ShouldBe(["mixed.json", "other.json"]);
        observed[0].Declaration.ShouldBe("app.filename");
    }
}
