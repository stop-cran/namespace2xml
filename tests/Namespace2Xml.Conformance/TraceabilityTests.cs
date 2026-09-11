using System.Text.Json;
using Namespace2Xml.Gatekeeper;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.Conformance;

/// <summary>
/// The traceability gate of specification Appendix C.5. Coverage grows by ratchet: an acceptance
/// item is promoted to <c>required</c> when the milestone that owns it merges, and from then on a
/// fixture or exact named gate must exist for it. Claiming coverage that does not exist would be
/// worse than none.
/// </summary>
[TestFixture]
public class TraceabilityTests
{
    private static JsonElement Manifest =>
        JsonDocument.Parse(File.ReadAllText(CorpusLayout.AssertionManifest)).RootElement;

    private static IReadOnlyList<JsonElement> Items =>
        Manifest.GetProperty("items").EnumerateArray().ToList();

    [Test]
    public void ManifestCoversExactlyTheSpecificationItems()
    {
        var fromSpecification = AcceptanceItemsFromSpecification();

        fromSpecification.ShouldNotBeEmpty();
        Items.Select(item => item.GetProperty("item").GetInt32()).ShouldBe(fromSpecification);
    }

    [Test]
    public void ManifestTextMatchesTheSpecification()
    {
        var text = File.ReadAllText(CorpusLayout.Specification).ReplaceLineEndings("\n");

        foreach (var item in Items)
        {
            // The manifest must quote the specification, never paraphrase it.
            text.ShouldContain(item.GetProperty("text").GetString()!, Case.Sensitive);
        }
    }

    [Test]
    public void EveryFixtureReferencesAtLeastOneValidItem()
    {
        var valid = Items.Select(item => item.GetProperty("item").GetInt32()).ToHashSet();

        foreach (var conformanceCase in ConformanceCase.Discover(CorpusLayout.Corpus))
        {
            var requirements = conformanceCase.Requirements;

            requirements.ShouldNotBeEmpty($"{conformanceCase.Name} references no acceptance item.");

            foreach (var requirement in requirements)
            {
                valid.ShouldContain(requirement, $"{conformanceCase.Name} references unknown item {requirement}.");
            }
        }
    }

    [Test]
    public void EveryRequiredItemHasFixtureOrGate()
    {
        var uncovered = Items
            .Where(item => item.GetProperty("status").GetString() == "required")
            .Where(item =>
                item.GetProperty("fixtures").GetArrayLength() == 0
                && (!item.TryGetProperty("gates", out var gates) || gates.GetArrayLength() == 0))
            .Select(item => item.GetProperty("item").GetInt32())
            .ToList();

        uncovered.ShouldBeEmpty(
            "acceptance items are marked required but name neither fixture nor gate evidence: "
            + string.Join(", ", uncovered));
    }

    /// <summary>
    /// Appendix C.5: every authored assertion owns exactly one listed fixture artifact or one exact
    /// executable-gate observation, and every listed evidence owner carries at least one assertion.
    /// The shared manifest reader enforces the closed object shapes, exact owner names, artifact
    /// grammar, uniqueness, and the required-item nonempty assertion set.
    /// </summary>
    [Test]
    public void EveryAssertionMapsToExactlyOneObservableEvidenceOwner()
    {
        var catalog = AssertionGateCatalog.Load(CorpusLayout.AssertionManifest);

        catalog.Evidence.ShouldNotBeEmpty();
        catalog.Evidence.Select(reference => reference.Item).Distinct().ShouldBe(
            Items.Select(item => item.GetProperty("item").GetInt32()));
    }

    [Test]
    public void EveryFixtureAssertionNamesADeclaredOracleArtifact()
    {
        var catalog = AssertionGateCatalog.Load(CorpusLayout.AssertionManifest);

        foreach (var reference in catalog.Evidence
                     .Where(reference => reference.Kind == AssertionEvidenceKind.Fixture))
        {
            var fixture = Path.Combine(CorpusLayout.Corpus, reference.Name);
            var artifact = reference.Artifact switch
            {
                "expected/" => Path.Combine(fixture, "expected"),
                "expected-diagnostics.json" => Path.Combine(fixture, "expected-diagnostics.json"),
                "expected-exit-code.txt" => Path.Combine(fixture, "expected-exit-code.txt"),
                "expected-stdout.txt" => Path.Combine(fixture, "expected-stdout.txt"),
                "legacy.md" => Path.Combine(fixture, "legacy.md"),
                _ => Path.Combine(
                    fixture,
                    reference.Artifact.Replace('/', Path.DirectorySeparatorChar)),
            };

            // Appendix C.3 makes an absent expected/ directory the complete empty-tree oracle.
            // Git cannot carry an empty directory, so that artifact is declared by the fixture
            // itself; every file-backed oracle must still exist at its exact path.
            var exists = reference.Artifact == "expected/"
                ? Directory.Exists(fixture)
                : File.Exists(artifact);

            exists.ShouldBeTrue(
                $"item {reference.Item} assertion '{reference.Assertion}' names missing fixture "
                + $"artifact '{reference.Name}/{reference.Artifact}'.");
        }
    }

    /// <summary>
    /// Appendix C.5: an item's manifest entry must name exactly the fixtures that reference it.
    /// Coverage stated in one place is a number in a text file; stated in two, a claim cannot be
    /// added, dropped, or retargeted without the manifest being re-authored and reviewed.
    /// <para>
    /// This holds for every item, not only the required ones. Restricting it to required items let
    /// six pending items accumulate fixtures the manifest never learned about, so the manifest
    /// understated coverage precisely where coverage was still being built and most needed reading.
    /// </para>
    /// </summary>
    [Test]
    public void AnItemNamesExactlyTheFixturesThatClaimIt()
    {
        var cases = ConformanceCase.Discover(CorpusLayout.Corpus).ToList();

        foreach (var item in Items)
        {
            var number = item.GetProperty("item").GetInt32();

            var claiming = cases
                .Where(conformanceCase => conformanceCase.Requirements.Contains(number))
                .Select(conformanceCase => conformanceCase.Name)
                .Order(StringComparer.Ordinal)
                .ToList();

            var named = item.GetProperty("fixtures").EnumerateArray()
                .Select(fixture => fixture.GetString()!)
                .Order(StringComparer.Ordinal)
                .ToList();

            named.ShouldBe(
                claiming,
                $"item {number} names fixtures [{string.Join(", ", named)}] but is claimed by "
                + $"[{string.Join(", ", claiming)}].");
        }
    }

    /// <summary>
    /// Appendix C.5: a fixture claiming a required item must assert more than its exit code.
    /// Declaring the empty array counts, because Appendix C.4 distinguishes it from writing no
    /// stream at all; declaring nothing at all does not.
    /// </summary>
    [Test]
    public void AFixtureClaimingARequiredItemAssertsMoreThanAnExitCode()
    {
        var required = Required().Select(item => item.GetProperty("item").GetInt32()).ToHashSet();

        foreach (var conformanceCase in ConformanceCase.Discover(CorpusLayout.Corpus))
        {
            if (!conformanceCase.Requirements.Any(required.Contains))
            {
                continue;
            }

            Asserts(conformanceCase).ShouldBeTrue(
                $"{conformanceCase.Name} claims a required item but declares no expected tree, no "
                + "expected standard output, and no diagnostic stream.");
        }
    }

    private static bool Asserts(ConformanceCase conformanceCase) =>
        conformanceCase.ExpectedTree is not null
        || File.Exists(conformanceCase.ExpectedStandardOutput)
        || conformanceCase.ExpectedDiagnostics is not null;

    /// <summary>
    /// Appendix C.5: a gate named in the manifest must resolve to an exact built NUnit leaf or an
    /// exact workflow path, stable job ID, and complete matrix expansion.
    /// <para>
    /// Without this the field would discharge an acceptance item by writing a plausible name into a
    /// file, which is the exact failure the appendix exists to prevent. Test discovery therefore
    /// comes from the two built assemblies through the pinned adapter, never from source text.
    /// </para>
    /// </summary>
    [Test]
    public void EveryNamedGateResolvesToAnExactCatalogIdentity()
    {
        var assertions = AssertionGateCatalog.Load(CorpusLayout.AssertionManifest);
        var tests = NUnitTestDiscovery.Discover(
            CorpusLayout.Root,
            typeof(TraceabilityTests).Assembly.Location);
        var workflows = WorkflowCatalog.Load(CorpusLayout.Root);

        tests.Assemblies.Keys.Order(StringComparer.Ordinal).ShouldBe(
        [
            "Namespace2Xml.Conformance",
            "Namespace2Xml.UnitTests",
        ]);
        tests.Contains(
            "Namespace2Xml.Conformance",
            typeof(TraceabilityTests).FullName
            + "."
            + nameof(EveryNamedGateResolvesToAnExactCatalogIdentity)).ShouldBeTrue(
                "built NUnit discovery does not contain the traceability sentinel leaf.");

        StaticGateValidator.Validate(assertions.References, tests, workflows);
    }

    /// <summary>
    /// Appendix C.5: every acceptance item is discharged by a fixture or by a gate, and an item that
    /// has neither must say so in the manifest.
    /// <para>
    /// <see cref="EveryRequiredItemHasFixtureOrGate"/> only inspects items already promoted to
    /// <c>required</c>, so an item that is still <c>pending</c> could carry no evidence at all and no
    /// test would notice. That is the state the corpus was in for most of its life, and it is
    /// invisible precisely when it matters — while the corpus is being filled in. This gate makes the
    /// uncovered set explicit: an item may be uncovered, but only out loud.
    /// </para>
    /// <para>
    /// The check is two-way. Leaving an item uncovered without a reason fails, and so does writing a
    /// reason for an item that has since been covered, so a stale exemption cannot outlive the gap it
    /// described.
    /// </para>
    /// </summary>
    [Test]
    public void EveryItemHasEvidenceOrAnExplicitPendingGap()
    {
        foreach (var item in Items)
        {
            var number = item.GetProperty("item").GetInt32();
            var status = item.GetProperty("status").GetString();
            var hasFixtures = item.GetProperty("fixtures").GetArrayLength() > 0;
            var hasGates = item.TryGetProperty("gates", out var gates)
                && gates.GetArrayLength() > 0;

            var argued = item.TryGetProperty("whyNotAFixture", out var why)
                && !string.IsNullOrWhiteSpace(why.GetString());

            if (!hasFixtures && !hasGates && status == "pending")
            {
                argued.ShouldBeTrue(
                    $"acceptance item {number} names no fixture and no gate, and does not say why. "
                    + "An item may be uncovered, but Appendix C.5 requires the gap to be argued in "
                    + "the manifest so that it is visible rather than merely absent.");
            }
            else if (hasFixtures)
            {
                argued.ShouldBeFalse(
                    $"acceptance item {number} names a fixture and still carries whyNotAFixture. "
                    + "An exemption that outlives the gap it described is worse than none, because "
                    + "it reports a hole that has been filled.");
            }
            else if (hasGates)
            {
                argued.ShouldBeTrue(
                    $"acceptance item {number} names only gate evidence but does not say why a "
                    + "fixture cannot discharge it. Appendix C.5 requires the exemption to be "
                    + "argued rather than assumed.");
            }
        }
    }

    private static IEnumerable<JsonElement> Required() =>
        Items.Where(item => item.GetProperty("status").GetString() == "required");

    [Test]
    public void EveryItemDeclaresAKnownStatus()
    {
        var statuses = Manifest.GetProperty("statuses").EnumerateObject()
            .Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var item in Items)
        {
            statuses.ShouldContain(item.GetProperty("status").GetString()!);
        }
    }

    private static List<int> AcceptanceItemsFromSpecification()
    {
        var numbers = new List<int>();
        var inSection = false;

        foreach (var line in File.ReadLines(CorpusLayout.Specification))
        {
            if (line.StartsWith("## 26. Acceptance requirements", StringComparison.Ordinal))
            {
                inSection = true;
                continue;
            }

            if (inSection && line.StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }

            if (inSection && line.Length > 0 && char.IsAsciiDigit(line[0]))
            {
                var dot = line.IndexOf('.');

                if (dot > 0 && int.TryParse(line[..dot], out var number))
                {
                    numbers.Add(number);
                }
            }
        }

        return numbers;
    }
}
