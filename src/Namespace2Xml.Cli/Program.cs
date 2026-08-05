using Namespace2Xml.Contract;
using Namespace2Xml.Diagnostics;

namespace Namespace2Xml.Cli;

/// <summary>Process entry point.</summary>
public static class Program
{
    /// <summary>
    /// Exit code reserved for a preview build that has not yet implemented the requested work.
    /// Specification Section 6.3 fixes <c>0</c> and <c>1</c> as the normative outcomes, so a
    /// preview must never return either of them for work it did not actually perform.
    /// </summary>
    internal const int NotImplementedInThisPreview = 70;

    /// <summary>Runs the tool.</summary>
    /// <param name="args">Raw argument vector, exactly as supplied by the host.</param>
    /// <returns>An exit code as defined by specification Section 6.3.</returns>
    public static int Main(string[] args)
    {
        var stdout = Console.Out;
        var stderr = Console.Error;

        // The diagnostic encoding is resolved before anything else is validated, so that an
        // invalid command line is itself reported in the encoding the caller asked for.
        var format = DiagnosticsFormatPreScan.Resolve(args);

        switch (DiagnosticsFormatPreScan.ResolveInformationalMode(args))
        {
            case InformationalMode.Help:
                stdout.Write(HelpText.Render());
                return 0;

            case InformationalMode.Version:
                stdout.Write(HelpText.RenderVersion());
                return 0;
        }

        var invalid = CommandLineParser.Parse(args).Diagnostic;
        if (invalid is not null)
        {
            Emit(stderr, format, [invalid]);
            return 1;
        }

        // M0 delivers governance, the conformance harness and the Section 6.4 diagnostic
        // channel. The transformation pipeline lands in later milestones.
        Emit(stderr, format, []);

        // Section 6.4.3 gives standard error to the diagnostic stream alone when the canonical
        // JSON encoding is selected, so an operational message must not follow the array. In the
        // text encoding the message is permitted, but it is terminated with LF rather than
        // Environment.NewLine because Section 24 forbids results that vary by host line ending.
        if (format != DiagnosticFormat.Json)
        {
            stderr.Write(
                "namespace2xml " + ContractBundle.ProductVersion +
                " is a preview that does not yet implement the transformation pipeline. " +
                "Use --help, --version, or track progress at " + HelpText.RepositoryUrl + ".\n");
        }

        return NotImplementedInThisPreview;
    }

    private static void Emit(TextWriter stderr, DiagnosticFormat format, IReadOnlyList<Diagnostic> diagnostics)
    {
        // Section 6.4.3: a failure to write the diagnostic stream is not itself a diagnostic and
        // does not change the exit code. A full or closed standard error must not turn a decided
        // outcome into a different one.
        try
        {
            if (format == DiagnosticFormat.Json)
            {
                // The array container is always written, so the stream always parses (Section 6.4.3).
                using var raw = Console.OpenStandardError();
                var bytes = JsonDiagnosticWriter.Render(diagnostics);
                raw.Write(bytes, 0, bytes.Length);
                raw.Flush();
                return;
            }

            foreach (var diagnostic in diagnostics)
            {
                // LF, not Environment.NewLine: Section 24 forbids host-dependent line endings.
                stderr.Write(TextDiagnosticWriter.Render(diagnostic) + "\n");
            }
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
