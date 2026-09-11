using System.Collections.Immutable;
using System.Numerics;
using System.Text;
using Namespace2Xml.Budgets;
using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Inputs;
using Namespace2Xml.Output;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

[TestFixture]
public sealed class ComplexityInstrumentationTests
{
    [Test]
    public void OrdinaryPipelineWorkStaysWithinTheNpeBound()
    {
        var small = MeasureOrdinaryPipeline(256);
        var large = MeasureOrdinaryPipeline(512);

        small.Work.ShouldBeLessThanOrEqualTo(small.Bound, small.Description);
        large.Work.ShouldBeLessThanOrEqualTo(large.Bound, large.Description);
        large.Work.ShouldBeLessThan(
            small.Work * 3,
            $"doubling the constructed input must remain below a quadratic growth ratio; "
            + $"small={small.Description}; large={large.Description}");
    }

    [Test]
    public void EachWildcardRuleItemPairIsCheckedAtMostOnce()
    {
        const string Profile =
            "a.x.seed=1\n"
            + "a.y.seed=2\n"
            + "a.hidden.seed=3\n"
            + "!a.*.seed\n"
            + "a.*.generated=value\n"
            + "a.*.*.leaf=tail\n"
            + "a.z*.miss=no\n";

        var run = EvaluateWildcards(Profile);
        var pairs = run.Ledger.Of(PipelineObservationKind.WildcardCandidate)
            .Select(item => (item.Owner, item.Path))
            .ToArray();

        pairs.Length.ShouldBe(12);
        pairs.Distinct().Count().ShouldBe(pairs.Length);
        run.Budget.Consumed(ResourceBound.MaxWildcardCandidates).ShouldBe(pairs.Length);
        run.Ledger.Of(PipelineObservationKind.MaskCandidate).ShouldNotBeEmpty();
        run.Diagnostics.Drain().ShouldBeEmpty();
    }

    [Test]
    public void WildcardWorkAccountsForMatchesAndGeneratedNodes()
    {
        const string Profile =
            "a.x.seed=1\n"
            + "a.x.generated=existing\n"
            + "a.y.seed=2\n"
            + "a.*.generated=value\n"
            + "a.*.*.leaf=tail\n";

        var run = EvaluateWildcards(Profile);
        var matches = run.Ledger.Of(PipelineObservationKind.WildcardMatch).Length;
        var generated = run.Ledger.Of(PipelineObservationKind.GeneratedNode);
        var postDiscovery = run.Ledger.Of(
            PipelineObservationKind.WildcardPostDiscovery).Length;

        matches.ShouldBe(6);
        generated.Length.ShouldBe(5);
        generated.Select(item => item.Path).ShouldBe(
            [
                "a.y.generated",
                "a.x.generated.leaf",
                "a.x.seed.leaf",
                "a.y.seed.leaf",
                "a.y.generated.leaf",
            ],
            ignoreOrder: true);
        run.Budget.Consumed(ResourceBound.MaxGenerated).ShouldBe(generated.Length);
        postDiscovery.ShouldBeLessThanOrEqualTo(3 * (matches + generated.Length));
        run.Diagnostics.Drain().ShouldBeEmpty();
    }

    [Test]
    public void ReferenceResolutionVisitsOnlyReachableEdges()
    {
        const string Profile =
            "left.a=${left.b}\n"
            + "left.b=${left.d}\n"
            + "left.c=${left.d}\n"
            + "left.d=${left.e}\n"
            + "left.e=end\n"
            + "right.a=${right.b}\n"
            + "right.b=${right.c}\n"
            + "right.c=end\n"
            + "dead.a=${dead.b}\n"
            + "dead.b=${dead.a}\n";

        var contribution = ReadProfile(Profile);
        var diagnostics = new DiagnosticBuffer();
        var ledger = new Ledger();

        using (PipelineInstrumentation.Begin(ledger))
        {
            ReferenceResolver.Resolve(
                contribution.Overlay,
                [
                    ImmutableArray.Create<NamePart>(Ordinary("left")),
                    ImmutableArray.Create<NamePart>(Ordinary("right")),
                ],
                new GlobalBudget(ResourceLimits.Defaults),
                diagnostics);
        }

        var edges = ledger.Of(PipelineObservationKind.ReferenceEdge)
            .Select(item => (Closure: item.Owner, From: item.Path, To: item.Target))
            .ToArray();

        edges.ShouldBe(
            [
                ("left", "left.a", "left.b"),
                ("left", "left.b", "left.d"),
                ("left", "left.d", "left.e"),
                ("left", "left.c", "left.d"),
                ("right", "right.a", "right.b"),
                ("right", "right.b", "right.c"),
            ],
            ignoreOrder: true);
        edges.Distinct().Count().ShouldBe(edges.Length);
        edges.ShouldNotContain(edge =>
            edge.From!.StartsWith("dead.", StringComparison.Ordinal)
            || edge.To!.StartsWith("dead.", StringComparison.Ordinal));
        diagnostics.Drain().ShouldBeEmpty();
    }

    [Test]
    public void RenderingChargesExactlyTheProducedUtf8Bytes()
    {
        var unlimited = Render(long.MaxValue);
        var producedBytes = unlimited.Buffers.Sum(buffer => buffer.Length);

        unlimited.AcceptedBytes.ShouldBe(producedBytes);
        unlimited.ConsumedBytes.ShouldBe(producedBytes);

        var exact = Render(producedBytes);
        exact.AcceptedBytes.ShouldBe(producedBytes);
        exact.ConsumedBytes.ShouldBe(producedBytes);
        exact.Buffers.Sum(buffer => buffer.Length).ShouldBe(producedBytes);

        var shortRun = Render(producedBytes - 1, expectFinalLineFeedRefusal: true);
        shortRun.AcceptedBytes.ShouldBe(producedBytes - 1);
        shortRun.ConsumedBytes.ShouldBe(producedBytes - 1);
        shortRun.RejectedBytes.ShouldBe(1);
        shortRun.SecondLength.ShouldBe(producedBytes - 1 - shortRun.FirstLength);
    }

    private static OrdinaryMeasurement MeasureOrdinaryPipeline(int size)
    {
        var first = new StringBuilder();
        var second = new StringBuilder();

        for (var index = 0; index < size; index++)
        {
            var item = index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
            first.Append("root.item").Append(item).Append(".left=").Append(index).Append('\n');
            second.Append("root.item").Append(item).Append(".right=").Append(index).Append('\n');
        }

        first.Append(
            "root.refs.a=${root.refs.b}\n"
            + "root.refs.b=${root.refs.d}\n"
            + "root.refs.c=${root.refs.d}\n"
            + "root.refs.d=${root.refs.e}\n"
            + "root.refs.e=end\n");

        var sources = new TransformationTests.Sources(
            ("first.txt", first.ToString()),
            ("second.txt", second.ToString()),
            ("mask.txt", "!missing.*\n"),
            ("scheme.txt", "root.output=namespace\n"));
        var parsed = CommandLineParser.Parse(
            [
                "-i", "first.txt",
                "-i", "second.txt",
                "-i", "mask.txt",
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
        result.Diagnostics.ShouldBeEmpty();

        PipelineObservationKind[] counted =
        [
            PipelineObservationKind.ParseNode,
            PipelineObservationKind.MergeNode,
            PipelineObservationKind.FilterNode,
            PipelineObservationKind.SequenceInferenceNode,
            PipelineObservationKind.ScalarInferenceNode,
            PipelineObservationKind.PlanningContribution,
            PipelineObservationKind.PathPart,
            PipelineObservationKind.ReferenceNode,
            PipelineObservationKind.ReferenceEdge,
        ];

        foreach (var kind in counted)
        {
            ledger.Of(kind).ShouldNotBeEmpty($"{kind} must be instrumented");
        }

        var work = counted.Sum(kind => ledger.Of(kind).Sum(item => item.Amount));
        var n = (2L * size) + 6;
        var p = (6L * size) + 27;
        const long e = 4;
        var log = BitOperations.Log2((uint)n) + 1;
        var bound = 16 * (n + p + e) * log;
        var counts = string.Join(
            ", ",
            counted.Select(kind => $"{kind}={ledger.Of(kind).Length}"));

        return new OrdinaryMeasurement(
            work,
            bound,
            $"size={size}, N={n}, P={p}, E={e}, log2ceil={log}, work={work}, "
            + $"bound={bound}, {counts}");
    }

    private static WildcardRun EvaluateWildcards(string profile)
    {
        var contribution = ReadProfile(profile);
        var rules = contribution.Templates
            .Select(template => new WildcardRule(
                template.Name,
                template.Value,
                template.Order,
                template.Comments,
                "profile.txt",
                "profile.txt",
                template.Line))
            .Concat(contribution.Masks
                .Where(mask => QualifiedNameLexer.ContainsWildcard(mask.Pattern))
                .Select(mask => new WildcardRule(
                    mask.Pattern,
                    null,
                    mask.Order,
                    [],
                    "profile.txt",
                    "profile.txt",
                    mask.Line)))
            .OrderBy(rule => rule.Order)
            .ToImmutableArray();
        var diagnostics = new DiagnosticBuffer();
        var budget = new GlobalBudget(
            ResourceLimits.Defaults with
            {
                MaxWildcardIterations = 8,
                MaxWildcardCandidates = 1_000,
                MaxGenerated = 1_000,
            });
        var ledger = new Ledger();
        var mask = ExclusionMask.Of(contribution.Masks.Select(item => item.Pattern));

        using (PipelineInstrumentation.Begin(ledger))
        {
            var evaluator = new WildcardEvaluator(
                WildcardEvaluator.Validate(rules, diagnostics),
                mask,
                new OverlayMerger(MergeStrategyMap.Default, diagnostics),
                budget,
                diagnostics);

            evaluator.ChargeMaskedCandidates([contribution.Overlay]).ShouldBeTrue();
            _ = evaluator.Evaluate(mask.Apply(contribution.Overlay));
        }

        return new WildcardRun(budget, diagnostics, ledger);
    }

    private static ProfileContribution ReadProfile(string text)
    {
        var diagnostics = new DiagnosticBuffer();
        var records = text
            .Split('\n')
            .Select((line, index) => NamespaceRecordClassifier.Classify(line, index + 1))
            .ToImmutableArray();
        var contribution = NamespaceProfileReader.Read(
            records,
            0,
            ProfileSource.OfFile("profile.txt"),
            SubstituteModeMap.Default,
            diagnostics);

        diagnostics.Drain().ShouldBeEmpty();
        return contribution;
    }

    private static RenderRun Render(
        long limit,
        bool expectFinalLineFeedRefusal = false)
    {
        var budget = new GlobalBudget(
            ResourceLimits.Defaults with { MaxTotalOutputBytes = limit });
        var ledger = new Ledger();
        var first = new OutputBufferWriter(budget);
        var second = new OutputBufferWriter(budget);
        OutputBuffer[] buffers = [];

        using (PipelineInstrumentation.Begin(ledger))
        {
            first.TryWriteLine("Aé😀\\").ShouldBeTrue();
            second.TryWrite("雪=").ShouldBeTrue();
            second.TryWrite("終").ShouldBeTrue();

            if (expectFinalLineFeedRefusal)
            {
                second.TryWrite("\n"u8).ShouldBeFalse();
            }
            else
            {
                second.TryWrite("\n"u8).ShouldBeTrue();
                buffers = [first.Build(), second.Build()];
            }
        }

        var observations = ledger.Of(PipelineObservationKind.RenderedBytes);

        return new RenderRun(
            buffers,
            observations.Where(item => item.Accepted).Sum(item => item.Amount),
            observations.Where(item => !item.Accepted).Sum(item => item.Amount),
            budget.Consumed(ResourceBound.MaxTotalOutputBytes),
            first.Length,
            second.Length);
    }

    private static OrdinaryPart Ordinary(string text) => new([new LiteralToken(text)]);

    private sealed class Ledger : IPipelineObserver
    {
        private readonly List<PipelineObservation> observations = [];

        public void Observe(PipelineObservation observation) =>
            observations.Add(observation);

        public PipelineObservation[] Of(PipelineObservationKind kind) =>
            observations.Where(item => item.Kind == kind).ToArray();
    }

    private sealed record OrdinaryMeasurement(long Work, long Bound, string Description);

    private sealed record WildcardRun(
        GlobalBudget Budget,
        DiagnosticBuffer Diagnostics,
        Ledger Ledger);

    private sealed record RenderRun(
        OutputBuffer[] Buffers,
        long AcceptedBytes,
        long RejectedBytes,
        long ConsumedBytes,
        long FirstLength,
        long SecondLength);
}
