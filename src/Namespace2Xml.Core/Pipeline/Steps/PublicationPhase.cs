using System.Collections.Immutable;
using Namespace2Xml.Budgets;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Output;
using Namespace2Xml.Scheme;

namespace Namespace2Xml.Pipeline.Steps;

/// <summary>
/// Specification Section 15.1 steps 19 and 20: the publication phase.
/// </summary>
public static class PublicationPhase
{
    /// <summary>Section 15.1 step 19: serialize every planned destination into a byte buffer.</summary>
    /// <param name="contributions">Step 18's product.</param>
    /// <param name="budget">The invocation's global budget, which bounds total output bytes.</param>
    /// <param name="diagnostics">This step's buffer.</param>
    /// <returns>The planned outputs, in Section 21.3 publication order.</returns>
    /// <remarks>
    /// Every destination is serialized before any is published, which is Section 21.2's rule that a
    /// complete buffer exists first. A serializer that failed halfway would otherwise leave a
    /// truncated file that looks like a successful run of a smaller model.
    /// </remarks>
    public static StepOutcome<ImmutableArray<PlannedOutput>> Serialize(
        ImmutableArray<DestinationContribution> contributions,
        GlobalBudget budget,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var planned = ImmutableArray.CreateBuilder<PlannedOutput>(contributions.Length);
        var order = 0;

        foreach (var contribution in contributions.OrderBy(c => c.Key))
        {
            if (TrySerialize(contribution, budget, diagnostics, order, out var buffer))
            {
                planned.Add(new PlannedOutput(contribution.Path, contribution.Key, buffer));
            }

            order++;
        }

        return diagnostics.HasBlockingError
            ? StepOutcome.Failed<ImmutableArray<PlannedOutput>>()
            : StepOutcome.Produced(PlannedOutput.InPublicationOrder(planned));
    }

    /// <summary>Section 15.1 step 20: publish destinations in deterministic order.</summary>
    /// <param name="planned">Step 19's product.</param>
    /// <param name="outputRoot">The configured output root.</param>
    /// <param name="diagnostics">This step's buffer.</param>
    /// <param name="sink">Where bytes go, or <c>null</c> for the file system.</param>
    /// <returns>How many destinations were written.</returns>
    public static StepOutcome<int> Publish(
        ImmutableArray<PlannedOutput> planned,
        string outputRoot,
        DiagnosticBuffer diagnostics,
        IPublicationSink? sink = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputRoot);
        ArgumentNullException.ThrowIfNull(diagnostics);

        return new Publisher(outputRoot, diagnostics, sink).TryPublish(planned)
            ? StepOutcome.Produced(planned.Length)
            : StepOutcome.Failed<int>();
    }

    private static bool TrySerialize(
        DestinationContribution contribution,
        GlobalBudget budget,
        DiagnosticBuffer diagnostics,
        int order,
        out OutputBuffer buffer)
    {
        buffer = OutputBuffer.Empty;

        var view = contribution.View;
        var destination = contribution.Path.Canonical;

        if (!view.Format.TryAsFlat(out var flat))
        {
            throw new InvalidOperationException(
                $"step 14 admitted {view.Format}, which has no flat projection.");
        }

        var delimiter = view.Instance.Delimiter ?? FlatKeyProjector.DefaultDelimiter(flat);
        var entries = new FlatProjection(diagnostics, destination).Project(view.View, view.Root);
        var keyed = new FlatKeyProjector(flat, delimiter, diagnostics, destination).Project(entries);
        var writer = new OutputBufferWriter(budget);

        var written = flat == FlatFormat.Ini
            ? new IniSerializer(view.Instance.IniOptions, diagnostics, destination)
                .TrySerialize(keyed, writer)
            : new FlatTextSerializer(flat, delimiter, diagnostics, destination)
                .TrySerialize(keyed, writer);

        if (!written)
        {
            return false;
        }

        // Section 24's termination rule "applies to output that has content", so an empty flat
        // output is zero bytes and satisfies it vacuously. That is decided once, by
        // OutputBuffer.IsWellTerminatedText; restating it here would be a second copy of a
        // normative rule, and the copy that drifted would be the one with no test.
        if (writer.TryBuildText(out buffer, out var violation))
        {
            return true;
        }

        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Serialize001(
                DiagnosticPhase.Publication,
                "\u00A724",
                $"'{destination}' could not be serialized: {violation}",
                cardinalityKey: destination,
                destination: destination),
            DestinationOrder: order));

        return false;
    }
}
