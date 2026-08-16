namespace Namespace2Xml.UnitTests;

/// <summary>
/// Locates the repository root from a test binary, so tests can assert against the specification
/// and the contract bundle rather than against a copy of them.
/// </summary>
internal static class RepositoryLayout
{
    internal static string Root { get; } = Locate();

    internal static string Specification => Path.Combine(Root, "docs", "specification.md");

    internal static string Registry => Path.Combine(Root, "spec", "diagnostics.registry.json");

    internal static string StreamSchema => Path.Combine(Root, "spec", "diagnostic-stream.schema.json");

    internal static string ContractBundle => Path.Combine(Root, "spec", "contract-bundle.json");

    internal static string Sha256Of(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }

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
