using Namespace2Xml.Gatekeeper;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

[TestFixture]
public class GateIdentityParserTests
{
    [TestCase(
        "nunit:Namespace2Xml.UnitTests::Namespace2Xml.UnitTests.PublisherTests.AStorageFailureIsReportedAgainstItsDestination")]
    [TestCase("ci:.github/workflows/ci.yml::differential")]
    [TestCase(
        "ci:.github/workflows/ci.yaml::determinism[enabled=true,os=\"windows-latest\",retries=3]")]
    public void CanonicalIdentitiesRoundTripExactly(string text)
    {
        GateIdentityRenderer.Render(GateIdentityParser.Parse(text)).ShouldBe(text);
    }

    [Test]
    public void JsonScalarTypesRemainDistinct()
    {
        var boolean = GateIdentityParser.Parse(
            "ci:.github/workflows/ci.yml::job[value=true]");
        var text = GateIdentityParser.Parse(
            "ci:.github/workflows/ci.yml::job[value=\"true\"]");

        boolean.ShouldNotBe(text);
        GateIdentityRenderer.Render(boolean).ShouldBe(
            "ci:.github/workflows/ci.yml::job[value=true]");
        GateIdentityRenderer.Render(text).ShouldBe(
            "ci:.github/workflows/ci.yml::job[value=\"true\"]");
    }

    [Test]
    public void JsonStringsMayContainIdentityDelimiters()
    {
        const string identity =
            "ci:.github/workflows/ci.yml::job[first=\"a,b=c]\",second=null]";

        GateIdentityRenderer.Render(GateIdentityParser.Parse(identity)).ShouldBe(identity);
    }

    [Test]
    public void LegacyAndNoncanonicalIdentitiesAreRejected()
    {
        string[] invalid =
        [
            "ci:determinism",
            "PublisherTests.SomeTest",
            "Namespace2Xml.UnitTests.PublisherTests.SomeTest",
            "ci:.github/workflows/ci.yml::job[]",
            "ci:.github/workflows/ci.yml::job[z=1,a=2]",
            "ci:.github/workflows/ci.yml::job[a=1,a=1]",
            "ci:.github/workflows/ci.yml::job[a=1.0]",
            "ci:.github\\workflows\\ci.yml::job",
            "ci:.github/workflows/../ci.yml::job",
            "ci:.github/workflows/ci.txt::job",
        ];

        foreach (var identity in invalid)
        {
            Should.Throw<FormatException>(() => GateIdentityParser.Parse(identity), identity);
        }
    }
}
