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

    /// <summary>Invokes the tool with the given tokens and working directory.</summary>
    public static ToolResult Run(IReadOnlyList<string> arguments, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.ProcessPath ?? "dotnet",
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
}
