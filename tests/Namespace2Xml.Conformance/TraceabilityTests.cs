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
    /// Appendix C.5: a required item's manifest entry must name exactly the fixtures that reference
    /// it. Coverage stated in one place is a number in a text file; stated in two, a claim cannot be
    /// added, dropped, or retargeted without the manifest being re-authored and reviewed.
    /// </summary>
    [Test]
    public void ARequiredItemNamesExactlyTheFixturesThatClaimIt()
    {
        var cases = ConformanceCase.Discover(CorpusLayout.Corpus).ToList();

        foreach (var item in Required())
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
