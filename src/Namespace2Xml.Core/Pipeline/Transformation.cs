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
/// <param name="Unsupported">The capability that stopped it, when one did.</param>
/// <param name="Published">How many destinations were written.</param>
public sealed record TransformationResult(
    ImmutableArray<Diagnostic> Diagnostics,
    PipelineRunState State,
    UnsupportedCapability? Unsupported,
    int Published)
{
    /// <summary>
    /// The Section 6.3 exit code, or <see langword="null"/> when the run declined and so decided no
    /// outcome at all.
    /// </summary>
    /// <remarks>
    /// Section 6.3 gives <c>0</c> to a run that completed with no blocking diagnostic and <c>1</c>
    /// to one that produced any. A warning does not change the code, so the test is on severity and
    /// not on count.
    /// </remarks>
    public int? ExitCode => State == PipelineRunState.Unsupported
        ? null
        : Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) ? 1 : 0;
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
        IPublicationSink? sink = null)
    {
        ArgumentNullException.ThrowIfNull(command);

        TransformationResult? result = null;
        ExceptionDispatchInfo? failure = null;

        var worker = new Thread(
            () =>
            {
                try
                {
                    result = RunCore(command, reader, sink);
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
        IPublicationSink? sink)
    {
        var run = new PipelineRun();
        var budget = new GlobalBudget(command.Limits);
        var loader = new SourceLoader(reader ?? new FileSystemSourceReader(), command.Limits);
        var start = PipelineRun.Seed(command);

        var schemes = run.Run(
            PipelineStep.ParseSchemes,
            start,
            (line, diagnostics) => SchemePhase.ParseSchemes(line, loader, budget, diagnostics));

        var options = run.Run(
            PipelineStep.CompileInputOptions,
            schemes,
            (entries, _) => SchemePhase.CompileInputOptions(entries));

        var substitutes = run.Run(
            PipelineStep.CompileSubstitutePatterns,
            options,
            (entries, _) => SchemePhase.CompileSubstitutePatterns(entries));

        var configuration = run.Run(
            PipelineStep.CompileInputMerges,
            substitutes,
            SchemePhase.CompileInputMerges);

        var inputs = run.Run(
            PipelineStep.ParseInputs,
            start,
            (line, diagnostics) => InputPhase.ParseInputs(line, loader, budget, diagnostics));

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
            PipelineRun.Both(exposed, extracted),
            (both, _) => InputPhase.EvaluateTemplates(both.First, both.Second));

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
            configuration,
            (compiled, _) => PlanningPhase.ExpandWildcards(compiled));

        var views = run.Run(
            PipelineStep.BuildOutputInstances,
            PipelineRun.Both(instances, model),
            (both, diagnostics) =>
                PlanningPhase.BuildOutputInstances(both.First, both.Second, diagnostics));

        var resolved = run.Run(
            PipelineStep.ResolveReferences,
            PipelineRun.Both(views, extracted),
            (both, _) => PlanningPhase.ResolveReferences(both.First, both.Second));

        var transformed = run.Run(
            PipelineStep.ApplyTransformations,
            PipelineRun.Both(resolved, configuration),
            (both, _) => PlanningPhase.ApplyTransformations(both.First, both.Second));

        var grouped = run.Run(
            PipelineStep.GroupByDestination,
            transformed,
            PlanningPhase.GroupByDestination);

        var folded = run.Run(
            PipelineStep.FoldDestinationCollisions,
            grouped,
            (contributions, _) => PlanningPhase.FoldDestinationCollisions(contributions));

        var serialized = run.Run(
            PipelineStep.Serialize,
            folded,
            (contributions, diagnostics) =>
                PublicationPhase.Serialize(contributions, budget, diagnostics));

        var published = run.Run(
            PipelineStep.Publish,
            serialized,
            (outputs, diagnostics) =>
                PublicationPhase.Publish(outputs, command.OutputRoot, diagnostics, sink));

        return new TransformationResult(
            run.Diagnostics.Drain(),
            run.State,
            run.Unsupported,
            published?.Value ?? 0);
    }
}
