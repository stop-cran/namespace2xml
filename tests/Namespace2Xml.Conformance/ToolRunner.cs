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

    /// <summary>Invokes the tool with the given tokens and working directory.</summary>
    public static ToolResult Run(IReadOnlyList<string> arguments, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = DotnetHost,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // Launching through the host keeps the run identical on every platform without needing
        // a platform-specific apphost to be present in the test output.
        startInfo.ArgumentList.Add(ToolAssembly);

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Section 24 forbids results that depend on locale or time zone. Pinning them here means
        // a green local run and a green CI run mean the same thing.
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        startInfo.Environment["LANG"] = "C";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the tool process.");

        using var outBuffer = new MemoryStream();
        using var errBuffer = new MemoryStream();

        var outTask = process.StandardOutput.BaseStream.CopyToAsync(outBuffer);
        var errTask = process.StandardError.BaseStream.CopyToAsync(errBuffer);

        Task.WaitAll(outTask, errTask);
        process.WaitForExit();

        return new ToolResult(process.ExitCode, outBuffer.ToArray(), errBuffer.ToArray());
    }

    private static string LocateToolAssembly()
    {
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
