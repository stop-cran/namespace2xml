using System.Collections.Immutable;
using Namespace2Xml.Cli;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Scheme;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

[TestFixture]
public sealed class PlanningPhaseEnvelopeTests
{
    [Test]
    public void AddressBasedOperationsNeverObserveEnvelopeMetadata()
    {
        var sources = new TransformationTests.Sources(
            ("input.xml",
                "<!--lead--><r><!--inside--><x><v>1</v></x><y>2</y>"
                + "<ref>${r.x.v}</ref></r><!--trail-->"),
            ("rules.txt", "!r.hidden.*\nr.x.*.copy=z\n"),
            ("scheme.txt",
                "r.output=json\n"
                + "r.root=doc\n"
                + "r.x.*.output=json\n"
                + "r.x.*.root=item\n"
                + "r.x.type=mapping\n"
                + "r.y.type=ignore\n"));
        var parsed = CommandLineParser.Parse(
            [
                "-i", "input.xml",
                "-i", "rules.txt",
                "-s", "scheme.txt",
            ]).CommandLine.ShouldNotBeNull();
        var ledger = new Ledger();
        var result = Transformation.Run(
            parsed,
            sources,
            new TransformationTests.Sink(),
            log: null,
            ledger);

        result.ExitCode.ShouldBe(0);

        PipelineObservationKind[] addressOperations =
        [
            PipelineObservationKind.MaskCandidate,
            PipelineObservationKind.WildcardCandidate,
            PipelineObservationKind.OutputSelector,
            PipelineObservationKind.ReferenceEdge,
            PipelineObservationKind.PathDirective,
        ];

        foreach (var kind in addressOperations)
        {
            var observations = ledger.Of(kind);
            observations.ShouldNotBeEmpty($"{kind} must execute in this gate");

            foreach (var item in observations)
            {
                item.Path.ShouldNotBe("lead");
                item.Path.ShouldNotBe("trail");
                (item.Path ?? string.Empty).ShouldNotContain("envelope", Case.Insensitive);
                (item.Target ?? string.Empty).ShouldNotContain("envelope", Case.Insensitive);
            }
        }

        ledger.Of(PipelineObservationKind.MaskCandidate)
            .ShouldContain(item => item.Path == "r.#0");
        ledger.Of(PipelineObservationKind.WildcardCandidate)
            .ShouldContain(item => item.Owner == "r.x.*.copy");
        ledger.Of(PipelineObservationKind.OutputSelector)
            .ShouldContain(item => item.Path == "r");
        ledger.Of(PipelineObservationKind.OutputSelector)
            .ShouldContain(item => item.Path == "r.x.v");
        ledger.Of(PipelineObservationKind.ReferenceEdge)
            .ShouldContain(item => item.Target == "r.x.v");
        ledger.Of(PipelineObservationKind.PathDirective)
            .Select(item => item.Owner)
            .ShouldContain(owner => owner!.Contains("type=ignore", StringComparison.Ordinal));
        ledger.Of(PipelineObservationKind.PathDirective)
            .ShouldContain(item => item.Owner == "r.x.type");
        ledger.Of(PipelineObservationKind.PathDirective)
            .ShouldContain(item => item.Owner == "root");

        var comments = ImmutableArray.Create(
            new XmlEnvelopeComment(
                "lead",
                XmlEnvelopePlacement.Leading,
                StableOrderingKey.FromSource(1, 1)),
            new XmlEnvelopeComment(
                "trail",
                XmlEnvelopePlacement.Trailing,
                StableOrderingKey.FromSource(1, 16)));
        var empty = ImmutableArray<XmlEnvelopeComment>.Empty;
        var input = new EnvelopeProductIdentity(
            "input-contribution",
            SourceIdentity: "input.xml",
            SourceOrdinal: 1);
        var rules = new EnvelopeProductIdentity(
            "input-contribution",
            SourceIdentity: "rules.txt",
            SourceOrdinal: 2);
        var rootView = new EnvelopeProductIdentity(
            "output-view",
            Selector: "r",
            Format: OutputFormat.Json,
            FormatOrdinal: 0);
        var wildcardView = new EnvelopeProductIdentity(
            "output-view",
            Selector: "r.x.v",
            Format: OutputFormat.Json,
            FormatOrdinal: 0);

        (PipelineStep Step, EnvelopeProductIdentity Product, ImmutableArray<XmlEnvelopeComment> Comments)[]
            expected =
            [
                (PipelineStep.ParseInputs, input, comments),
                (PipelineStep.ParseInputs, rules, empty),
                (PipelineStep.BuildOutputInstances, rootView, empty),
                (PipelineStep.BuildOutputInstances, wildcardView, empty),
                (PipelineStep.ResolveReferences, rootView, comments),
                (PipelineStep.ResolveReferences, wildcardView, comments),
                (PipelineStep.ApplyTransformations, rootView, comments),
                (PipelineStep.ApplyTransformations, wildcardView, comments),
            ];
        var snapshots = ledger.Of(PipelineObservationKind.EnvelopeSnapshot)
            .Select(item => item.Envelope.ShouldNotBeNull())
            .ToArray();

        snapshots.Length.ShouldBe(expected.Length);

        for (var i = 0; i < expected.Length; i++)
        {
            snapshots[i].Step.ShouldBe(expected[i].Step);
            snapshots[i].Product.ShouldBe(expected[i].Product);
            snapshots[i].Comments.ShouldBe(expected[i].Comments);
        }
    }

    private sealed class Ledger : IPipelineObserver
    {
        private readonly List<PipelineObservation> observations = [];

        public void Observe(PipelineObservation observation) =>
            observations.Add(observation);

        public PipelineObservation[] Of(PipelineObservationKind kind) =>
            observations.Where(item => item.Kind == kind).ToArray();
    }
}
