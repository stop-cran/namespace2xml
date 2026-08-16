using System.Diagnostics;

namespace Namespace2Xml.Conformance;

/// <summary>Result of one tool invocation, captured as raw bytes so nothing is normalized away.</summary>
/// <param name="ExitCode">Process exit code.</param>
/// <param name="StandardOutput">Raw standard-output bytes.</param>
/// <param name="StandardError">Raw standard-error bytes.</param>
public sealed record ToolResult(int ExitCode, byte[] StandardOutput, byte[] StandardError);

/// <summary>
/// Runs the packaged tool as a real process with a real argument vector. This is the production
/// black-box lane: the harness never calls into the tool's internals, so a seam cannot diverge
/// from what a user actually invokes.
/// </summary>
public static class ToolRunner
{
    private static readonly string ToolAssembly = LocateToolAssembly();
    private static readonly string DotnetHost = LocateDotnetHost();

    /// <summary>
    /// Upper bound on one tool invocation. Generous enough that a slow runner never flakes, short
    /// enough that a hang fails the case instead of the job.
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    /// <summary>Invokes the tool with the given tokens and working directory.</summary>
    public static ToolResult Run(IReadOnlyList<string> arguments, string workingDirectory) =>
        Run(ToolAssembly, arguments, workingDirectory);

    /// <summary>
    /// Invokes an arbitrary managed assembly through the .NET host. The Appendix C.6 differential
    /// lane uses this to observe the pinned 2.4.0 baseline under exactly the environment the corpus
    /// harness gives the tool under test, so a divergence is a difference between the two binaries
    /// rather than between two ways of launching one.
    /// </summary>
    /// <param name="assembly">Absolute path of the managed entry assembly.</param>
    /// <param name="arguments">Argument tokens, passed through without shell interpretation.</param>
    /// <param name="workingDirectory">Directory the process starts in.</param>
    public static ToolResult Run(string assembly, IReadOnlyList<string> arguments, string workingDirectory) =>
        Run(assembly, arguments, workingDirectory, host: null);

    /// <summary>
    /// Invokes a managed assembly through a chosen .NET host.
    /// </summary>
    /// <param name="assembly">Absolute path of the managed entry assembly.</param>
    /// <param name="arguments">Argument tokens, passed through without shell interpretation.</param>
    /// <param name="workingDirectory">Directory the process starts in.</param>
    /// <param name="host">The muxer to launch with, or <see langword="null"/> for the harness's own.</param>
    public static ToolResult Run(
        string assembly,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string? host)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        // Launching through the host keeps the run identical on every platform without needing
        // a platform-specific apphost to be present in the test output.
        return Start(host ?? DotnetHost, [assembly, .. arguments], workingDirectory);
    }

    /// <summary>
    /// Invokes the .NET host itself, with no assembly. The Appendix C.6 differential lane asks the
    /// host which runtimes it can see before it observes the baseline, under the same pinned
    /// environment the baseline will be launched with.
    /// </summary>
    /// <param name="host">The muxer to ask, or <see langword="null"/> for the harness's own.</param>
    /// <param name="arguments">Argument tokens for the host.</param>
    internal static ToolResult RunHost(string? host, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return Start(host ?? DotnetHost, arguments, Path.GetTempPath());
    }

    private static ToolResult Start(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Section 24 forbids results that depend on locale or time zone. Pinning them here means
        // a green local run and a green CI run mean the same thing. LC_ALL is set as well as LANG
        // because on glibc LC_ALL overrides LANG, so pinning LANG alone can be defeated by the
        // ambient environment.
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        startInfo.Environment["LANG"] = "C";
        startInfo.Environment["LC_ALL"] = "C";
        startInfo.Environment["TZ"] = "UTC";

        // Appendix C.6 forbids observing the differential baseline on a runtime it was never
        // published against. "Minor" is the host's own default and never crosses a major version,
        // so setting it here changes nothing except that an ambient DOTNET_ROLL_FORWARD=LatestMajor
        // in the environment cannot silently move either binary onto a different runtime.
        startInfo.Environment["DOTNET_ROLL_FORWARD"] = "Minor";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the tool process.");

        using var outBuffer = new MemoryStream();
        using var errBuffer = new MemoryStream();

        var outTask = process.StandardOutput.BaseStream.CopyToAsync(outBuffer);
        var errTask = process.StandardError.BaseStream.CopyToAsync(errBuffer);

        // A tool that never exits must fail its case, not consume the whole CI job budget.
        if (!process.WaitForExit(Timeout))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process ended between the timeout and the kill.
            }

            throw new ConformanceFormatException(
                $"the tool did not exit within {Timeout.TotalSeconds:0} seconds for arguments " +
                $"[{string.Join(", ", arguments)}].");
        }

        Task.WaitAll([outTask, errTask], Timeout);

        return new ToolResult(process.ExitCode, outBuffer.ToArray(), errBuffer.ToArray());
    }

    /// <summary>
    /// Finds the tool assembly the corpus will judge.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The build output is the default, and <c>N2X_TOOL_ASSEMBLY</c> overrides it. Packing, the
    /// NuGet layout and the tool shim all sit between a built binary and the artifact a user
    /// installs, and the two are not byte-identical: a release build normalizes source paths, so
    /// the shipped assembly is never the one the corpus ran against. The override lets the release
    /// workflow point the whole corpus at the packed artifact before it is published, rather than
    /// publishing on the strength of a smoke test.
    /// </para>
    /// <para>
    /// A variable that is set but names nothing is a hard failure rather than a fall back to the
    /// build output. Falling back would let a typo silently re-test the binary the corpus has
    /// already judged and report it as evidence about the package — which is the exact false green
    /// this override exists to remove.
    /// </para>
    /// </remarks>
    private static string LocateToolAssembly()
    {
        var overridden = Environment.GetEnvironmentVariable("N2X_TOOL_ASSEMBLY");

        if (!string.IsNullOrEmpty(overridden))
        {
            return File.Exists(overridden)
                ? Path.GetFullPath(overridden)
                : throw new InvalidOperationException(
                    $"N2X_TOOL_ASSEMBLY names '{overridden}', which does not exist.");
        }

        var candidate = Path.Combine(AppContext.BaseDirectory, "namespace2xml.dll");

        return File.Exists(candidate)
            ? candidate
            : throw new InvalidOperationException(
                $"The tool assembly was not found at '{candidate}'. Build the CLI project first.");
    }

    /// <summary>
    /// Finds the .NET muxer. <see cref="Environment.ProcessPath"/> is not it: under a test run the
    /// current process is the test host, and asking the test host to execute a framework-dependent
    /// assembly makes it attempt a self-contained launch and fail on a missing hostpolicy.
    /// </summary>
    private static string LocateDotnetHost()
    {
        var fromSdk = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");

        if (!string.IsNullOrEmpty(fromSdk) && File.Exists(fromSdk))
        {
            return fromSdk;
        }

        var executable = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

        // The runtime directory is <root>/shared/Microsoft.NETCore.App/<version>, so the muxer sits
        // three levels above it.
        var directory = new DirectoryInfo(
            System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory());

        for (var level = 0; level < 3 && directory is not null; level++)
        {
            directory = directory.Parent;
        }

        var candidate = directory is null ? null : Path.Combine(directory.FullName, executable);

        if (candidate is not null && File.Exists(candidate))
        {
            return candidate;
        }

        var fromProcess = Environment.ProcessPath;

        if (fromProcess is not null &&
            string.Equals(Path.GetFileName(fromProcess), executable, StringComparison.OrdinalIgnoreCase))
        {
            return fromProcess;
        }

        // Last resort: PATH lookup. Better than failing outright, but it means the tool may run on
        // a different runtime than the harness, so say so if it goes wrong.
        return "dotnet";
    }
}
