namespace Namespace2Xml.Conformance;

/// <summary>Locates the repository root and the conformance corpus from a test binary.</summary>
internal static class CorpusLayout
{
    internal static string Root { get; } = Locate();

    internal static string Corpus => Path.Combine(Root, "conformance");

    internal static string AssertionManifest => Path.Combine(Corpus, "assertions.json");

    internal static string Specification => Path.Combine(Root, "docs", "specification.md");

    private static string Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "namespace2xml.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root above '{AppContext.BaseDirectory}'.");
    }
}
