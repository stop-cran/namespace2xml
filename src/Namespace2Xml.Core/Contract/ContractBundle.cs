using System.Reflection;
using System.Text.Json;

namespace Namespace2Xml.Contract;

/// <summary>
/// The versioned contract this binary implements: the specification text plus the machine-readable
/// diagnostic registry, carrying one revision identifier that changes whenever either artifact
/// changes. Specification Section 22.
/// </summary>
/// <remarks>
/// Reported by <c>--version</c> so that a bug report, including one filed by an automated agent,
/// can name the exact contract the observed behaviour was measured against.
/// </remarks>
public sealed record ContractBundle(
    string Revision,
    string SpecificationSha256,
    string RegistrySha256)
{
    private const string ResourceName = "Namespace2Xml.contract-bundle.json";

    /// <summary>The bundle embedded in this assembly.</summary>
    public static ContractBundle Current { get; } = Load();

    private static ContractBundle Load()
    {
        using var stream = typeof(ContractBundle).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded resource '{ResourceName}' is missing. The build is not self-describing.");

        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        return new ContractBundle(
            root.GetProperty("revision").GetString()!,
            root.GetProperty("specification").GetProperty("sha256").GetString()!,
            root.GetProperty("registry").GetProperty("sha256").GetString()!);
    }

    /// <summary>Informational version of the running assembly, including its source revision.</summary>
    public static string ProductVersion { get; } =
        typeof(ContractBundle).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ContractBundle).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}
