namespace Namespace2Xml.Pipeline;

/// <summary>
/// A value produced by a specification Section 15.1 step, tagged with the step that produced it.
/// </summary>
/// <remarks>
/// The tag is what makes Section 15.1's "this phase order is acyclic" checkable. A step declares
/// which earlier product it consumes, and <see cref="PipelineRun"/> refuses a product from a step
/// that has not run yet; a pipeline that reads step 13's expansion while computing step 11's
/// projection therefore fails on its first execution rather than producing a plausible wrong tree.
/// </remarks>
/// <typeparam name="T">The product type.</typeparam>
/// <param name="Step">The producing step, or <c>null</c> for material available before step 1.</param>
/// <param name="Value">The product.</param>
public sealed record StepProduct<T>(PipelineStep? Step, T Value);

/// <summary>What a Section 15.1 step returns.</summary>
/// <remarks>
/// A step either produced its result or did not. Diagnostics travel in the buffer the step was
/// given rather than here, because Section 15.4 requires a phase to buffer a deterministic
/// diagnostic set whether or not the individual step succeeded.
/// </remarks>
/// <typeparam name="T">The product type.</typeparam>
public readonly record struct StepOutcome<T>
{
    internal StepOutcome(bool faulted, T? value, UnsupportedCapability? unsupported)
    {
        Faulted = faulted;
        Value = value;
        Unsupported = unsupported;
    }

    /// <summary>Whether the step failed to produce a result.</summary>
    public bool Faulted { get; }

    /// <summary>The product, or <c>default</c> when <see cref="Faulted"/>.</summary>
    public T? Value { get; }

    /// <summary>The capability this build lacks, when the step declined to run at all.</summary>
    public UnsupportedCapability? Unsupported { get; }
}

/// <summary>Creates <see cref="StepOutcome{T}"/> values.</summary>
public static class StepOutcome
{
    /// <summary>The step produced its result.</summary>
    /// <typeparam name="T">The product type.</typeparam>
    /// <param name="value">The product.</param>
    /// <returns>A successful outcome.</returns>
    public static StepOutcome<T> Produced<T>(T value) => new(faulted: false, value, unsupported: null);

    /// <summary>The step did not produce a usable result.</summary>
    /// <remarks>
    /// Section 15.4: a failed source contributes no partial overlay, a failed scheme contributes no
    /// partial directives, and transformation and planning errors never produce a partial output
    /// instance. There is deliberately no way to fault and still return a value.
    /// </remarks>
    /// <typeparam name="T">The product type.</typeparam>
    /// <returns>A failed outcome.</returns>
    public static StepOutcome<T> Failed<T>() => new(faulted: true, value: default, unsupported: null);

    /// <summary>
    /// The step declined to run because the invocation needs a capability this build does not have.
    /// </summary>
    /// <typeparam name="T">The product type.</typeparam>
    /// <param name="capability">The missing capability.</param>
    /// <returns>An outcome that stops the run without deciding a Section 6.3 exit code.</returns>
    public static StepOutcome<T> Unsupported<T>(UnsupportedCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);

        return new StepOutcome<T>(faulted: true, value: default, capability);
    }
}

/// <summary>The state of a <see cref="PipelineRun"/>.</summary>
public enum PipelineRunState
{
    /// <summary>Steps remain and no phase boundary has aborted the run.</summary>
    Running,

    /// <summary>A phase ended with a blocking diagnostic, so no later phase runs.</summary>
    Aborted,

    /// <summary>Every step has run.</summary>
    Finished,

    /// <summary>
    /// A step declined because the invocation needs a capability this build does not have. The run
    /// decides no Section 6.3 outcome at all: it did not happen.
    /// </summary>
    Unsupported,
}

/// <summary>
/// The specification Section 15.4 driver loop: it runs the Section 15.1 steps in order, buffers
/// each phase's diagnostics, and stops at the phase boundary after a blocking diagnostic.
/// </summary>
/// <remarks>
/// <para>
/// Error recovery lives here so that it exists once. Section 15.4 asks for two different behaviours
/// that are easy to confuse: within a phase, work that does not depend on a failed result still
/// runs, so its diagnostics are collected; between phases, nothing runs at all once a blocking
/// diagnostic exists. Written as checks inside the steps, the first becomes "remember to continue"
/// and the second becomes "remember to stop", and both are forgotten in the step added last.
/// </para>
/// <para>
/// Dependency is expressed by data rather than by a flag: a failed step returns <c>null</c>, and a
/// step handed <c>null</c> does not run. A step whose inputs all survived runs even if a sibling in
/// the same phase failed, which is exactly Section 15.4's "every independent check that does not
/// depend on a failed result".
/// </para>
/// </remarks>
public sealed class PipelineRun
{
    private PipelineStep? completed;

    /// <summary>Every diagnostic buffered so far, in Section 24 emission order when drained.</summary>
    public DiagnosticBuffer Diagnostics { get; } = new();

    /// <summary>The most recently attempted step, whether it ran or was skipped.</summary>
    public PipelineStep? LastStep => completed;

    /// <summary>The step whose phase boundary aborted the run, or <c>null</c> if none has.</summary>
    public PipelineStep? AbortedAfter { get; private set; }

    /// <summary>
    /// The capability that stopped the run, or <c>null</c> when every step this run reached was one
    /// this build implements.
    /// </summary>
    public UnsupportedCapability? Unsupported { get; private set; }

    /// <summary>Where the run stands under Section 15.4.</summary>
    public PipelineRunState State { get; private set; } = PipelineRunState.Running;

    /// <summary>Wraps material available before Section 15.1 step 1, such as the parsed command line.</summary>
    /// <typeparam name="T">The product type.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>A product tagged as preceding every step.</returns>
    public static StepProduct<T> Seed<T>(T value) => new(Step: null, value);

    /// <summary>
    /// Pairs two products so that a step consuming both can be expressed as a single
    /// <see cref="Run{TIn, TOut}"/>.
    /// </summary>
    /// <remarks>
    /// Step 8 consumes the source contributions and the effective merge strategy; step 14 consumes
    /// the merged model and the compiled scheme. The pair is tagged with the <em>later</em> of the
    /// two producing steps so that the cycle check in <see cref="Run{TIn, TOut}"/> still sees the
    /// most recent dependency: tagging it with the earlier one would let a step consume a product
    /// from its own future as long as it also consumed something old.
    /// </remarks>
    /// <typeparam name="T1">The first product type.</typeparam>
    /// <typeparam name="T2">The second product type.</typeparam>
    /// <param name="first">The first product, or <c>null</c> when its step failed or was skipped.</param>
    /// <param name="second">The second product, or <c>null</c> when its step failed or was skipped.</param>
    /// <returns>The paired product, or <c>null</c> when either input is absent.</returns>
    public static StepProduct<(T1 First, T2 Second)>? Both<T1, T2>(
        StepProduct<T1>? first,
        StepProduct<T2>? second)
    {
        if (first is null || second is null)
        {
            return null;
        }

        var step = (first.Step, second.Step) switch
        {
            (null, var b) => b,
            (var a, null) => a,
            var (a, b) => a > b ? a : b,
        };

        return new StepProduct<(T1, T2)>(step, (first.Value, second.Value));
    }

    /// <summary>
    /// Runs one Section 15.1 step, or skips it when the product it consumes was not produced.
    /// </summary>
    /// <typeparam name="TIn">The consumed product type.</typeparam>
    /// <typeparam name="TOut">The produced product type.</typeparam>
    /// <param name="step">The step to run. Must be the one following <see cref="LastStep"/>.</param>
    /// <param name="input">
    /// The product this step consumes, from any earlier step, or <c>null</c> when the step that
    /// would have produced it failed or was itself skipped.
    /// </param>
    /// <param name="body">The step, given its input and a buffer for its own diagnostics.</param>
    /// <returns>The step's product, or <c>null</c> when it failed, was skipped, or the run had aborted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The step is out of Section 15.1 order, the input comes from a step that has not run, or the
    /// step reported failure without a blocking diagnostic.
    /// </exception>
    public StepProduct<TOut>? Run<TIn, TOut>(
        PipelineStep step,
        StepProduct<TIn>? input,
        Func<TIn, DiagnosticBuffer, StepOutcome<TOut>> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (completed == PipelineSteps.Last)
        {
            throw new InvalidOperationException(
                $"Section 15.1 ends at {PipelineSteps.Last} (step {PipelineSteps.Last.Number()}); "
                + $"{step} would be a twenty-first.");
        }

        var expected = completed?.Next() ?? PipelineSteps.First;
        if (step != expected)
        {
            throw new InvalidOperationException(
                $"Section 15.1 orders {expected} (step {expected.Number()}) next, not {step} "
                + $"(step {step.Number()}). The phase order is acyclic and is not a suggestion.");
        }

        if (input?.Step is { } producer && producer >= step)
        {
            throw new InvalidOperationException(
                $"{step} (step {step.Number()}) consumes a product of {producer} "
                + $"(step {producer.Number()}), which Section 15.1 places no earlier. That is a cycle.");
        }

        completed = step;

        if (State != PipelineRunState.Running || input is null)
        {
            CloseStep(step);
            return null;
        }

        var stepDiagnostics = new DiagnosticBuffer();
        var outcome = body(input.Value, stepDiagnostics);

        if (outcome.Unsupported is { } capability)
        {
            // Deliberately before the blocking-diagnostic check: a step that declines has not
            // examined the input closely enough to have an opinion about it, and inventing a
            // diagnostic here would put a code in the stream that Section 22 does not define.
            Diagnostics.Merge(stepDiagnostics);
            Unsupported = capability;
            State = PipelineRunState.Unsupported;
            return null;
        }

        if (outcome.Faulted && !stepDiagnostics.HasBlockingError)
        {
            throw new InvalidOperationException(
                $"{step} (step {step.Number()}) reported failure without buffering a blocking "
                + "diagnostic. Section 15.4 aborts on blocking diagnostics, so a silent failure "
                + "would let the run continue past a step that produced nothing.");
        }

        Diagnostics.Merge(stepDiagnostics);
        CloseStep(step);

        return outcome.Faulted ? null : new StepProduct<TOut>(step, outcome.Value!);
    }

    /// <summary>
    /// Applies the Section 15.4 phase gate after a step, and marks the run finished after step 20.
    /// </summary>
    private void CloseStep(PipelineStep step)
    {
        if (State != PipelineRunState.Running)
        {
            return;
        }

        if (step.Next() is null)
        {
            State = PipelineRunState.Finished;
            return;
        }

        if (step.EndsPhase() && Diagnostics.HasBlockingError)
        {
            State = PipelineRunState.Aborted;
            AbortedAfter = step;
        }
    }
}
