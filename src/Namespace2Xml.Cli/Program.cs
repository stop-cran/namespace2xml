using Namespace2Xml.Contract;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Pipeline;

namespace Namespace2Xml.Cli;

/// <summary>Process entry point.</summary>
public static class Program
{
    /// <summary>Runs the tool.</summary>
    /// <param name="args">Raw argument vector, exactly as supplied by the host.</param>
    /// <returns>An exit code as defined by specification Section 6.3.</returns>
    public static int Main(string[] args) =>
        Execute(
            args,
            Console.Out,
            Console.Error,
            static (command, log) => Transformation.Run(command, sink: null, log: log));

    internal static int Execute(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        Func<CommandLine, IOperationalLog, TransformationResult> transform,
        Stream? jsonStream = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
        ArgumentNullException.ThrowIfNull(transform);

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

        var parsed = CommandLineParser.Parse(args);
        if (parsed.Diagnostic is { } invalid)
        {
            // The default threshold, not the one the arguments asked for. Section 6.2 makes
            // verbosity a property of a validated command line, and this line reports why
            // validation failed — including, when it was '--verbosity' itself that was invalid, a
            // refusal that could not be filtered by the value being refused. Exit 1 with an empty
            // stream would leave the caller nothing to act on.
            Emit(stderr, format, [invalid], Verbosity.Information);
            return 1;
        }

        var command = parsed.CommandLine!;
        var log = OperationalLogWriter.For(stderr, command);

        TransformationResult result;
        try
        {
            result = transform(command, log);
        }
        catch (PipelineInvariantException failure)
        {
            EmitInvariantFailure(stderr, format, command.Verbosity, failure, jsonStream);
            return 1;
        }

        Emit(stderr, format, result.Diagnostics, command.Verbosity, jsonStream);
        return result.ExitCode;
    }

    internal static void EmitInvariantFailure(
        TextWriter stderr,
        DiagnosticFormat format,
        Verbosity verbosity,
        PipelineInvariantException failure,
        Stream? jsonStream = null)
    {
        if (format == DiagnosticFormat.Json)
        {
            Emit(stderr, format, [], verbosity, jsonStream);
            return;
        }

        if (verbosity == Verbosity.None)
        {
            return;
        }

        stderr.Write(
            "namespace2xml " + ContractBundle.ProductVersion
            + ": internal pipeline invariant failed: " + failure.Message + "\n");
    }

    private static void Emit(
        TextWriter stderr,
        DiagnosticFormat format,
        IReadOnlyList<Diagnostic> diagnostics,
        Verbosity verbosity,
        Stream? jsonStream = null)
    {
        // Section 6.2 filters what is written and nothing else: the list arrives already ordered by
        // Section 24 and this never reorders it, so a threshold change moves lines out of the
        // stream without moving the ones that remain.
        var admitted = diagnostics.Where(d => verbosity.Admits(d.Severity)).ToList();

        // Section 6.4.3: a failure to write the diagnostic stream is not itself a diagnostic and
        // does not change the exit code. A full or closed standard error must not turn a decided
        // outcome into a different one.
        try
        {
            if (format == DiagnosticFormat.Json)
            {
                // The array container is always written, so the stream always parses (Section
                // 6.4.3). That clause explicitly overrides Section 6.2 for `none`: "--verbosity
                // none, and any threshold that filters every produced diagnostic, yields exactly
                // the two bytes [] followed by one LF", which is what an empty list renders as.
                var bytes = JsonDiagnosticWriter.Render(admitted);
                if (jsonStream is not null)
                {
                    jsonStream.Write(bytes, 0, bytes.Length);
                    jsonStream.Flush();
                }
                else
                {
                    using var raw = Console.OpenStandardError();
                    raw.Write(bytes, 0, bytes.Length);
                    raw.Flush();
                }
                return;
            }

            foreach (var diagnostic in admitted)
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
