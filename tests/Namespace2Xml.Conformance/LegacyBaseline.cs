using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Namespace2Xml.Conformance;

/// <summary>
/// Raised when a baseline package is present but is not the one Appendix C.6 pins.
/// </summary>
public sealed class BaselineIntegrityException : Exception
{
    /// <summary>Creates the exception with an explanatory message.</summary>
    /// <param name="message">Why the package was refused.</param>
    public BaselineIntegrityException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner cause.</summary>
    /// <param name="message">Why the package was refused.</param>
    /// <param name="innerException">The underlying failure.</param>
    public BaselineIntegrityException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception with no message.</summary>
    public BaselineIntegrityException()
    {
    }
}

/// <summary>
/// The namespace2xml 2.4.0 differential baseline of Appendix C.6.
/// <para>
/// The pinned digest and byte size are transcribed here from the specification rather than read
/// from it, so that an edit to either one has to be made twice and reviewed twice. A baseline the
/// harness fetched and then trusted would let a compromised or merely wrong package silently
/// redefine what "2.4.0 did" means, and every compatibility claim in the corpus rests on that.
/// </para>
/// </summary>
internal static class LegacyBaseline
{
    /// <summary>Appendix C.6: the SHA-256 of the pinned package.</summary>
    internal const string PinnedSha256 =
        "92472F4F191A8FC32B81CE30A8F3E2FC97CF99C968F635155172F111EE65C3ED";

    /// <summary>Appendix C.6: the byte size of the pinned package.</summary>
    internal const long PinnedSize = 1095996;

    /// <summary>The framework the package ships for, and the only one it may be observed on.</summary>
    internal const string BaselineFramework = "net9.0";

    /// <summary>
    /// Environment variable naming the pinned <c>.nupkg</c>. Absent, the differential lane does not
    /// run: the baseline is a 1 MB download that a unit-test run should not perform on its own.
    /// </summary>
    private const string PackageVariable = "N2X_LEGACY_PACKAGE";

    /// <summary>
    /// Optional .NET host to run the baseline with. Appendix C.6 requires observing 2.4.0 on the
    /// runtime it targets, and installing that runtime beside the current one needs administrative
    /// rights on some machines. Pointing this at a private install is the way to satisfy the
    /// appendix without them; CI leaves it unset, because the workflow installs both runtimes into
    /// the same root and the default host then finds the right one.
    /// </summary>
    private const string HostVariable = "N2X_LEGACY_DOTNET";

    /// <summary>The host to launch the baseline with, or <see langword="null"/> for the default.</summary>
    internal static string? Host
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable(HostVariable);

            if (string.IsNullOrEmpty(configured))
            {
                return null;
            }

            return File.Exists(configured)
                ? configured
                : throw new BaselineIntegrityException(
                    $"{HostVariable} is set to '{configured}', which does not exist.");
        }
    }

    private static readonly Lazy<string?> Assembly = new(Prepare, isThreadSafe: true);

    /// <summary>
    /// Absolute path of the baseline entry assembly, or <see langword="null"/> when no package was
    /// supplied. Verification failures throw instead of returning null, because a baseline that
    /// fails its integrity check must stop the run rather than skip it.
    /// </summary>
    internal static string? EntryAssembly => Assembly.Value;

    /// <summary>The configured package path, whether or not it exists.</summary>
    internal static string? PackagePath => Environment.GetEnvironmentVariable(PackageVariable);

    /// <summary>
    /// Verifies a candidate package against the Appendix C.6 pin. Exposed so the harness self-tests
    /// can prove the refusal fires without needing a second real download.
    /// </summary>
    /// <param name="packagePath">Path of the package to check.</param>
    /// <exception cref="BaselineIntegrityException">The file is missing, the wrong size, or the wrong hash.</exception>
    internal static void Verify(string packagePath)
    {
        if (!File.Exists(packagePath))
        {
            throw new BaselineIntegrityException(
                $"the differential baseline package '{packagePath}' does not exist.");
        }

        var size = new FileInfo(packagePath).Length;

        // Size is checked first because it is the cheaper of the two and because a size mismatch
        // names the likelier cause: a truncated or redirected download rather than a substitution.
        if (size != PinnedSize)
        {
            throw new BaselineIntegrityException(
                $"the differential baseline package '{packagePath}' is {size} bytes, but Appendix C.6 " +
                $"pins {PinnedSize}. The harness will not run against an unpinned baseline.");
        }

        string actual;

        using (var stream = File.OpenRead(packagePath))
        {
            actual = Convert.ToHexString(SHA256.HashData(stream));
        }

        if (!string.Equals(actual, PinnedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new BaselineIntegrityException(
                $"the differential baseline package '{packagePath}' has SHA-256 {actual}, but " +
                $"Appendix C.6 pins {PinnedSha256}. The harness will not run against an unpinned baseline.");
        }
    }

    private static string? Prepare()
    {
        var packagePath = PackagePath;

        if (string.IsNullOrEmpty(packagePath))
        {
            return null;
        }

        Verify(packagePath);

        // Extraction is keyed on the verified digest, so a stale extraction of a different package
        // can never be reused under the pinned name.
        var destination = Path.Combine(
            Path.GetTempPath(),
            "n2x-baseline-" + PinnedSha256[..16].ToLowerInvariant());

        var entry = Path.Combine(
            destination, "tools", BaselineFramework, "any", "namespace2xml.dll");

        if (!File.Exists(entry))
        {
            Directory.CreateDirectory(destination);
            ZipFile.ExtractToDirectory(packagePath, destination, overwriteFiles: true);
        }

        if (!File.Exists(entry))
        {
            throw new BaselineIntegrityException(
                $"the pinned baseline package does not contain '{Path.GetRelativePath(destination, entry)}'.");
        }

        return entry;
    }

    /// <summary>
    /// Explains, in one sentence, why the differential lane is inert. Used as the ignore reason so
    /// a skipped run says what would make it run.
    /// </summary>
    internal static string WhyInert =>
        $"{PackageVariable} is not set, so the Appendix C.6 baseline is unavailable. Run " +
        "tools/fetch-differential-baseline.ps1 and set it to the downloaded package.";

    /// <summary>
    /// The runtime the baseline must be observed on, formatted for a <c>--fx-version</c>-free
    /// invocation. Appendix C.6 forbids rolling the baseline forward, so the harness asserts the
    /// runtime is present rather than letting the host silently substitute a newer one.
    /// </summary>
    internal static string RequiredRuntimeMajor =>
        BaselineFramework.AsSpan("net".Length).ToString().Split('.')[0]
            .ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Whether a host's runtime listing declares the framework the baseline targets. Parsing is
    /// separated from launching so the detection can be proved without uninstalling a runtime.
    /// </summary>
    /// <param name="listedRuntimes">Standard output of the host's <c>--list-runtimes</c>.</param>
    internal static bool DeclaresRequiredRuntime(string listedRuntimes)
    {
        ArgumentNullException.ThrowIfNull(listedRuntimes);

        var wanted = "Microsoft.NETCore.App " + RequiredRuntimeMajor + ".";

        return listedRuntimes
            .Split('\n')
            .Any(line => line.TrimStart().StartsWith(wanted, StringComparison.Ordinal));
    }

    /// <summary>
    /// Establishes that the host can actually run the baseline, before anything is observed.
    /// <para>
    /// Appendix C.6 requires this because a host that cannot find the runtime is indistinguishable
    /// after the fact from a tool that wrote nothing and exited nonzero. The confusion is not
    /// symmetric: a baseline that never starts diverges from every case's expected result, so it
    /// fails each <c>agrees</c> case and <em>confirms</em> every <c>differs</c> and <c>crashes</c>
    /// one. The lane would then report a plausible list of wrong verdicts whose only obvious repair
    /// turns the entire differential corpus green while measuring nothing at all.
    /// </para>
    /// </summary>
    /// <exception cref="BaselineIntegrityException">The required runtime is not available.</exception>
    internal static void RequireRuntime()
    {
        var listing = ToolRunner.RunHost(Host, ["--list-runtimes"]);
        var listed = Encoding.UTF8.GetString(listing.StandardOutput);

        if (listing.ExitCode == 0 && DeclaresRequiredRuntime(listed))
        {
            return;
        }

        throw new BaselineIntegrityException(
            $"the host has no Microsoft.NETCore.App {RequiredRuntimeMajor}.x runtime, so the " +
            $"Appendix C.6 baseline cannot be observed on the {BaselineFramework} runtime it was " +
            "published against. Install the .NET " + RequiredRuntimeMajor + " runtime, or set " +
            $"{HostVariable} to a host that has one. The differential lane fails rather than " +
            "reporting, because a baseline that never starts diverges from every case and would " +
            "silently confirm every 'differs' and 'crashes' verdict in the corpus." +
            Environment.NewLine + "The host listed:" + Environment.NewLine + listed);
    }
}
