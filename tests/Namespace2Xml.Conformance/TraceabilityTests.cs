using System.Text.Json;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.Conformance;

/// <summary>
/// The traceability gate of specification Appendix C.5. Coverage grows by ratchet: an acceptance
/// item is promoted to <c>required</c> when the milestone that owns it merges, and from then on a
/// fixture must exist for it. Claiming coverage that does not exist would be worse than none.
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
    public void EveryRequiredItemHasAFixture()
    {
        var covered = ConformanceCase.Discover(CorpusLayout.Corpus)
            .SelectMany(conformanceCase => conformanceCase.Requirements)
            .ToHashSet();

        var uncovered = Items
            .Where(item => item.GetProperty("status").GetString() == "required")
            .Select(item => item.GetProperty("item").GetInt32())
            .Where(item => !covered.Contains(item))
            .ToList();

        uncovered.ShouldBeEmpty(
            "acceptance items are marked required but have no fixture: " + string.Join(", ", uncovered));
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
    /// Appendix C.5: a gate named in the manifest must resolve to something that exists — a declared
    /// test, or a job defined in the continuous-integration workflow.
    /// <para>
    /// Without this the field would discharge an acceptance item by writing a plausible name into a
    /// file, which is the exact failure the appendix exists to prevent. A gate is checked by finding
    /// the name in the sources rather than by reflection, because the tests it names live in an
    /// assembly this one does not reference and should not have to.
    /// </para>
    /// </summary>
    [Test]
    public void EveryNamedGateResolvesToSomethingThatExists()
    {
        var testSources = Directory
            .EnumerateFiles(Path.Combine(CorpusLayout.Root, "tests"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToList();

        var workflows = Directory
            .EnumerateFiles(
                Path.Combine(CorpusLayout.Root, ".github", "workflows"),
                "*.yml",
                SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToList();

        foreach (var item in Items)
        {
            if (!item.TryGetProperty("gates", out var gates))
            {
                continue;
            }

            var number = item.GetProperty("item").GetInt32();

            var hasFixtures = item.GetProperty("fixtures").GetArrayLength() > 0;

            if (!hasFixtures)
            {
                item.TryGetProperty("whyNotAFixture", out var why).ShouldBeTrue(
                    $"item {number} is discharged by gates alone but does not say why a fixture "
                    + "cannot discharge it. Appendix C.5 requires the exemption to be argued "
                    + "rather than assumed.");

                why.GetString().ShouldNotBeNullOrWhiteSpace();
            }

            foreach (var gate in gates.EnumerateArray())
            {
                var name = gate.GetString()!;

                var resolved = name.StartsWith("ci:", StringComparison.Ordinal)
                    ? workflows.Any(workflow =>
                        workflow.Contains("\n  " + name[3..] + ":", StringComparison.Ordinal))
                    : testSources.Any(source =>
                        source.Contains(LastSegment(name) + "(", StringComparison.Ordinal));

                resolved.ShouldBeTrue(
                    $"item {number} names the gate '{name}', which does not resolve to a declared "
                    + "test or a job in .github/workflows. A gate that names nothing discharges "
                    + "nothing.");
            }
        }
    }

    private static string LastSegment(string name)
    {
        var dot = name.LastIndexOf('.');

        return dot < 0 ? name : name[(dot + 1)..];
    }

    /// <summary>
    /// Appendix C.5: every acceptance item is discharged by a fixture or by a gate, and an item that
    /// has neither must say so in the manifest.
    /// <para>
    /// <see cref="EveryRequiredItemHasAFixture"/> only inspects items already promoted to
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
    public void EveryItemIsDischargedByAFixtureAGateOrAnArgument()
    {
        foreach (var item in Items)
        {
            var number = item.GetProperty("item").GetInt32();
            var covered = item.GetProperty("fixtures").GetArrayLength() > 0
                || (item.TryGetProperty("gates", out var gates) && gates.GetArrayLength() > 0);

            var argued = item.TryGetProperty("whyNotAFixture", out var why)
                && !string.IsNullOrWhiteSpace(why.GetString());

            if (!covered)
            {
                argued.ShouldBeTrue(
                    $"acceptance item {number} names no fixture and no gate, and does not say why. "
                    + "An item may be uncovered, but Appendix C.5 requires the gap to be argued in "
                    + "the manifest so that it is visible rather than merely absent.");
            }
            else if (item.GetProperty("fixtures").GetArrayLength() > 0)
            {
                argued.ShouldBeFalse(
                    $"acceptance item {number} names a fixture and still carries whyNotAFixture. "
                    + "An exemption that outlives the gap it described is worse than none, because "
                    + "it reports a hole that has been filled.");
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
