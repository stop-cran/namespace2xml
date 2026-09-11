using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Output;
using Namespace2Xml.Pipeline;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Specification Section 6.2 verbosity as an output threshold, and the Section 21.4 operational
/// message that is the only one the specification names by level.
/// </summary>
public sealed class VerbosityTests
{
    /// <summary>
    /// Section 6.2 levels 1 through 4 all admit a warning: the first three say "all diagnostics" or
    /// name warnings explicitly, and <c>warning</c> is the level warnings are named for.
    /// </summary>
    [TestCase(Verbosity.Trace)]
    [TestCase(Verbosity.Debug)]
    [TestCase(Verbosity.Information)]
    [TestCase(Verbosity.Warning)]
    public void AThresholdAtOrAboveWarningAdmitsAWarning(Verbosity verbosity) =>
        verbosity.Admits(DiagnosticSeverity.Warning).ShouldBeTrue();

    /// <summary>
    /// Section 6.2 level 5 is "error and critical messages", so a warning is below it. This is the
    /// one filtering decision the two severities Section 22 defines can actually distinguish.
    /// </summary>
    [TestCase(Verbosity.Error)]
    [TestCase(Verbosity.Critical)]
    [TestCase(Verbosity.None)]
    public void AThresholdBelowWarningDropsAWarning(Verbosity verbosity) =>
        verbosity.Admits(DiagnosticSeverity.Warning).ShouldBeFalse();

    /// <summary>Section 6.2 levels 1 through 5 all name errors.</summary>
    [TestCase(Verbosity.Trace)]
    [TestCase(Verbosity.Debug)]
    [TestCase(Verbosity.Information)]
    [TestCase(Verbosity.Warning)]
    [TestCase(Verbosity.Error)]
    public void AThresholdAtOrAboveErrorAdmitsAnError(Verbosity verbosity) =>
        verbosity.Admits(DiagnosticSeverity.Error).ShouldBeTrue();

    /// <summary>
    /// Section 6.2 level 6 is "critical host/runtime failures only", and Section 22 places host and
    /// runtime failures outside the diagnostic registry — every registered code is a warning or an
    /// error. So <c>critical</c> admits no diagnostic at all, and <c>none</c> is "no diagnostic or
    /// operational log output".
    /// </summary>
    [TestCase(Verbosity.Critical)]
    [TestCase(Verbosity.None)]
    public void AThresholdBelowErrorDropsAnError(Verbosity verbosity) =>
        verbosity.Admits(DiagnosticSeverity.Error).ShouldBeFalse();

    /// <summary>
    /// Section 6.2 level 3 is the default and names "information, warning, error, and critical",
    /// so the Section 21.4 replacement message appears without the caller asking for anything.
    /// </summary>
    [Test]
    public void TheDefaultThresholdAdmitsAnInformationMessage() =>
        Verbosity.Information.Admits(OperationalLevel.Information).ShouldBeTrue();

    /// <summary>Section 6.2 level 3 is below the trace and debug categories.</summary>
    [TestCase(OperationalLevel.Trace)]
    [TestCase(OperationalLevel.Debug)]
    public void TheDefaultThresholdDropsTraceAndDebug(OperationalLevel level) =>
        Verbosity.Information.Admits(level).ShouldBeFalse();

    /// <summary>
    /// Section 6.2 level 2 is "all diagnostics plus pipeline-phase progress, merge decisions,
    /// expansion counters, and output-plan summaries" — the debug categories and everything above
    /// them, but not the trace ones.
    /// </summary>
    [Test]
    public void DebugAdmitsDebugAndInformationButNotTrace()
    {
        Verbosity.Debug.Admits(OperationalLevel.Debug).ShouldBeTrue();
        Verbosity.Debug.Admits(OperationalLevel.Information).ShouldBeTrue();
        Verbosity.Debug.Admits(OperationalLevel.Trace).ShouldBeFalse();
    }

    /// <summary>Section 6.2 level 1 is the most verbose and admits every category.</summary>
    [TestCase(OperationalLevel.Trace)]
    [TestCase(OperationalLevel.Debug)]
    [TestCase(OperationalLevel.Information)]
    public void TraceAdmitsEveryOperationalLevel(OperationalLevel level) =>
        Verbosity.Trace.Admits(level).ShouldBeTrue();

    /// <summary>
    /// Section 6.2 level 7 is "no diagnostic or operational log output", and levels 4 through 6
    /// list only severities, never an operational category.
    /// </summary>
    [TestCase(Verbosity.Warning)]
    [TestCase(Verbosity.Error)]
    [TestCase(Verbosity.Critical)]
    [TestCase(Verbosity.None)]
    public void AThresholdBelowInformationWritesNoOperationalMessage(Verbosity verbosity)
    {
        var writer = new StringWriter();

        new OperationalLogWriter(writer, verbosity)
            .Write(OperationalLevel.Information, "replaced something.");

        writer.ToString().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 6.4.3 suppresses operational messages "entirely, at every verbosity", so the
    /// encoding decides before the threshold does. Asserting the returned type, rather than that
    /// nothing was written, is what pins the stronger property: under <c>json</c> no code path
    /// holds a writer aimed at the stream carrying the array.
    /// </summary>
    [Test]
    public void TheJsonEncodingSuppressesOperationalMessagesAtEveryVerbosity()
    {
        foreach (var verbosity in Enum.GetValues<Verbosity>())
        {
            var writer = new StringWriter();

            OperationalLogWriter.For(writer, Command(verbosity, DiagnosticFormat.Json))
                .ShouldBeOfType<SilentOperationalLog>();

            OperationalLogWriter.For(writer, Command(verbosity, DiagnosticFormat.Text))
                .ShouldBeOfType<OperationalLogWriter>();
        }
    }

    /// <summary>
    /// Section 6.2 puts operational messages on standard error and Section 24 forbids
    /// host-dependent line endings, so a message is one LF-terminated line.
    /// </summary>
    [Test]
    public void AnOperationalMessageIsOneLineTerminatedWithLf()
    {
        var writer = new StringWriter();

        new OperationalLogWriter(writer, Verbosity.Information)
            .Write(OperationalLevel.Information, "replaced something.");

        writer.ToString().ShouldBe("info: replaced something.\n");
    }

    /// <summary>
    /// Section 21.4: "Replacing an existing destination is allowed and is logged at information
    /// level." Both halves are asserted: the run succeeds, and the message appears at the default
    /// threshold rather than only when tracing.
    /// </summary>
    [Test]
    public void ReplacingAnExistingDestinationIsLoggedAtInformationLevel()
    {
        var sink = new TransformationTests.Sink();
        var sources = new TransformationTests.Sources(
            ("in.txt", "a.x=1"), ("scheme.txt", "a.output=namespace"));

        var log = new RecordingLog();

        TransformationTests.Run(sink, sources, log, "-i", "in.txt", "-s", "scheme.txt")
            .ExitCode.ShouldBe(0);

        // Nothing existed, so nothing was replaced.
        log.At(OperationalLevel.Information).ShouldBeEmpty();

        var second = new RecordingLog();

        TransformationTests.Run(sink, sources, second, "-i", "in.txt", "-s", "scheme.txt")
            .ExitCode.ShouldBe(0);

        second.At(OperationalLevel.Information).ShouldHaveSingleItem()
            .ShouldContain("a.properties", Case.Sensitive);
    }

    /// <summary>
    /// Section 6.2: verbosity "never changes exit codes, output files, resource accounting, or the
    /// underlying deterministic diagnostic order".
    /// </summary>
    /// <remarks>
    /// The threshold is carried on the command line the pipeline receives, so a future change that
    /// consulted it while deciding anything would compile and would pass every test that runs at
    /// one threshold. This runs the same work at all seven and compares the results.
    /// </remarks>
    [Test]
    public void VerbosityChangesNothingButWhatIsWritten()
    {
        var results = Enum.GetValues<Verbosity>().Select(verbosity =>
        {
            var sink = new TransformationTests.Sink();
            var sources = new TransformationTests.Sources(
                ("in.txt", "a.x=1\na.z=2"),
                ("scheme.txt", "a.output=namespace"));
            var command = CommandLineParser.Parse(
                [
                    "-i", "missing.txt", "-i", "in.txt", "-s", "scheme.txt",
                    "--verbosity", verbosity.ToString().ToLowerInvariant(),
                ])
                .CommandLine.ShouldNotBeNull();
            var observer = new ObservationLedger();
            var operational = new StringWriter();

            var result = Transformation.Run(
                command,
                sources,
                sink,
                OperationalLogWriter.For(operational, command),
                observer);

            return (
                Verbosity: verbosity,
                Result: Describe(result),
                Files: Describe(sink),
                Observations: observer.Describe(),
                Operational: operational.ToString());
        }).ToList();

        var first = results[0];

        foreach (var other in results)
        {
            other.Result.ShouldBe(first.Result, $"at --verbosity {other.Verbosity}");
            other.Files.ShouldBe(first.Files, $"at --verbosity {other.Verbosity}");
            other.Observations.ShouldBe(first.Observations, $"at --verbosity {other.Verbosity}");
        }

        results.Single(run => run.Verbosity == Verbosity.Trace).Operational.ShouldNotBeEmpty();
        results.Single(run => run.Verbosity == Verbosity.None).Operational.ShouldBeEmpty();
    }

    /// <summary>
    /// Sections 6.2, 6.4.3, 21.3, and 24 jointly require a late publication failure to remain one
    /// complete JSON diagnostic array with no operational text at every threshold.
    /// </summary>
    [Test]
    public void LatePublicationFailureIsOnePureJsonArrayAtEveryVerbosity()
    {
        foreach (var verbosity in Enum.GetValues<Verbosity>())
        {
            var command = CommandLineParser.Parse(
                [
                    "-i", "in.txt", "-s", "scheme.txt",
                    "--diagnostics-format", "json",
                    "--verbosity", verbosity.ToString().ToLowerInvariant(),
                ])
                .CommandLine.ShouldNotBeNull();
            var standardError = new StringWriter();

            var result = Transformation.Run(
                command,
                new TransformationTests.Sources(
                    ("in.txt", "a.x=1"),
                    ("scheme.txt", "a.output=namespace")),
                new FailingPublicationSink(),
                OperationalLogWriter.For(standardError, command));

            standardError.Write(Encoding.UTF8.GetString(JsonDiagnosticWriter.Render(result.Diagnostics)));

            result.ExitCode.ShouldBe(1, $"at --verbosity {verbosity}");
            result.Published.ShouldBe(0, $"at --verbosity {verbosity}");
            using var document = JsonDocument.Parse(standardError.ToString());
            document.RootElement.ValueKind.ShouldBe(JsonValueKind.Array);
            document.RootElement.GetArrayLength().ShouldBe(1);
            document.RootElement[0].GetProperty("code").GetString().ShouldBe("PATH002");
        }
    }

    /// <summary>
    /// Section 21.2 makes repeated warning-policy flags idempotent, while Section 6.2 prevents
    /// verbosity from changing the policy result, diagnostics, publication, or accounting.
    /// </summary>
    [Test]
    public void RepeatedWarningPolicyIsInvariantAcrossEveryVerbosity()
    {
        var runs = Enum.GetValues<Verbosity>().Select(verbosity =>
        {
            var sink = new TransformationTests.Sink();
            var command = CommandLineParser.Parse(
                [
                    "--fail-on-warning", "--fail-on-warning",
                    "-i", "missing.txt", "-i", "in.txt", "-s", "scheme.txt",
                    "--verbosity", verbosity.ToString().ToLowerInvariant(),
                ])
                .CommandLine.ShouldNotBeNull();
            var observer = new ObservationLedger();

            var result = Transformation.Run(
                command,
                new TransformationTests.Sources(
                    ("in.txt", "a.x=1"),
                    ("scheme.txt", "a.output=namespace")),
                sink,
                SilentOperationalLog.Instance,
                observer);

            return (
                Verbosity: verbosity,
                Result: Describe(result),
                Files: Describe(sink),
                Observations: observer.Describe());
        }).ToList();

        foreach (var run in runs)
        {
            run.Result.ShouldBe(runs[0].Result, $"at --verbosity {run.Verbosity}");
            run.Files.ShouldBe(runs[0].Files, $"at --verbosity {run.Verbosity}");
            run.Observations.ShouldBe(runs[0].Observations, $"at --verbosity {run.Verbosity}");
        }

        var representative = runs[0].Result;
        representative.ShouldContain("ExitCode=1");
        representative.ShouldContain("Published=0");
        representative.ShouldContain("WarningPolicyTriggered=True");
        representative.ShouldContain("WARN001");
    }

    private static CommandLine Command(Verbosity verbosity, DiagnosticFormat format) =>
        new(
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty,
            ".",
            verbosity,
            format,
            ResourceLimits.Defaults);

    private static string Describe(TransformationResult result) => string.Join(
        '\u001f',
        $"State={result.State}",
        $"Unsupported={result.Unsupported}",
        $"Published={result.Published}",
        $"WarningPolicyTriggered={result.WarningPolicyTriggered}",
        $"ExitCode={result.ExitCode}",
        string.Join(
            '\u001e',
            result.Diagnostics.Select(diagnostic => string.Join(
                '\u001d',
                diagnostic.Code,
                diagnostic.Severity,
                diagnostic.Phase,
                diagnostic.Source,
                diagnostic.Line,
                diagnostic.Column,
                diagnostic.Path,
                diagnostic.Declaration,
                diagnostic.Rule.IsDefault ? string.Empty : string.Join('\u001c', diagnostic.Rule),
                diagnostic.Destination,
                diagnostic.Spec,
                diagnostic.Message))));

    private static string Describe(TransformationTests.Sink sink) => string.Join(
        '\u001f',
        string.Join('\u001e', sink.Directories),
        string.Join('\u001e', sink.WriteCalls),
        string.Join(
            '\u001e',
            sink.Written.OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => entry.Key + "\u001d" + entry.Value)));

    /// <summary>Records every operational message, so a test can ask what level carried it.</summary>
    private sealed class RecordingLog : IOperationalLog
    {
        private readonly List<(OperationalLevel Level, string Message)> written = [];

        public bool IsEnabled(OperationalLevel level) => true;

        public void Write(OperationalLevel level, string message) => written.Add((level, message));

        public ReadOnlyCollection<string> At(OperationalLevel level) =>
            written.Where(entry => entry.Level == level).Select(entry => entry.Message).ToList().AsReadOnly();
    }

    private sealed class ObservationLedger : IPipelineObserver
    {
        private readonly List<PipelineObservation> observations = [];

        public void Observe(PipelineObservation observation) => observations.Add(observation);

        public string Describe() => string.Join(
            '\u001e',
            observations.Select(observation => string.Join(
                '\u001d',
                observation.Kind,
                observation.Path,
                observation.Owner,
                observation.Target,
                observation.Amount,
                observation.Accepted,
                observation.Values.IsDefault
                    ? string.Empty
                    : string.Join('\u001c', observation.Values))));
    }

    private sealed class FailingPublicationSink : IPublicationSink
    {
        public bool SupportsSecureContainment => true;

        public void CreateDirectory(string root, string relative)
        {
        }

        public bool Write(string root, string relative, OutputBuffer buffer) =>
            throw new IOException("simulated late write failure");
    }
}
