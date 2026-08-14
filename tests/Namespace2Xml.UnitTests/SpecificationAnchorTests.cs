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
/// This gate is deliberately structural rather than a table of expected anchors: a table would
/// restate the source it is checking and pass whatever the source happens to say. Resolving each
/// anchor against the specification's own headings asks an independent question, and it is the
/// question that renumbering an amended specification would make interesting.
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

    /// <summary>
    /// A numbered or appendix heading. The trailing dot is optional because the specification
    /// writes "## 23. Complexity" but "### 7.3 Parsing concurrency".
    /// </summary>
    private static readonly Regex Heading = new(
        @"^\#{2,4}[ ](?:Appendix[ ])?(?<section>(?:\d+(?:\.\d+)*|[A-Z](?:\.\d+)*))\.?(?:[ ]|$)",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static HashSet<string> Sections { get; } = Heading
        .Matches(File.ReadAllText(RepositoryLayout.Specification))
        .Select(match => match.Groups["section"].Value)
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
    /// The gate is worthless if the heading regex silently matches nothing, so pin a few sections
    /// that must be found, spanning both heading spellings and the appendices.
    /// </summary>
    [TestCase("22")]
    [TestCase("7.3")]
    [TestCase("23")]
    [TestCase("B")]
    [TestCase("C.4")]
    public void TheSpecificationHeadingsParse(string section) => Sections.ShouldContain(section);

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
}
