using Namespace2Xml.Diagnostics;

namespace Namespace2Xml.Pipeline;

/// <summary>
/// The twenty steps of the normative processing pipeline, specification Section 15.1.
/// </summary>
/// <remarks>
/// <para>
/// The numeric value of each member is its specification step number, so <c>(int)step</c> is the
/// number a reader looks up in Section 15.1 and a renumbering of this enum is a visible change
/// rather than a silent one.
/// </para>
/// <para>
/// Section 15.1 closes with "this phase order is acyclic". That sentence is the whole reason the
/// steps are an enumeration instead of a set of method calls: an ordering that exists only as the
/// order statements happen to appear in a driver method is an ordering no test can interrogate.
/// </para>
/// </remarks>
public enum PipelineStep
{
    /// <summary>Parse scheme syntax and resolve references among scheme entries.</summary>
    ParseSchemes = 1,

    /// <summary>Compile root-level input options.</summary>
    CompileInputOptions = 2,

    /// <summary>Compile <c>substitute</c> path patterns.</summary>
    CompileSubstitutePatterns = 3,

    /// <summary>Compile literal-path input <c>merge</c> directives.</summary>
    CompileInputMerges = 4,

    /// <summary>Parse input documents into typed overlays.</summary>
    ParseInputs = 5,

    /// <summary>Lex and validate reference and value-wildcard syntax in strings.</summary>
    ValidateReferenceSyntax = 6,

    /// <summary>Extract wildcard template entries and permanent exclusion masks.</summary>
    ExtractTemplatesAndMasks = 7,

    /// <summary>Fold entries within each contribution, then merge contributions in source order.</summary>
    MergeContributions = 8,

    /// <summary>Expose native-sequence ordering values and numeric mapping keys as path parts.</summary>
    ExposeOrderingValues = 9,

    /// <summary>Evaluate templates under the permanent exclusion masks to a fixed point.</summary>
    EvaluateTemplates = 10,

    /// <summary>Project remaining inferable mappings as explicit indexed sequences.</summary>
    InferSequences = 11,

    /// <summary>Infer locale-independent scalar kinds for remaining untyped payloads.</summary>
    InferScalarKinds = 12,

    /// <summary>Expand wildcard scheme selectors and output declarations.</summary>
    ExpandWildcards = 13,

    /// <summary>Build concrete output instances and apply strict prefix filtering.</summary>
    BuildOutputInstances = 14,

    /// <summary>Compute each output instance's reference closure and resolve within it.</summary>
    ResolveReferences = 15,

    /// <summary>Apply path-scoped transformations to each selected output view.</summary>
    ApplyTransformations = 16,

    /// <summary>Group fully transformed output contributions by canonical destination path.</summary>
    GroupByDestination = 17,

    /// <summary>Fold same-format destination collisions and resolve cross-format overrides.</summary>
    FoldDestinationCollisions = 18,

    /// <summary>Serialize all planned destinations into immutable in-memory byte buffers.</summary>
    Serialize = 19,

    /// <summary>Publish destinations directly in deterministic order.</summary>
    Publish = 20,
}

/// <summary>
/// Facts about <see cref="PipelineStep"/> fixed by specification Sections 15.1 and 6.4.3.
/// </summary>
public static class PipelineSteps
{
    /// <summary>The first step of the pipeline, Section 15.1 step 1.</summary>
    public const PipelineStep First = PipelineStep.ParseSchemes;

    /// <summary>The last step of the pipeline, Section 15.1 step 20.</summary>
    public const PipelineStep Last = PipelineStep.Publish;

    /// <summary>Every step in specification order.</summary>
    public static IReadOnlyList<PipelineStep> All { get; } = Enum.GetValues<PipelineStep>();

    /// <summary>The Section 15.1 step number, which is also the enum's numeric value.</summary>
    /// <param name="step">The step.</param>
    /// <returns>A number from 1 to 20.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined step.</exception>
    public static int Number(this PipelineStep step) => IsDefined(step)
        ? (int)step
        : throw new ArgumentOutOfRangeException(nameof(step), step, "Not a Section 15.1 step.");

    /// <summary>
    /// The phase a diagnostic detected in this step reports, per the Section 6.4.3 ranges.
    /// </summary>
    /// <remarks>
    /// Derived rather than supplied. Phase is a required field of every diagnostic and the
    /// conformance harness compares it exactly; letting each emission site name its own phase makes
    /// the one site that names the wrong one indistinguishable from the rest until a fixture happens
    /// to cover it. There is no <see cref="DiagnosticPhase.Cli"/> step because Section 6.4.3 places
    /// that phase before step 1.
    /// </remarks>
    /// <param name="step">The step.</param>
    /// <returns>The phase that step belongs to.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined step.</exception>
    public static DiagnosticPhase Phase(this PipelineStep step) => step switch
    {
        >= PipelineStep.ParseSchemes and <= PipelineStep.CompileInputMerges => DiagnosticPhase.Scheme,
        >= PipelineStep.ParseInputs and <= PipelineStep.InferScalarKinds => DiagnosticPhase.Input,
        >= PipelineStep.ExpandWildcards and <= PipelineStep.Serialize => DiagnosticPhase.Planning,
        PipelineStep.Publish => DiagnosticPhase.Publication,
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, "Not a Section 15.1 step."),
    };

    /// <summary>The step that must run immediately before this one, or <c>null</c> for step 1.</summary>
    /// <param name="step">The step.</param>
    /// <returns>The preceding step, or <c>null</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined step.</exception>
    public static PipelineStep? Previous(this PipelineStep step) =>
        step.Number() == 1 ? null : (PipelineStep)((int)step - 1);

    /// <summary>The step that must run immediately after this one, or <c>null</c> for step 20.</summary>
    /// <param name="step">The step.</param>
    /// <returns>The following step, or <c>null</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined step.</exception>
    public static PipelineStep? Next(this PipelineStep step) =>
        step.Number() == (int)Last ? null : (PipelineStep)((int)step + 1);

    /// <summary>Whether this step is the last of its phase, so a phase boundary follows it.</summary>
    /// <param name="step">The step.</param>
    /// <returns><c>true</c> when the next step belongs to a different phase or there is none.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined step.</exception>
    public static bool EndsPhase(this PipelineStep step) =>
        step.Next() is not { } next || next.Phase() != step.Phase();

    /// <summary>Whether the value is one of the twenty defined steps.</summary>
    /// <param name="step">The value to test.</param>
    /// <returns><c>true</c> when defined.</returns>
    public static bool IsDefined(PipelineStep step) =>
        step >= First && step <= Last;
}
