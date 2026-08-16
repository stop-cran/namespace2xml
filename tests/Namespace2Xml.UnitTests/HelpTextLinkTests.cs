using System.Text.RegularExpressions;
using Namespace2Xml.Cli;
using Namespace2Xml.Contract;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Specification section 6.4: <c>--help</c> and <c>--version</c> are the tool's discovery surface,
/// and for an automated caller they are the only one.
/// </summary>
/// <remarks>
/// The release workflow follows every link both commands print and refuses a tag whose links do
/// not resolve, which is the check that matters. It is also the check that runs last: a link to a
/// branch that does not carry this contract passes every gate, review included, and fails at the
/// moment of publication. This gate answers the same question one push earlier and without the
/// network, so a wrong link is a red build rather than a failed release.
///
/// The rule is structural rather than a list of known URLs on purpose: a link added later is
/// covered the day it is added, which a fixture enumerating today's three would not be.
/// </remarks>
[TestFixture]
public class HelpTextLinkTests
{
    private static readonly Regex DocumentLink = new(
        @"https://github\.com/stop-cran/namespace2xml/blob/(?<ref>[^/]+)/(?<path>\S+)",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant);

    private static string Surface => HelpText.Render() + "\n" + HelpText.RenderVersion();

    [Test]
    public void EveryDocumentLinkNamesThisReleaseTag()
    {
        var links = DocumentLink.Matches(Surface);

        links.Count.ShouldBeGreaterThan(0, "the discovery surface must link to the contract at all");

        foreach (var link in links)
        {
            ((Match)link).Groups["ref"].Value.ShouldBe(
                $"v{ContractBundle.ProductVersion}",
                $"'{link}' names a git ref other than this release's tag, so it can serve bytes " +
                "other than the ones this binary implements and reports a hash for");
        }
    }

    /// <summary>
    /// Every linked document must exist in the working tree, because the tag the link names is
    /// this commit and a link to a file nobody wrote resolves to a 404 at release time.
    /// </summary>
    [Test]
    public void EveryLinkedDocumentExistsInTheRepository()
    {
        foreach (var link in DocumentLink.Matches(Surface).Cast<Match>())
        {
            var path = Path.Combine(RepositoryLayout.Root, link.Groups["path"].Value);
            File.Exists(path).ShouldBeTrue($"{link.Value} names {link.Groups["path"].Value}, which is not in the repository");
        }
    }

    /// <summary>
    /// The documents an automated caller cannot work without must be reachable in one step from
    /// the help text.
    /// </summary>
    /// <remarks>
    /// The two gates above are structural, so both stay green when a link is deleted: an empty set
    /// of wrong links is still a set of no wrong links. This one is an enumeration for that
    /// reason, and it is deliberately short. Each entry answers a question an agent asks before it
    /// can report anything usefully: what is the contract, what does this code mean, is this
    /// already known, and where does a finding go. An agent that has to search for those either
    /// guesses or gives up, and both failures are silent.
    ///
    /// Help and version are asserted separately rather than over their concatenation. Checked
    /// together, deleting a link from one passes as long as the other still carries it, which is
    /// exactly the regression worth catching: an agent runs one command, not both.
    ///
    /// Matching is case-sensitive because the distinction being asserted is partly one of case:
    /// the <c>known-limits</c> field and the <c>KNOWN-LIMITS.md</c> file it names differ by
    /// nothing else, and Shouldly compares strings case-insensitively unless told otherwise.
    /// </remarks>
    [Test]
    public void TheHelpTextLinksEveryDocumentAnAgentNeeds()
    {
        string[] required =
        [
            "docs/specification.md",
            "docs/diagnostics.md",
            "KNOWN-LIMITS.md",
            "CONTRIBUTING.md",
            "AGENTS.md",
            "llms.txt",
        ];

        foreach (var document in required)
        {
            HelpText.Render().ShouldContain(
                document,
                Case.Sensitive,
                $"--help must link {document}; an agent that cannot reach it from the tool has no " +
                "way to find it that does not involve guessing");
        }
    }

    /// <summary>
    /// The version output is the machine-readable half of the discovery surface, so the documents
    /// it names are the ones a caller parsing fields rather than prose has to be able to find.
    /// </summary>
    [Test]
    public void TheVersionOutputLinksTheContractDocuments()
    {
        string[] required =
        [
            "docs/specification.md",
            "docs/diagnostics.md",
            "KNOWN-LIMITS.md",
            "llms.txt",
        ];

        foreach (var document in required)
        {
            HelpText.RenderVersion().ShouldContain(
                document,
                Case.Sensitive,
                $"--version must link {document}; it is the only discovery surface a caller that " +
                "parses fields rather than prose will read");
        }
    }
}
