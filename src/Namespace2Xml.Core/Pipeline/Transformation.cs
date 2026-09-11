using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Namespace2Xml.Budgets;
using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Output;
using Namespace2Xml.Pipeline.Steps;

namespace Namespace2Xml.Pipeline;

/// <summary>What one invocation of the pipeline produced.</summary>
/// <param name="Diagnostics">The complete Section 24 diagnostic stream.</param>
/// <param name="State">Where the run stopped.</param>
/// <param name="Published">How many destinations were written.</param>
/// <param name="WarningPolicyTriggered">
/// Whether the enabled Section 21.2 warning policy refused publication.
/// </param>
public sealed record TransformationResult(
    ImmutableArray<Diagnostic> Diagnostics,
    PipelineRunState State,
    int Published,
    bool WarningPolicyTriggered = false)
{
    /// <summary>
    /// The Section 6.3 exit code.
    /// </summary>
    /// <remarks>
    /// Section 6.3 gives <c>0</c> to a run that completed with no blocking diagnostic and <c>1</c>
    /// to one that produced any. Warnings remain non-blocking, but the opt-in Section 21.2 policy
    /// can independently make the completed invocation fail.
    /// </remarks>
    public int ExitCode =>
        WarningPolicyTriggered || Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) ? 1 : 0;
}

/// <summary>
/// Runs the twenty specification Section 15.1 steps for one invocation.
/// </summary>
/// <remarks>
/// <para>
/// The step order is expressed once, here, as a chain of <c>Run</c> calls.
/// <see cref="PipelineRun"/> rejects a step that is out of order or that consumes a product from
/// its own future, so the order in this method is checked rather than merely intended.
/// </para>
/// <para>
/// Every step receives its inputs as products rather than reading shared mutable state, with two
/// deliberate exceptions: the <see cref="SourceLoader"/> and the <see cref="GlobalBudget"/>.
/// Section 7.3 defines one ordered stream of sources for the whole invocation, spanning the scheme
/// phase and the input phase, so the budget it charges necessarily outlives a single step.
/// </para>
/// </remarks>
public static class Transformation
{
    /// <summary>Runs the pipeline.</summary>
    /// <param name="command">The parsed and validated command line.</param>
    /// <param name="reader">Where source bytes come from, or <c>null</c> for the file system.</param>
    /// <param name="sink">Where output bytes go, or <c>null</c> for the file system.</param>
    /// <param name="log">Where operational messages go, or <c>null</c> to discard them.</param>
    /// <returns>What the run produced.</returns>
    /// <remarks>
    /// The work happens on a thread with an explicitly sized stack. Several phases walk the overlay
    /// tree by recursion, so the depth a run can survive is a property of the stack it is given —
    /// and the ambient stack is whatever the host chose, which differs between platforms and
    /// between a thread-pool thread and a main thread. Sizing it here is what lets
    /// <see cref="LimitValue.MaxDepthCeiling"/> be a promise rather than a hope, and is also what
    /// keeps the promise identical on every supported platform.
    /// </remarks>
    public static TransformationResult Run(
        CommandLine command,
        ISourceReader? reader = null,
        IPublicationSink? sink = null,
        IOperationalLog? log = null) =>
        RunObserved(command, reader, sink, log, observer: null);

    internal static TransformationResult Run(
        CommandLine command,
        ISourceReader? reader,
        IPublicationSink? sink,
        IOperationalLog? log,
        IPipelineObserver observer) =>
        RunObserved(command, reader, sink, log, observer);

    private static TransformationResult RunObserved(
        CommandLine command,
        ISourceReader? reader,
        IPublicationSink? sink,
        IOperationalLog? log,
        IPipelineObserver? observer)
    {
        ArgumentNullException.ThrowIfNull(command);

        TransformationResult? result = null;
        ExceptionDispatchInfo? failure = null;

        var worker = new Thread(
            () =>
            {
                try
                {
                    result = RunCore(
                        command,
                        reader,
                        sink,
                        log ?? SilentOperationalLog.Instance,
                        observer);
                }
                catch (Exception error)
                {
                    failure = ExceptionDispatchInfo.Capture(error);
                }
            },
            PipelineStackBytes);

        worker.Start();
        worker.Join();

        failure?.Throw();

        return result!;
    }

    /// <summary>
    /// The stack the pipeline runs on, sized so that a document at
    /// <see cref="LimitValue.MaxDepthCeiling"/> completes every recursive phase.
    /// </summary>
    /// <remarks>
    /// A document of depth 1,000 completes in the 1 MiB the host gives a main thread by default,
    /// which puts the deepest phase near 1 KiB of stack per level. Sixteen mebibytes is therefore
    /// roughly a fourfold margin over the 4,096 ceiling, and costs nothing until it is touched:
    /// the value reserves address space, and pages are committed on demand.
    /// </remarks>
    private const int PipelineStackBytes = 16 * 1024 * 1024;

    private static TransformationResult RunCore(
        CommandLine command,
        ISourceReader? reader,
        IPublicationSink? sink,
        IOperationalLog log,
        IPipelineObserver? observer)
    {
        using var instrumentation = observer is null
            ? null
            : PipelineInstrumentation.Begin(observer);
        var run = new PipelineRun(log);
        var budget = new GlobalBudget(command.Limits);
        var loader = new SourceLoader(reader ?? new FileSystemSourceReader(), command.Limits, log);
        var start = PipelineRun.Seed(command);

        var schemes = run.Run(
            PipelineStep.ParseSchemes,
            start,
            (line, diagnostics) => SchemePhase.ParseSchemes(line, loader, budget, diagnostics));

        var options = run.Run(
            PipelineStep.CompileInputOptions,
            schemes,
            SchemePhase.CompileInputOptions);

        var substitutes = run.Run(
            PipelineStep.CompileSubstitutePatterns,
            options,
            (both, diagnostics) =>
                SchemePhase.CompileSubstitutePatterns(both.Entries, diagnostics));

        var configuration = run.Run(
            PipelineStep.CompileInputMerges,
            substitutes,
            (both, diagnostics) => SchemePhase.CompileInputMerges(both.Entries, diagnostics));

        var inputs = run.Run(
            PipelineStep.ParseInputs,
            PipelineRun.Both(PipelineRun.Both(start, options), substitutes),
            (both, diagnostics) => InputPhase.ParseInputs(
                both.First.First,
                loader,
                budget,
                both.First.Second.Options,
                both.Second.Substitutes,
                diagnostics));
        ObserveInputEnvelopes(PipelineStep.ParseInputs, inputs);

        // Steps 6 and 7 are performed by the reader as it parses: it lexes every value and returns
        // templates and masks separately from concrete contributions. They are still run as steps
        // so the order stays checkable and a later implementation has a place to live.
        var lexed = run.Run(
            PipelineStep.ValidateReferenceSyntax,
            inputs,
            (contributions, _) => StepOutcome.Produced(contributions));

        var extracted = run.Run(
            PipelineStep.ExtractTemplatesAndMasks,
            lexed,
            (contributions, _) => StepOutcome.Produced(contributions));

        var merged = run.Run(
            PipelineStep.MergeContributions,
            PipelineRun.Both(extracted, configuration),
            (both, diagnostics) =>
                InputPhase.MergeContributions(both.First, both.Second, diagnostics));

        var exposed = run.Run(
            PipelineStep.ExposeOrderingValues,
            PipelineRun.Both(merged, configuration),
            (both, diagnostics) =>
                InputPhase.ExposeOrderingValues(both.First, both.Second, diagnostics));

        var evaluated = run.Run(
            PipelineStep.EvaluateTemplates,
            PipelineRun.Both(PipelineRun.Both(exposed, extracted), configuration),
            (both, diagnostics) => InputPhase.EvaluateTemplates(
                both.First.First, both.First.Second, both.Second, budget, diagnostics));

        var projected = run.Run(
            PipelineStep.InferSequences,
            evaluated,
            (overlay, _) => InputPhase.InferSequences(overlay));

        var model = run.Run(
            PipelineStep.InferScalarKinds,
            projected,
            (overlay, _) => InputPhase.InferScalarKinds(overlay));

        var instances = run.Run(
            PipelineStep.ExpandWildcards,
            PipelineRun.Both(configuration, model),
            (both, diagnostics) =>
                PlanningPhase.ExpandWildcards(both.First, both.Second, budget, diagnostics));

        var views = run.Run(
            PipelineStep.BuildOutputInstances,
            PipelineRun.Both(instances, model),
            (both, diagnostics) =>
                PlanningPhase.BuildOutputInstances(both.First, both.Second, diagnostics));
        ObserveViewEnvelopes(PipelineStep.BuildOutputInstances, views);

        var resolved = run.Run(
            PipelineStep.ResolveReferences,
            PipelineRun.Both(
                PipelineRun.Both(views, model),
                PipelineRun.Both(extracted, configuration)),
            (both, diagnostics) => PlanningPhase.ResolveReferences(
                both.First.First,
                both.First.Second,
                both.Second.First,
                both.Second.Second,
                budget,
                diagnostics));
        ObserveViewEnvelopes(PipelineStep.ResolveReferences, resolved);

        var transformed = run.Run(
            PipelineStep.ApplyTransformations,
            PipelineRun.Both(resolved, configuration),
            (both, diagnostics) =>
                PlanningPhase.ApplyTransformations(both.First, both.Second, diagnostics));
        ObserveViewEnvelopes(PipelineStep.ApplyTransformations, transformed);

        var grouped = run.Run(
            PipelineStep.GroupByDestination,
            transformed,
            PlanningPhase.GroupByDestination);

        var folded = run.Run(
            PipelineStep.FoldDestinationCollisions,
            grouped,
            (contributions, diagnostics) =>
                PlanningPhase.FoldDestinationCollisions(contributions, budget, diagnostics));

        var serialized = run.Run(
            PipelineStep.Serialize,
            folded,
            (contributions, diagnostics) =>
                PublicationPhase.Serialize(contributions, budget, diagnostics));

        var warningPolicyTriggered =
            serialized is not null && command.FailOnWarning && run.Diagnostics.HasWarning;

        var published = run.Run(
            PipelineStep.Publish,
            serialized,
            (outputs, diagnostics) =>
                warningPolicyTriggered
                    ? StepOutcome.Produced(0)
                    : PublicationPhase.Publish(outputs, command.OutputRoot, diagnostics, sink, log));

        return new TransformationResult(
            run.Diagnostics.Drain(),
            run.State,
            published?.Value ?? 0,
            warningPolicyTriggered);
    }

    private static void ObserveInputEnvelopes(
        PipelineStep step,
        StepProduct<ImmutableArray<InputContribution>>? inputs)
    {
        if (!PipelineInstrumentation.IsEnabled || inputs is null)
        {
            return;
        }

        foreach (var contribution in inputs.Value)
        {
            PipelineInstrumentation.RecordEnvelope(new EnvelopeSnapshot(
                step,
                new EnvelopeProductIdentity(
                    "input-contribution",
                    SourceIdentity: contribution.Origin.Identity,
                    SourceOrdinal: contribution.Origin.Ordinal),
                contribution.Contribution.XmlEnvelopeComments));
        }
    }

    private static void ObserveViewEnvelopes(
        PipelineStep step,
        StepProduct<ImmutableArray<OutputView>>? views)
    {
        if (!PipelineInstrumentation.IsEnabled || views is null)
        {
            return;
        }

        foreach (var view in views.Value)
        {
            PipelineInstrumentation.RecordEnvelope(new EnvelopeSnapshot(
                step,
                new EnvelopeProductIdentity(
                    "output-view",
                    Selector: view.Instance.Selector.ToString(),
                    Format: view.Format,
                    FormatOrdinal: view.FormatOrdinal),
                view.XmlEnvelopeComments));
        }
    }
}
