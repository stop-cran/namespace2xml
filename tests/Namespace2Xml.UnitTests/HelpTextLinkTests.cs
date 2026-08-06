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
}
