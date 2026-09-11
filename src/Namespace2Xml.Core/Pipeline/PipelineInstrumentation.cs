using System.Collections.Immutable;
using Namespace2Xml.Overlay;
using Namespace2Xml.Scheme;

namespace Namespace2Xml.Pipeline;

internal enum PipelineObservationKind
{
    ParseNode,
    MergeNode,
    FilterNode,
    SequenceInferenceNode,
    ScalarInferenceNode,
    PlanningContribution,
    PathPart,
    MaskCandidate,
    WildcardCandidate,
    WildcardMatch,
    GeneratedNode,
    WildcardPostDiscovery,
    ReferenceClosure,
    ReferenceEdge,
    ReferenceNode,
    RenderedBytes,
    OutputSelector,
    PathDirective,
    EnvelopeSnapshot,
}

internal readonly record struct EnvelopeProductIdentity(
    string Kind,
    string? SourceIdentity = null,
    long? SourceOrdinal = null,
    string? Selector = null,
    OutputFormat? Format = null,
    int? FormatOrdinal = null);

internal readonly record struct EnvelopeSnapshot(
    PipelineStep Step,
    EnvelopeProductIdentity Product,
    ImmutableArray<XmlEnvelopeComment> Comments);

internal readonly record struct PipelineObservation(
    PipelineObservationKind Kind,
    string? Path = null,
    string? Owner = null,
    string? Target = null,
    long Amount = 1,
    bool Accepted = true,
    ImmutableArray<string> Values = default,
    EnvelopeSnapshot? Envelope = null);

internal interface IPipelineObserver
{
    void Observe(PipelineObservation observation);
}

internal static class PipelineInstrumentation
{
    private static readonly AsyncLocal<IPipelineObserver?> CurrentObserver = new();

    internal static bool IsEnabled => CurrentObserver.Value is not null;

    internal static IDisposable Begin(IPipelineObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);

        var previous = CurrentObserver.Value;
        CurrentObserver.Value = observer;
        return new ObservationScope(previous);
    }

    internal static void Record(
        PipelineObservationKind kind,
        string? path = null,
        string? owner = null,
        string? target = null,
        long amount = 1,
        bool accepted = true)
    {
        CurrentObserver.Value?.Observe(
            new PipelineObservation(kind, path, owner, target, amount, accepted));
    }

    internal static void RecordEnvelope(EnvelopeSnapshot snapshot)
    {
        if (CurrentObserver.Value is not { } observer)
        {
            return;
        }

        observer.Observe(new PipelineObservation(
            PipelineObservationKind.EnvelopeSnapshot,
            Owner: snapshot.Step.ToString(),
            Envelope: snapshot));
    }

    private sealed class ObservationScope(IPipelineObserver? previous) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            CurrentObserver.Value = previous;
            disposed = true;
        }
    }
}
