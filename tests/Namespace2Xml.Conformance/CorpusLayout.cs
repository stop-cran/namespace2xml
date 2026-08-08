namespace Namespace2Xml.Conformance;

/// <summary>Locates the repository root and the conformance corpus from a test binary.</summary>
internal static class CorpusLayout
{
    /// <summary>
    /// Names Appendix C gives to fixture-owned entries. Everything else in a run directory is a
    /// destination the tool under test produced.
    /// </summary>
    internal static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal)
    {
        "args.txt", "args-diagnostics.txt", "inputs", "schemes", "expected",
        "expected-diagnostics.json", "expected-exit-code.txt", "expected-stdout.txt",
        "requirements.txt", "legacy.md",
    };

    internal static string Root { get; } = Locate();

    internal static string Corpus => Path.Combine(Root, "conformance");

    internal static string AssertionManifest => Path.Combine(Corpus, "assertions.json");

    internal static string Specification => Path.Combine(Root, "docs", "specification.md");

    /// <summary>
    /// The generated Section 6.4.3 stream schema. The comparer drives its value constraints from
    /// this file so that the published schema and the oracle cannot disagree.
    /// </summary>
    internal static string StreamSchema => Path.Combine(Root, "spec", "diagnostic-stream.schema.json");

    /// <summary>
    /// The committed contract bundle. Appendix C.5 placeholders resolve from this file, so that a
    /// case asserting a contract revision is asserting it against the contract rather than against
    /// the binary that reports it.
    /// </summary>
    internal static string ContractBundle => Path.Combine(Root, "spec", "contract-bundle.json");

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
