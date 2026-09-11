using System.Text.Json;
using Namespace2Xml.Gatekeeper;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

[TestFixture]
public sealed class AssertionGateCatalogTests
{
    [Test]
    public void OneAssertionOwnsOneListedFixtureArtifact()
    {
        using var manifest = TemporaryManifest(
            Item(
                "required",
                ["fixture"],
                [],
                [Assertion("claim", "fixture", "fixture", "expected/out.txt", "out.txt changes")]));

        var catalog = AssertionGateCatalog.Load(manifest.Path);

        catalog.Evidence.ShouldBe(
        [
            new AssertionEvidenceReference(
                1,
                "claim",
                AssertionEvidenceKind.Fixture,
                "fixture",
                "expected/out.txt",
                "out.txt changes"),
        ]);
    }

    [Test]
    public void OneAssertionOwnsOneListedGateObservation()
    {
        using var manifest = TemporaryManifest(
            Item(
                "required",
                [],
                [Gate],
                [Assertion("claim", "gate", Gate, "nunit-result", "count changes")]));

        var catalog = AssertionGateCatalog.Load(manifest.Path);

        catalog.Evidence.Single().ShouldBe(
            new AssertionEvidenceReference(
                1,
                "claim",
                AssertionEvidenceKind.Gate,
                Gate,
                "nunit-result",
                "count changes"));
    }

    [TestCase("legacy.md")]
    [TestCase("expected/")]
    [TestCase("expected/a/b.txt")]
    [TestCase("expected-diagnostics.json")]
    [TestCase("expected-stdout.txt")]
    public void EveryPortableFixtureArtifactIsAccepted(string artifact)
    {
        using var manifest = TemporaryManifest(
            Item(
                "required",
                ["fixture"],
                [],
                [Assertion("claim", "fixture", "fixture", artifact, "observable")]));

        Should.NotThrow(() => AssertionGateCatalog.Load(manifest.Path));
    }

    [TestCase("expected\\out.txt")]
    [TestCase("expected/../out.txt")]
    [TestCase("inputs/source.txt")]
    public void ANonoraclePathCannotBeFixtureEvidence(string artifact)
    {
        using var manifest = TemporaryManifest(
            Item(
                "required",
                ["fixture"],
                [],
                [Assertion("claim", "fixture", "fixture", artifact, "observable")]));

        Should.Throw<InvalidDataException>(() => AssertionGateCatalog.Load(manifest.Path))
            .Message.ShouldContain("invalid fixture artifact");
    }

    [Test]
    public void ExitStatusEvidenceRequiresAnExactValueAndSubstantiveOracle()
    {
        using var manifest = TemporaryManifest(
            Item(
                "required",
                ["fixture"],
                [],
                [
                    Assertion(
                        "status",
                        "fixture",
                        "fixture",
                        "expected-exit-code.txt",
                        "expected-exit-code.txt = 1."),
                    Assertion("diagnostic", "fixture", "fixture", "expected-diagnostics.json", "code changes"),
                ]));

        Should.NotThrow(() => AssertionGateCatalog.Load(manifest.Path));
    }

    [Test]
    public void ExitOnlyFixtureEvidenceFailsClosed()
    {
        using var manifest = TemporaryManifest(
            Item(
                "required",
                ["fixture"],
                [],
                [
                    Assertion(
                        "status",
                        "fixture",
                        "fixture",
                        "expected-exit-code.txt",
                        "expected-exit-code.txt = 1."),
                ]));

        Should.Throw<InvalidDataException>(() => AssertionGateCatalog.Load(manifest.Path))
            .Message.ShouldContain("without a substantive oracle assertion");
    }

    [Test]
    public void LegacyMetadataCannotBeTheSubstantiveCompanionForExitStatus()
    {
        using var manifest = TemporaryManifest(
            Item(
                "required",
                ["fixture"],
                [],
                [
                    Assertion(
                        "status",
                        "fixture",
                        "fixture",
                        "expected-exit-code.txt",
                        "expected-exit-code.txt = 1."),
                    Assertion(
                        "legacy difference",
                        "fixture",
                        "fixture",
                        "legacy.md",
                        "legacy verdict changes"),
                ]));

        Should.Throw<InvalidDataException>(() => AssertionGateCatalog.Load(manifest.Path))
            .Message.ShouldContain("without a substantive oracle assertion");
    }

    [Test]
    public void ExitStatusObservationMustStateTheExactExpectedValue()
    {
        using var manifest = TemporaryManifest(
            Item(
                "required",
                ["fixture"],
                [],
                [
                    Assertion(
                        "status",
                        "fixture",
                        "fixture",
                        "expected-exit-code.txt",
                        "the process fails"),
                    Assertion("diagnostic", "fixture", "fixture", "expected-diagnostics.json", "code changes"),
                ]));

        Should.Throw<InvalidDataException>(() => AssertionGateCatalog.Load(manifest.Path))
            .Message.ShouldContain("must state the exact expected-exit-code.txt value");
    }

    [TestCase("arbitrary prose")]
    [TestCase("ci-job-result")]
    public void NUnitGateArtifactsFailClosedUnlessTheyNameTheLeafResult(string artifact)
    {
        using var manifest = TemporaryManifest(
            Item("required", [], [Gate], [Assertion("claim", "gate", Gate, artifact, "field changes")]));

        Should.Throw<InvalidDataException>(() => AssertionGateCatalog.Load(manifest.Path))
            .Message.ShouldContain("must use artifact 'nunit-result'");
    }

    [Test]
    public void CiGateArtifactsMustNameTheJobResult()
    {
        const string ciGate = "ci:.github/workflows/ci.yml::build";
        using var manifest = TemporaryManifest(
            Item(
                "required",
                [],
                [ciGate],
                [Assertion("claim", "gate", ciGate, "nunit-result", "job fails")]));

        Should.Throw<InvalidDataException>(() => AssertionGateCatalog.Load(manifest.Path))
            .Message.ShouldContain("must use artifact 'ci-job-result'");
    }

    [Test]
    public void AnAssertionCannotNameAnUnlistedOwner()
    {
        using var manifest = TemporaryManifest(
            Item(
                "required",
                ["fixture"],
                [],
                [Assertion("claim", "fixture", "other", "expected/", "tree changes")]));

        Should.Throw<InvalidDataException>(() => AssertionGateCatalog.Load(manifest.Path))
            .Message.ShouldContain("unlisted fixture evidence 'other'");
    }

    [Test]
    public void EveryListedOwnerMustCarryAnAssertion()
    {
        using var manifest = TemporaryManifest(
            Item(
                "required",
                ["used", "unused"],
                [],
                [Assertion("claim", "fixture", "used", "expected/", "tree changes")]));

        Should.Throw<InvalidDataException>(() => AssertionGateCatalog.Load(manifest.Path))
            .Message.ShouldContain("fixtures [unused]");
    }

    [Test]
    public void AssertionAndEvidenceShapesAreClosed()
    {
        var assertion = Assertion("claim", "fixture", "fixture", "expected/", "tree changes");
        assertion["secondEvidence"] = new Dictionary<string, object?>();
        using var manifest = TemporaryManifest(
            Item("required", ["fixture"], [], [assertion]));

        Should.Throw<InvalidDataException>(() => AssertionGateCatalog.Load(manifest.Path))
            .Message.ShouldContain("expected [text, evidence]");
    }

    [Test]
    public void EvidenceShapeIsClosed()
    {
        var assertion = Assertion("claim", "fixture", "fixture", "expected/", "tree changes");
        ((Dictionary<string, object?>)assertion["evidence"]!)["extra"] = true;
        using var manifest = TemporaryManifest(
            Item("required", ["fixture"], [], [assertion]));

        Should.Throw<InvalidDataException>(() => AssertionGateCatalog.Load(manifest.Path))
            .Message.ShouldContain("expected [kind, name, artifact, observation]");
    }

    [Test]
    public void AssertionAndEvidencePropertyOrderIsCanonical()
    {
        var assertion = Assertion("claim", "fixture", "fixture", "expected/", "tree changes");
        var evidence = (Dictionary<string, object?>)assertion["evidence"]!;
        assertion = new Dictionary<string, object?>
        {
            ["evidence"] = evidence,
            ["text"] = "claim",
        };
        using var manifest = TemporaryManifest(
            Item("required", ["fixture"], [], [assertion]));

        Should.Throw<InvalidDataException>(() => AssertionGateCatalog.Load(manifest.Path))
            .Message.ShouldContain("properties are [evidence, text]");
    }

    [TestCase("text")]
    [TestCase("kind")]
    [TestCase("name")]
    [TestCase("artifact")]
    [TestCase("observation")]
    public void EveryAssertionEvidenceStringMustBeNonblank(string propertyName)
    {
        var assertion = Assertion("claim", "fixture", "fixture", "expected/", "tree changes");
        if (propertyName == "text")
        {
            assertion[propertyName] = " ";
        }
        else
        {
            ((Dictionary<string, object?>)assertion["evidence"]!)[propertyName] = " ";
        }

        using var manifest = TemporaryManifest(
            Item("required", ["fixture"], [], [assertion]));

        Should.Throw<InvalidDataException>(() => AssertionGateCatalog.Load(manifest.Path))
            .Message.ShouldContain($"no nonblank string {propertyName}");
    }

    [Test]
    public void DuplicateFixtureOrGateNamesAreRejected()
    {
        using var manifest = TemporaryManifest(
            Item(
                "required",
                ["fixture", "fixture"],
                [Gate, Gate],
                [
                    Assertion("fixture claim", "fixture", "fixture", "expected/", "tree changes"),
                    Assertion("gate claim", "gate", Gate, "nunit-result", "field changes"),
                ]));

        Should.Throw<InvalidDataException>(() => AssertionGateCatalog.Load(manifest.Path))
            .Message.ShouldContain("repeats fixtures value 'fixture'");
    }

    [Test]
    public void NonstringOwnerNamesAreRejectedAsManifestData()
    {
        var item = Item("pending", [], [], []);
        item["fixtures"] = new object?[] { 42 };
        using var manifest = TemporaryManifest(item);

        Should.Throw<InvalidDataException>(() => AssertionGateCatalog.Load(manifest.Path))
            .Message.ShouldContain("blank or non-string fixtures value");
    }

    [Test]
    public void PendingItemMayMapOnlyItsAlreadyAuthoredClaims()
    {
        using var manifest = TemporaryManifest(
            Item(
                "pending",
                ["fixture"],
                [],
                [Assertion("known claim", "fixture", "fixture", "expected/", "tree changes")]));

        Should.NotThrow(() => AssertionGateCatalog.Load(manifest.Path));
    }

    [Test]
    public void RequiredItemsMustDecomposeIntoAssertions()
    {
        using var manifest = TemporaryManifest(Item("required", [], [Gate], []));

        Should.Throw<InvalidDataException>(() => AssertionGateCatalog.Load(manifest.Path))
            .Message.ShouldContain("has no authored assertions");
    }

    [Test]
    public void DuplicateAssertionTextIsRejected()
    {
        var assertion = Assertion("claim", "gate", Gate, "nunit-result", "field changes");
        using var manifest = TemporaryManifest(
            Item("required", [], [Gate], [assertion, assertion]));

        Should.Throw<InvalidDataException>(() => AssertionGateCatalog.Load(manifest.Path))
            .Message.ShouldContain("repeats assertion text 'claim'");
    }

    private const string Gate =
        "nunit:Namespace2Xml.UnitTests::Namespace2Xml.UnitTests.ExampleTests.Leaf";

    private static Dictionary<string, object?> Item(
        string status,
        string[] fixtures,
        string[] gates,
        Dictionary<string, object?>[] assertions) =>
        new()
        {
            ["item"] = 1,
            ["status"] = status,
            ["assertions"] = assertions,
            ["fixtures"] = fixtures,
            ["gates"] = gates,
        };

    private static Dictionary<string, object?> Assertion(
        string text,
        string kind,
        string name,
        string artifact,
        string observation) =>
        new()
        {
            ["text"] = text,
            ["evidence"] = new Dictionary<string, object?>
            {
                ["kind"] = kind,
                ["name"] = name,
                ["artifact"] = artifact,
                ["observation"] = observation,
            },
        };

    private static TemporaryFile TemporaryManifest(Dictionary<string, object?> item) =>
        new(JsonSerializer.Serialize(new Dictionary<string, object?> { ["items"] = new[] { item } }));

    private sealed class TemporaryFile : IDisposable
    {
        internal TemporaryFile(string contents)
        {
            Path = System.IO.Path.GetTempFileName();
            File.WriteAllText(Path, contents);
        }

        internal string Path { get; }

        public void Dispose()
        {
            File.Delete(Path);
        }
    }
}
