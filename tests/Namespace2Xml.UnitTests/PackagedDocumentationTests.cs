using System.Text.RegularExpressions;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// The package carries its own documentation so an agent with no network can still read the
/// contract. These gates check that the carried set is coherent.
/// </summary>
/// <remarks>
/// <c>llms.txt</c> indexes the documentation by relative path. Inside the package those paths
/// resolve only if two things hold: the document is packed at all, and it is packed at the path
/// the index names it by. Neither is checked by anything that builds or publishes the package, so
/// a document added to the index and not to the project file produces an index with a hole in it
/// and no failure anywhere. That is the quiet failure this file exists to make loud.
/// </remarks>
[TestFixture]
public class PackagedDocumentationTests
{
    /// <summary>
    /// Index entries deliberately not packed, each with the reason. An entry here is a link that
    /// dangles offline, which is a cost paid on purpose rather than an oversight.
    /// </summary>
    private static readonly Dictionary<string, string> DeliberateOmissions = new(StringComparer.Ordinal)
    {
        ["docs/migration-2.x-to-3.0.md"] =
            "300 KB describing the version this one replaces: the least useful document to carry " +
            "offline and by some distance the most expensive",
    };

    private static readonly Regex RelativeLink = new(
        @"\]\((?<path>(?!https?:)[^)]+)\)",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant);

    private static readonly Regex PackedFile = new(
        @"<None\s+Include=""(?<include>[^""]+)""\s+Pack=""true""\s+PackagePath=""(?<packagePath>[^""]*)""",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant);

    private static string ProjectFile =>
        File.ReadAllText(Path.Combine(
            RepositoryLayout.Root, "src", "Namespace2Xml.Cli", "Namespace2Xml.Cli.csproj"));

    /// <summary>Repository-relative paths of every file the CLI project packs.</summary>
    private static Dictionary<string, string> PackedDocuments()
    {
        var packed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var match in PackedFile.Matches(ProjectFile).Cast<Match>())
        {
            var include = match.Groups["include"].Value
                .Replace('\\', '/')
                .Replace("../../", string.Empty, StringComparison.Ordinal);

            packed[include] = match.Groups["packagePath"].Value.Replace('\\', '/');
        }

        return packed;
    }

    /// <summary>Relative links in the index, excluding directory links, which name no file.</summary>
    private static List<string> IndexedDocuments() =>
        [.. RelativeLink
            .Matches(File.ReadAllText(Path.Combine(RepositoryLayout.Root, "llms.txt")))
            .Cast<Match>()
            .Select(match => match.Groups["path"].Value)
            .Where(path => !path.EndsWith('/'))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    [Test]
    public void EveryIndexedDocumentIsPackedOrDeliberatelyOmitted()
    {
        var packed = PackedDocuments();

        foreach (var document in IndexedDocuments())
        {
            if (DeliberateOmissions.TryGetValue(document, out var reason))
            {
                packed.ShouldNotContainKey(
                    document,
                    $"{document} is recorded as a deliberate omission because it is {reason}, but " +
                    "the project packs it. One of the two statements is stale.");

                continue;
            }

            packed.ShouldContainKey(
                document,
                $"llms.txt indexes {document}, so an agent reading the index inside the package " +
                "will look for it there. Either pack it or record why it is omitted.");
        }
    }

    /// <summary>
    /// A packed document must land at the path the index names it by, because the index links are
    /// relative and resolve against the package layout.
    /// </summary>
    [Test]
    public void EveryPackedDocumentMirrorsItsRepositoryPath()
    {
        foreach (var (repositoryPath, packagePath) in PackedDocuments())
        {
            var directory = repositoryPath.Contains('/', StringComparison.Ordinal)
                ? repositoryPath[..(repositoryPath.LastIndexOf('/') + 1)]
                : string.Empty;

            var normalized = packagePath is "/" or "\\" ? string.Empty : packagePath;

            normalized.ShouldBe(
                directory,
                $"{repositoryPath} is packed to '{packagePath}', so a relative link to it from " +
                "llms.txt resolves to a path the package does not have");
        }
    }

    /// <summary>
    /// The documents an agent needs before it can report anything must be readable offline, not
    /// merely linked. This is the packaged counterpart of the discovery-surface gate.
    /// </summary>
    [Test]
    public void ThePackageCarriesEveryDocumentAnAgentNeeds()
    {
        var packed = PackedDocuments();

        string[] required =
        [
            "README.md",
            "llms.txt",
            "docs/specification.md",
            "docs/diagnostics.md",
            "KNOWN-LIMITS.md",
            "CONTRIBUTING.md",
            "AGENTS.md",
            "spec/diagnostics.registry.json",
            "spec/contract-bundle.json",
            "spec/diagnostic-stream.schema.json",
        ];

        foreach (var document in required)
        {
            packed.ShouldContainKey(
                document,
                $"{document} must travel with the package; an agent working without network " +
                "access has nothing else to read");
        }
    }
}
