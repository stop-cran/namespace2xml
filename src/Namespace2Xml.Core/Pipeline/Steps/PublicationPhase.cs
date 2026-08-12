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

        if (contributions.IsEmpty)
        {
            // Section 21.2 contemplates this plan rather than forbidding it -- "A zero-destination
            // plan does not create it" is said of the output root -- so Section 22 lists it among
            // the warnings, once per invocation. It carries no member at all: there is no source
            // to blame and, by construction, no destination to name.
            diagnostics.Add(new BufferedDiagnostic(
                DiagnosticCodes.Warn008(
                    DiagnosticPhase.Planning,
                    "\u00A721.2",
                    "the validated output plan contains no destinations, so this run writes no "
                    + "files. Every scheme directive was accepted; none of them declared an "
                    + "output.")));
        }

        var planned = ImmutableArray.CreateBuilder<PlannedOutput>(contributions.Length);
        var order = 0;

        // Section 21.3's destination order is "by publication key, then by canonical relative path
        // compared as unsigned UTF-8 bytes". The key alone is not a total order — one declaration
        // can write two destinations — and the index assigned here is what Section 24 orders this
        // step's diagnostics by, so a tie left to enumeration order would be a diagnostic order
        // that depends on a dictionary. Step 18 numbers its own destination diagnostics from the
        // same expression, so the two steps cannot disagree about one run's stream.
        foreach (var contribution in DestinationContribution.InPublicationOrder(contributions))
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
    /// <param name="log">Where Section 21.4 replacement messages go.</param>
    /// <returns>How many destinations were written.</returns>
    public static StepOutcome<int> Publish(
        ImmutableArray<PlannedOutput> planned,
        string outputRoot,
        DiagnosticBuffer diagnostics,
        IPublicationSink? sink = null,
        IOperationalLog? log = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputRoot);
        ArgumentNullException.ThrowIfNull(diagnostics);

        return new Publisher(outputRoot, diagnostics, sink, log).TryPublish(planned)
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
        var destination = new DestinationRef(contribution.Path.Canonical, order);
        var writer = new OutputBufferWriter(budget);

        if (!TryWrite(view, diagnostics, destination, writer))
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
                // Section 6.4.3 maps planning to "Section 15.1 steps 13 through 19" and publication
                // to step 20 alone. Building the immutable byte buffer is the tail of step 19,
                // "serialize all planned destinations into immutable in-memory byte buffers", so a
                // failure here is a planning diagnostic exactly as one raised inside a serializer
                // is. The enclosing class is named for the step it ends with, not for this one.
                DiagnosticPhase.Planning,
                "\u00A724",
                $"'{destination.Canonical}' could not be serialized: {violation}",
                cardinalityKey: destination.Canonical,
                destination: destination.Canonical),
            DestinationOrder: order));

        return false;
    }

    /// <summary>
    /// Section 15.1 step 19: renders one view through the serializer its format names.
    /// </summary>
    /// <remarks>
    /// The flat formats project to Section 19.1 keyed entries and the structured formats to a
    /// Section 19.3 document, and the two projections differ in more than layout: Section 4.4 makes
    /// JSON and YAML exclusive-shape destinations where the payload competes with the container,
    /// while a flat output emits both facets of one node.
    /// </remarks>
    private static bool TryWrite(
        OutputView view,
        DiagnosticBuffer diagnostics,
        DestinationRef destination,
        OutputBufferWriter writer)
    {
        if (view.Format.TryAsFlat(out var flat))
        {
            var delimiter = view.Instance.Delimiter ?? FlatKeyProjector.DefaultDelimiter(flat);
            var projected = new FlatProjection(diagnostics, destination).Project(view.View, view.Root);
            var keyed = new FlatKeyProjector(flat, delimiter, diagnostics, destination).Project(projected);

            // INI takes only the entries: Section 20 gives it a placement rule of its own, and the
            // half covering document-trailing and inline comments — "normalized to full-line
            // comments at the nearest deterministic leading position" — does not name a position an
            // implementation could agree on. Guessing one here would fix a spelling the
            // specification has not chosen.
            return flat == FlatFormat.Ini
                ? new IniSerializer(view.Instance.IniOptions, diagnostics, destination)
                    .TrySerialize(keyed.Entries, writer)
                : new FlatTextSerializer(flat, delimiter, diagnostics, destination)
                    .TrySerialize(keyed, writer);
        }

        if (view.Format == OutputFormat.Xml)
        {
            var element = new XmlProjection(
                    diagnostics,
                    destination,
                    view.Types,
                    view.AppliedRoot.Length,
                    view.Instance.XmlOptions)
                .Project(view.View, view.Root);

            return element is not null
                && new XmlSerializer(view.Instance.XmlOptions, diagnostics, destination)
                    .TrySerialize(element, writer);
        }

        if (view.Format is not (OutputFormat.Json or OutputFormat.Yaml))
        {
            throw new InvalidOperationException(
                $"step 14 admitted {view.Format}, which has no serializer.");
        }

        var anchor = view.Format == OutputFormat.Json ? "\u00A719.3" : "\u00A719.4";
        var document = new DocumentProjection(
                diagnostics,
                anchor,
                view.Types,
                view.AppliedRoot.Length,
                destination)
            .Project(view.View, view.Root);

        return view.Format == OutputFormat.Json
            ? new JsonSerializer(view.Instance.JsonOptions, diagnostics, destination)
                .TrySerialize(document, writer)
            : new YamlSerializer(view.Instance.YamlOptions, diagnostics, destination)
                .TrySerialize(document, writer);
    }
}
