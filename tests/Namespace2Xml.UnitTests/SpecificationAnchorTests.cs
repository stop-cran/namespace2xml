using System.Text.Json;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Specification section 22: "Pipeline phase and specification anchor are properties of the
/// individual occurrence", so every anchor the library emits is a claim about where in the
/// contract a condition is defined.
/// </summary>
/// <remarks>
/// <para>
/// Nothing else checks that claim. <see cref="DiagnosticConstructionTests"/> constrains an
/// anchor's <em>shape</em>, and the registry is explicitly "not authoritative for phase, anchor,
/// or message prose", so an anchor naming a section that does not exist reaches a consumer of
/// <c>--diagnostics-format json</c> unchallenged.
/// </para>
/// <para>
/// This gate is deliberately structural rather than a table of expected anchors. The navigation
/// generator is the only parser for specification clause headings; its generated manifest lets
/// this test verify the emitted C# values and rendered targets without maintaining a second,
/// subtly different heading grammar.
/// </para>
/// <para>
/// It does not, and cannot mechanically, check that an anchor names the <em>right</em> clause —
/// <c>LIMIT001</c> once shipped anchored at section 25, "Backward-compatibility examples", which
/// exists. Only reading the cited text catches that. The gate closes the weaker hole so that the
/// reading has less to cover.
/// </para>
/// </remarks>
[TestFixture]
public class SpecificationAnchorTests
{
    /// <summary>An anchor as the library spells one: the section sign, then a section number.</summary>
    private static readonly Regex EmittedAnchor = new(
        @"\\u00A7(?<section>(?:\d+(?:\.\d+)*|[A-Z](?:\.\d+)*))",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static IReadOnlyList<Clause> Clauses { get; } = LoadClauses();

    private static HashSet<string> Sections { get; } = Clauses
        .Select(clause => clause.Section)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Every section that has at least one numbered subdivision, derived from the headings rather
    /// than listed, so an amendment that subdivides a section brings its anchors into question.
    /// </summary>
    private static HashSet<string> Subdivided { get; } = Sections
        .Where(section => section.Contains('.', StringComparison.Ordinal))
        .Select(section => section[..section.LastIndexOf('.')])
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Sections anchored at section level although they are subdivided, each because the rule is
    /// stated in the section's own preamble: section 15 enumerates the recognized directives and
    /// declares an unknown one a blocking error before section 15.1 begins.
    /// </summary>
    private static HashSet<string> PreambleRules { get; } = new(StringComparer.Ordinal) { "15" };

    private sealed record Clause(string Section, string Anchor, int Level, string Heading);

    private sealed record NavigationManifest(string GeneratedBy, IReadOnlyList<Clause> Clauses);

    private static IReadOnlyList<Clause> LoadClauses()
    {
        var manifest = JsonSerializer.Deserialize<NavigationManifest>(
            File.ReadAllText(RepositoryLayout.SpecificationNavigation),
            JsonOptions);

        manifest.ShouldNotBeNull("the specification-navigation manifest must be valid JSON");
        manifest.GeneratedBy.ShouldBe("tools/sync-specification-navigation.ps1");
        return manifest.Clauses;
    }

    private static IEnumerable<TestCaseData> Anchors()
    {
        var source = Path.Combine(RepositoryLayout.Root, "src");

        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            foreach (var section in EmittedAnchor
                .Matches(File.ReadAllText(file))
                .Select(match => match.Groups["section"].Value)
                .Distinct(StringComparer.Ordinal))
            {
                yield return new TestCaseData(section, Path.GetFileName(file))
                    .SetArgDisplayNames($"\u00A7{section} in {Path.GetFileName(file)}");
            }
        }
    }

    /// <summary>
    /// Every anchor the library can emit names a section the specification actually has.
    /// </summary>
    /// <param name="section">The section number the anchor names.</param>
    /// <param name="file">The file that emits it, named so a failure says where to look.</param>
    [TestCaseSource(nameof(Anchors))]
    public void AnEmittedAnchorNamesASectionOfTheSpecification(string section, string file) =>
        Sections.ShouldContain(
            section,
            $"{file} emits the anchor \u00A7{section}, which is not a heading in docs/specification.md");

    /// <summary>
    /// The gate is worthless if the generated model silently omits a clause class, so pin a few
    /// sections spanning decimals, nested decimals, appendices, and appendix subsections.
    /// </summary>
    [TestCase("22")]
    [TestCase("7.3")]
    [TestCase("23")]
    [TestCase("B")]
    [TestCase("C.4")]
    public void TheGeneratedNavigationContainsRepresentativeClauses(string section) =>
        Sections.ShouldContain(section);

    /// <summary>
    /// A section the specification does not define must not resolve, or the gate above would
    /// accept anything.
    /// </summary>
    [TestCase("99")]
    [TestCase("23.99")]
    public void AnInventedSectionDoesNotResolve(string section) => Sections.ShouldNotContain(section);

    /// <summary>
    /// Section 22 requires an anchor to name the rule's clause "at the deepest numbering the
    /// specification gives that statement", so an anchor that stops at section level while the
    /// section is subdivided is a claim that the rule is stated in the preamble.
    /// </summary>
    /// <remarks>
    /// This is the mechanical half of the section 22 rule. The other half — that the clause states
    /// the rule rather than citing it — needs a reader. Reaching for the section number because
    /// finding the subsection is work is the failure this catches, and it is the likely one.
    /// </remarks>
    /// <param name="section">The section number the anchor names.</param>
    /// <param name="file">The file that emits it, named so a failure says where to look.</param>
    [TestCaseSource(nameof(Anchors))]
    public void ASectionLevelAnchorNamesAnUndividedSectionOrAPreambleRule(string section, string file)
    {
        if (section.Contains('.', StringComparison.Ordinal) || !Subdivided.Contains(section))
        {
            return;
        }

        PreambleRules.ShouldContain(
            section,
            $"{file} emits the anchor \u00A7{section}, but section {section} is subdivided; "
                + "name the subsection that states the rule, or record here why the preamble does");
    }

    /// <summary>
    /// The gate above is vacuous unless the derivation finds subdivisions, so pin one section that
    /// has them and one that does not.
    /// </summary>
    [TestCase("16", true)]
    [TestCase("20", false)]
    public void TheSubdivisionsOfASectionAreFound(string section, bool subdivided) =>
        Subdivided.Contains(section).ShouldBe(subdivided);

    /// <summary>
    /// Every manifest entry has exactly one explicit target and one contents link, and every
    /// reserved target in the specification belongs to the manifest.
    /// </summary>
    [Test]
    public void TheGeneratedNavigationIsOneToOneWithTheSpecification()
    {
        var specification = File.ReadAllText(RepositoryLayout.Specification);

        Clauses.Select(clause => clause.Section).ShouldBeUnique();
        Clauses.Select(clause => clause.Anchor).ShouldBeUnique();

        foreach (var clause in Clauses)
        {
            Regex.Count(
                    specification,
                    Regex.Escape($"<a id=\"{clause.Anchor}\"></a>"),
                    RegexOptions.CultureInvariant)
                .ShouldBe(1, $"{clause.Section} must have exactly one explicit target");
            Regex.Count(
                    specification,
                    Regex.Escape($"](#{clause.Anchor})"),
                    RegexOptions.CultureInvariant)
                .ShouldBe(1, $"{clause.Section} must have exactly one contents entry");
        }

        var renderedAnchors = Regex.Matches(
                specification,
                "<a id=\"(?<anchor>spec-[a-z0-9-]+)\"></a>",
                RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant)
            .Select(match => match.Groups["anchor"].Value)
            .ToArray();
        renderedAnchors.ShouldBe(Clauses.Select(clause => clause.Anchor));
    }

    [TestCase("6", "spec-6")]
    [TestCase("6.4.3", "spec-6-4-3")]
    [TestCase("A", "spec-a")]
    [TestCase("C.4", "spec-c-4")]
    public void RepresentativeClausesUseStableTargets(string section, string target) =>
        Clauses.Single(clause => clause.Section == section).Anchor.ShouldBe(target);

    [Test]
    public void UnnumberedCapitalizedProseIsNotAClause() =>
        Clauses.ShouldNotContain(clause => clause.Heading == "A type that names a format");
}
