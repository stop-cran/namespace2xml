using Namespace2Xml.Diagnostics;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// The Section 15.1 step enumeration and its Section 6.4.3 phase mapping.
/// </summary>
[TestFixture]
public sealed class PipelineStepTests
{
    [Test]
    public void SectionFifteenPointOneHasTwentySteps() =>
        PipelineSteps.All.Count.ShouldBe(20);

    [Test]
    public void EveryStepNumberIsItsSpecificationStepNumber()
    {
        var numbers = PipelineSteps.All.Select(step => step.Number()).ToArray();

        numbers.ShouldBe(Enumerable.Range(1, 20).ToArray());
    }

    [Test]
    public void StepsAreDeclaredInSpecificationOrder()
    {
        var ordered = PipelineSteps.All.OrderBy(step => step.Number()).ToArray();

        ordered.ShouldBe(PipelineSteps.All.ToArray());
    }

    // Section 6.4.3: scheme is steps 1 through 4, input 5 through 12, planning 13 through 19,
    // publication step 20.
    [TestCase(PipelineStep.ParseSchemes, DiagnosticPhase.Scheme)]
    [TestCase(PipelineStep.CompileInputOptions, DiagnosticPhase.Scheme)]
    [TestCase(PipelineStep.CompileSubstitutePatterns, DiagnosticPhase.Scheme)]
    [TestCase(PipelineStep.CompileInputMerges, DiagnosticPhase.Scheme)]
    [TestCase(PipelineStep.ParseInputs, DiagnosticPhase.Input)]
    [TestCase(PipelineStep.ValidateReferenceSyntax, DiagnosticPhase.Input)]
    [TestCase(PipelineStep.ExtractTemplatesAndMasks, DiagnosticPhase.Input)]
    [TestCase(PipelineStep.MergeContributions, DiagnosticPhase.Input)]
    [TestCase(PipelineStep.ExposeOrderingValues, DiagnosticPhase.Input)]
    [TestCase(PipelineStep.EvaluateTemplates, DiagnosticPhase.Input)]
    [TestCase(PipelineStep.InferSequences, DiagnosticPhase.Input)]
    [TestCase(PipelineStep.InferScalarKinds, DiagnosticPhase.Input)]
    [TestCase(PipelineStep.ExpandWildcards, DiagnosticPhase.Planning)]
    [TestCase(PipelineStep.BuildOutputInstances, DiagnosticPhase.Planning)]
    [TestCase(PipelineStep.ResolveReferences, DiagnosticPhase.Planning)]
    [TestCase(PipelineStep.ApplyTransformations, DiagnosticPhase.Planning)]
    [TestCase(PipelineStep.GroupByDestination, DiagnosticPhase.Planning)]
    [TestCase(PipelineStep.FoldDestinationCollisions, DiagnosticPhase.Planning)]
    [TestCase(PipelineStep.Serialize, DiagnosticPhase.Planning)]
    [TestCase(PipelineStep.Publish, DiagnosticPhase.Publication)]
    public void PhaseFollowsTheSectionSixPointFourPointThreeRanges(
        PipelineStep step,
        DiagnosticPhase expected) =>
        step.Phase().ShouldBe(expected);

    [Test]
    public void NoStepReportsTheCommandLinePhase()
    {
        // Section 6.4.3 places cli before step 1, so no step may claim it.
        PipelineSteps.All.ShouldAllBe(step => step.Phase() != DiagnosticPhase.Cli);
    }

    [Test]
    public void PhasesNeverGoBackwards()
    {
        var phases = PipelineSteps.All.Select(step => (int)step.Phase()).ToArray();

        for (var i = 1; i < phases.Length; i++)
        {
            phases[i].ShouldBeGreaterThanOrEqualTo(phases[i - 1]);
        }
    }

    [Test]
    public void TheFirstStepHasNoPredecessor() =>
        PipelineSteps.First.Previous().ShouldBeNull();

    [Test]
    public void TheLastStepHasNoSuccessor() =>
        PipelineSteps.Last.Next().ShouldBeNull();

    [Test]
    public void PredecessorAndSuccessorAreInverse()
    {
        foreach (var step in PipelineSteps.All.Where(step => step != PipelineSteps.Last))
        {
            step.Next()!.Value.Previous().ShouldBe(step);
        }
    }

    [Test]
    public void APhaseEndsExactlyAtStepsFourTwelveNineteenAndTwenty()
    {
        var boundaries = PipelineSteps.All.Where(step => step.EndsPhase()).Select(step => step.Number());

        boundaries.ShouldBe([4, 12, 19, 20]);
    }

    [Test]
    public void AnUndefinedStepIsNotNumbered() =>
        Should.Throw<ArgumentOutOfRangeException>(() => ((PipelineStep)0).Number());

    [Test]
    public void AnUndefinedStepHasNoPhase() =>
        Should.Throw<ArgumentOutOfRangeException>(() => ((PipelineStep)21).Phase());

    [Test]
    public void DefinednessCoversExactlyTheTwentySteps()
    {
        PipelineSteps.IsDefined((PipelineStep)0).ShouldBeFalse();
        PipelineSteps.IsDefined((PipelineStep)21).ShouldBeFalse();
        PipelineSteps.All.ShouldAllBe(step => PipelineSteps.IsDefined(step));
    }
}

/// <summary>
/// Section 24 emission order and Section 22 cardinality, as enforced by the buffer.
/// </summary>
[TestFixture]
public sealed class DiagnosticBufferTests
{
    [Test]
    public void AnEmptyBufferDrainsToNothing()
    {
        var buffer = new DiagnosticBuffer();

        buffer.Drain().ShouldBeEmpty();
        buffer.Count.ShouldBe(0);
        buffer.HasBlockingError.ShouldBeFalse();
    }

    [Test]
    public void PhaseOrdersBeforeEverythingElse()
    {
        var buffer = new DiagnosticBuffer();
        buffer.Add(Entry("PATH002", DiagnosticPhase.Publication, key: "a"));
        buffer.Add(Entry("PARSE001", DiagnosticPhase.Input, key: "b"));
        buffer.Add(Entry("SCHEME001", DiagnosticPhase.Scheme, key: "c"));
        buffer.Add(Entry("CLI001", DiagnosticPhase.Cli, key: "d"));

        Codes(buffer).ShouldBe(["CLI001", "SCHEME001", "PARSE001", "PATH002"]);
    }

    [Test]
    public void SchemeDiagnosticsPrecedeInputDiagnosticsWhichPrecedePlanningAndPublication()
    {
        var buffer = new DiagnosticBuffer();
        buffer.Add(Entry("SERIALIZE001", DiagnosticPhase.Planning, key: "a"));
        buffer.Add(Entry("PATH002", DiagnosticPhase.Publication, key: "b"));
        buffer.Add(Entry("SCHEME001", DiagnosticPhase.Scheme, key: "c"));
        buffer.Add(Entry("PARSE001", DiagnosticPhase.Input, key: "d"));

        Codes(buffer).ShouldBe(["SCHEME001", "PARSE001", "SERIALIZE001", "PATH002"]);
    }

    [Test]
    public void WithinAPhaseAnOrderingKeyComesFirstThenADestinationThenNeither()
    {
        var buffer = new DiagnosticBuffer();
        buffer.Add(Entry("WARN008", DiagnosticPhase.Planning, key: "neither"));
        buffer.Add(Entry("PATH001", DiagnosticPhase.Planning, key: "dest", destination: 3));
        buffer.Add(Entry("TYPE001", DiagnosticPhase.Planning, key: "item", orderingKey: StableOrderingKey.FromSource(9, 9)));

        Codes(buffer).ShouldBe(["TYPE001", "PATH001", "WARN008"]);
    }

    [Test]
    public void OrderingKeysCompareUnderSectionFourPointSeven()
    {
        var buffer = new DiagnosticBuffer();
        buffer.Add(Entry("PARSE001", DiagnosticPhase.Input, key: "c", orderingKey: StableOrderingKey.FromSource(1, 5)));
        buffer.Add(Entry("PARSE001", DiagnosticPhase.Input, key: "a", orderingKey: StableOrderingKey.FromSource(0, 7)));
        buffer.Add(Entry("PARSE001", DiagnosticPhase.Input, key: "b", orderingKey: StableOrderingKey.FromSource(1, 2)));

        Keys(buffer).ShouldBe(["a", "b", "c"]);
    }

    [Test]
    public void ASourceScopedDiagnosticPrecedesEveryItemOfThatSource()
    {
        // Section 24: a diagnostic concerning a source but no item carries only the CLI source
        // ordinal, so it sorts before every item of that source.
        var buffer = new DiagnosticBuffer();
        buffer.Add(Entry("REFERENCE002", DiagnosticPhase.Input, key: "item", orderingKey: StableOrderingKey.FromSource(2, 1)));
        buffer.Add(Entry("PARSE002", DiagnosticPhase.Input, key: "source", orderingKey: StableOrderingKey.FromSource(2, 0)));

        Keys(buffer).ShouldBe(["source", "item"]);
    }

    [Test]
    public void DestinationOnlyDiagnosticsFollowSectionTwentyOnePointThreeOrder()
    {
        var buffer = new DiagnosticBuffer();
        buffer.Add(Entry("PATH001", DiagnosticPhase.Planning, key: "c", destination: 2));
        buffer.Add(Entry("PATH001", DiagnosticPhase.Planning, key: "a", destination: 0));
        buffer.Add(Entry("PATH001", DiagnosticPhase.Planning, key: "b", destination: 1));

        Keys(buffer).ShouldBe(["a", "b", "c"]);
    }

    [Test]
    public void CodeBreaksATieOnTheOrderingKey()
    {
        var at = StableOrderingKey.FromSource(1, 1);
        var buffer = new DiagnosticBuffer();
        buffer.Add(Entry("XML002", DiagnosticPhase.Input, key: "c", orderingKey: at));
        buffer.Add(Entry("PARSE001", DiagnosticPhase.Input, key: "a", orderingKey: at));
        buffer.Add(Entry("TYPE001", DiagnosticPhase.Input, key: "b", orderingKey: at));

        Keys(buffer).ShouldBe(["a", "b", "c"]);
    }

    [Test]
    public void PathBreaksATieOnTheCode()
    {
        var at = StableOrderingKey.FromSource(1, 1);
        var buffer = new DiagnosticBuffer();
        buffer.Add(Entry("TYPE001", DiagnosticPhase.Input, key: "c", orderingKey: at, path: "b.z"));
        buffer.Add(Entry("TYPE001", DiagnosticPhase.Input, key: "a", orderingKey: at, path: "a.a"));
        buffer.Add(Entry("TYPE001", DiagnosticPhase.Input, key: "b", orderingKey: at, path: "b.a"));

        Keys(buffer).ShouldBe(["a", "b", "c"]);
    }

    [Test]
    public void AnAbsentPathSortsBeforeAnyPresentPath()
    {
        var at = StableOrderingKey.FromSource(1, 1);
        var buffer = new DiagnosticBuffer();
        buffer.Add(Entry("TYPE001", DiagnosticPhase.Input, key: "present", orderingKey: at, path: "a"));
        buffer.Add(Entry("TYPE001", DiagnosticPhase.Input, key: "absent", orderingKey: at));

        Keys(buffer).ShouldBe(["absent", "present"]);
    }

    [Test]
    public void AnAbsentPathComparesBeforeAPresentOneFromEitherSide()
    {
        // Sorting two elements asks the comparer once, so a drained order exercises only one of
        // the two null branches. Both are asserted here or one of them is never executed.
        var at = StableOrderingKey.FromSource(1, 1);
        var absent = Entry("TYPE001", DiagnosticPhase.Input, key: "absent", orderingKey: at);
        var present = Entry("TYPE001", DiagnosticPhase.Input, key: "present", orderingKey: at, path: "a");

        BufferedDiagnosticOrder.Instance.Compare(absent, present).ShouldBeLessThan(0);
        BufferedDiagnosticOrder.Instance.Compare(present, absent).ShouldBeGreaterThan(0);
    }

    [Test]
    public void TwoOccurrencesWithoutAPathTieOnPath()
    {
        // Distinct cardinality keys, everything Section 24 orders by identical, neither carrying a
        // path. The final key must tie rather than assert an order the specification does not give.
        var at = StableOrderingKey.FromSource(1, 1);
        var left = Entry("WARN005", DiagnosticPhase.Planning, key: "pair-a", orderingKey: at, severity: DiagnosticSeverity.Warning);
        var right = Entry("WARN005", DiagnosticPhase.Planning, key: "pair-b", orderingKey: at, severity: DiagnosticSeverity.Warning);

        BufferedDiagnosticOrder.Instance.Compare(left, right).ShouldBe(0);
    }

    [Test]
    public void ANullOccurrenceSortsBeforeAnyOccurrence()
    {
        var entry = Entry("CLI001", DiagnosticPhase.Cli, key: "cli");

        BufferedDiagnosticOrder.Instance.Compare(null, entry).ShouldBeLessThan(0);
        BufferedDiagnosticOrder.Instance.Compare(entry, null).ShouldBeGreaterThan(0);
        BufferedDiagnosticOrder.Instance.Compare(null, null).ShouldBe(0);
    }

    [Test]
    public void TheOrderIsAntisymmetric()
    {
        foreach (var left in Representative)
        {
            foreach (var right in Representative)
            {
                var forward = Math.Sign(BufferedDiagnosticOrder.Instance.Compare(left, right));
                var backward = Math.Sign(BufferedDiagnosticOrder.Instance.Compare(right, left));

                forward.ShouldBe(-backward);
            }
        }
    }

    [Test]
    public void AnOccurrenceComparesEqualToItself()
    {
        foreach (var entry in Representative)
        {
            var same = entry;
            BufferedDiagnosticOrder.Instance.Compare(entry, same).ShouldBe(0);
        }
    }

    [Test]
    public void TheOrderIsTransitive()
    {
        foreach (var a in Representative)
        {
            foreach (var b in Representative)
            {
                foreach (var c in Representative)
                {
                    if (BufferedDiagnosticOrder.Instance.Compare(a, b) <= 0
                        && BufferedDiagnosticOrder.Instance.Compare(b, c) <= 0)
                    {
                        BufferedDiagnosticOrder.Instance.Compare(a, c).ShouldBeLessThanOrEqualTo(0);
                    }
                }
            }
        }
    }

    [Test]
    public void PathsCompareAsUtf8BytesNotAsUtf16CodeUnits()
    {
        // U+10000 encodes as F0 90 80 80 and U+E000 as EE 80 80, so UTF-8 puts U+E000 first.
        // Ordinal UTF-16 comparison puts the surrogate pair D800 DC00 first and would invert this.
        var at = StableOrderingKey.FromSource(1, 1);
        var buffer = new DiagnosticBuffer();
        buffer.Add(Entry("TYPE001", DiagnosticPhase.Input, key: "astral", orderingKey: at, path: "\U00010000"));
        buffer.Add(Entry("TYPE001", DiagnosticPhase.Input, key: "private", orderingKey: at, path: "\uE000"));

        Keys(buffer).ShouldBe(["private", "astral"]);
    }

    [Test]
    public void APrefixSortsBeforeTheStringItPrefixes()
    {
        var at = StableOrderingKey.FromSource(1, 1);
        var buffer = new DiagnosticBuffer();
        buffer.Add(Entry("TYPE001", DiagnosticPhase.Input, key: "long", orderingKey: at, path: "app.db"));
        buffer.Add(Entry("TYPE001", DiagnosticPhase.Input, key: "short", orderingKey: at, path: "app"));

        Keys(buffer).ShouldBe(["short", "long"]);
    }

    [Test]
    public void OneCardinalitySlotHoldsOneOccurrence()
    {
        var buffer = new DiagnosticBuffer();
        buffer.Add(Entry("LIMIT001", DiagnosticPhase.Input, key: DiagnosticCodes.Invocation, orderingKey: StableOrderingKey.FromSource(0, 0)));
        buffer.Add(Entry("LIMIT001", DiagnosticPhase.Input, key: DiagnosticCodes.Invocation, orderingKey: StableOrderingKey.FromSource(4, 0)));

        buffer.Count.ShouldBe(1);
    }

    [Test]
    public void TheSurvivingOccurrenceIsTheEarliestNotTheFirstOffered()
    {
        // Two workers reach the same once-per-invocation slot. Whichever arrives first, the
        // occurrence Section 24 orders earliest is the one reported.
        var early = Entry("LIMIT001", DiagnosticPhase.Input, key: DiagnosticCodes.Invocation, orderingKey: StableOrderingKey.FromSource(0, 3), path: "early");
        var late = Entry("LIMIT001", DiagnosticPhase.Input, key: DiagnosticCodes.Invocation, orderingKey: StableOrderingKey.FromSource(7, 1), path: "late");

        var lateFirst = new DiagnosticBuffer();
        lateFirst.Add(late);
        lateFirst.Add(early);

        var earlyFirst = new DiagnosticBuffer();
        earlyFirst.Add(early);
        earlyFirst.Add(late);

        Paths(lateFirst).ShouldBe(["early"]);
        Paths(earlyFirst).ShouldBe(["early"]);
    }

    [Test]
    public void AddReportsWhetherTheBufferTookTheOccurrence()
    {
        var early = Entry("LIMIT001", DiagnosticPhase.Input, key: DiagnosticCodes.Invocation, orderingKey: StableOrderingKey.FromSource(0, 0));
        var late = Entry("LIMIT001", DiagnosticPhase.Input, key: DiagnosticCodes.Invocation, orderingKey: StableOrderingKey.FromSource(1, 0));
        var buffer = new DiagnosticBuffer();

        buffer.Add(late).ShouldBeTrue();
        buffer.Add(early).ShouldBeTrue();
        buffer.Add(late).ShouldBeFalse();
    }

    [Test]
    public void DistinctCardinalityKeysAreDistinctSlots()
    {
        var buffer = new DiagnosticBuffer();
        buffer.Add(Entry("PARSE001", DiagnosticPhase.Input, key: "a.yaml", orderingKey: StableOrderingKey.FromSource(0, 0)));
        buffer.Add(Entry("PARSE001", DiagnosticPhase.Input, key: "b.yaml", orderingKey: StableOrderingKey.FromSource(1, 0)));

        buffer.Count.ShouldBe(2);
    }

    [Test]
    public void DistinctCodesAreDistinctSlotsEvenUnderOneCardinalityKey()
    {
        var buffer = new DiagnosticBuffer();
        buffer.Add(Entry("WARN006", DiagnosticPhase.Input, key: "a.xml", orderingKey: StableOrderingKey.FromSource(0, 0), severity: DiagnosticSeverity.Warning));
        buffer.Add(Entry("WARN007", DiagnosticPhase.Input, key: "a.xml", orderingKey: StableOrderingKey.FromSource(0, 0), severity: DiagnosticSeverity.Warning));

        buffer.Count.ShouldBe(2);
    }

    [Test]
    public void WarningsAreNotBlocking()
    {
        var buffer = new DiagnosticBuffer();
        buffer.Add(Entry("WARN001", DiagnosticPhase.Cli, key: "a", severity: DiagnosticSeverity.Warning));

        buffer.HasBlockingError.ShouldBeFalse();
    }

    [Test]
    public void OneErrorMakesTheBufferBlocking()
    {
        var buffer = new DiagnosticBuffer();
        buffer.Add(Entry("WARN001", DiagnosticPhase.Cli, key: "a", severity: DiagnosticSeverity.Warning));
        buffer.Add(Entry("PARSE001", DiagnosticPhase.Input, key: "b"));

        buffer.HasBlockingError.ShouldBeTrue();
    }

    [Test]
    public void MergingIsIndependentOfTheOrderTheBuffersAreFolded()
    {
        var first = new DiagnosticBuffer();
        first.Add(Entry("PARSE001", DiagnosticPhase.Input, key: "a.yaml", orderingKey: StableOrderingKey.FromSource(0, 0), path: "a"));
        first.Add(Entry("LIMIT001", DiagnosticPhase.Input, key: DiagnosticCodes.Invocation, orderingKey: StableOrderingKey.FromSource(5, 0), path: "late"));

        var second = new DiagnosticBuffer();
        second.Add(Entry("PARSE001", DiagnosticPhase.Input, key: "b.yaml", orderingKey: StableOrderingKey.FromSource(1, 0), path: "b"));
        second.Add(Entry("LIMIT001", DiagnosticPhase.Input, key: DiagnosticCodes.Invocation, orderingKey: StableOrderingKey.FromSource(2, 0), path: "early"));

        var forward = new DiagnosticBuffer();
        forward.Merge(first);
        forward.Merge(second);

        var backward = new DiagnosticBuffer();
        backward.Merge(second);
        backward.Merge(first);

        Paths(forward).ShouldBe(Paths(backward));
        Paths(forward).ShouldBe(["a", "b", "early"]);
    }

    [Test]
    public void MergingCarriesTheBlockingFlag()
    {
        var source = new DiagnosticBuffer();
        source.Add(Entry("PARSE001", DiagnosticPhase.Input, key: "a"));

        var target = new DiagnosticBuffer();
        target.Merge(source);

        target.HasBlockingError.ShouldBeTrue();
    }

    [Test]
    public void MergingLeavesTheSourceBufferAlone()
    {
        var source = new DiagnosticBuffer();
        source.Add(Entry("PARSE001", DiagnosticPhase.Input, key: "a"));

        var target = new DiagnosticBuffer();
        target.Merge(source);

        source.Count.ShouldBe(1);
    }

    [Test]
    public void TheComparerIsTotalOverAShuffledStream()
    {
        var entries = new[]
        {
            Entry("SCHEME001", DiagnosticPhase.Scheme, key: "1", orderingKey: StableOrderingKey.FromSource(0, 1)),
            Entry("PARSE001", DiagnosticPhase.Input, key: "2", orderingKey: StableOrderingKey.FromSource(0, 0)),
            Entry("TYPE001", DiagnosticPhase.Input, key: "3", orderingKey: StableOrderingKey.FromSource(0, 9)),
            Entry("PATH001", DiagnosticPhase.Planning, key: "4", destination: 1),
            Entry("WARN008", DiagnosticPhase.Planning, key: "5", severity: DiagnosticSeverity.Warning),
            Entry("PATH002", DiagnosticPhase.Publication, key: "6", destination: 0),
        };

        var forward = new DiagnosticBuffer();
        foreach (var entry in entries)
        {
            forward.Add(entry);
        }

        var reversed = new DiagnosticBuffer();
        foreach (var entry in entries.Reverse())
        {
            reversed.Add(entry);
        }

        Keys(forward).ShouldBe(["1", "2", "3", "4", "5", "6"]);
        Keys(reversed).ShouldBe(Keys(forward));
    }

    private static BufferedDiagnostic Entry(
        string code,
        DiagnosticPhase phase,
        string key,
        DiagnosticSeverity severity = DiagnosticSeverity.Error,
        StableOrderingKey? orderingKey = null,
        int? destination = null,
        string? path = null) =>
        new(
            new DiagnosticOccurrence(
                new Diagnostic(code, severity, phase, "\u00A715.4", key, path: path),
                key),
            orderingKey,
            destination);

    /// <summary>One occurrence for each way Section 24 can distinguish two of them.</summary>
    private static IReadOnlyList<BufferedDiagnostic> Representative { get; } =
    [
        Entry("CLI001", DiagnosticPhase.Cli, key: "cli"),
        Entry("SCHEME001", DiagnosticPhase.Scheme, key: "scheme", orderingKey: StableOrderingKey.First),
        Entry("PARSE001", DiagnosticPhase.Input, key: "src", orderingKey: StableOrderingKey.FromSource(1, 0)),
        Entry("PARSE001", DiagnosticPhase.Input, key: "item", orderingKey: StableOrderingKey.FromSource(1, 4)),
        Entry("TYPE001", DiagnosticPhase.Input, key: "code", orderingKey: StableOrderingKey.FromSource(1, 4)),
        Entry("TYPE001", DiagnosticPhase.Input, key: "path", orderingKey: StableOrderingKey.FromSource(1, 4), path: "a.b"),
        Entry("TYPE001", DiagnosticPhase.Input, key: "astral", orderingKey: StableOrderingKey.FromSource(1, 4), path: "\U00010000"),
        Entry("PATH001", DiagnosticPhase.Planning, key: "dest0", destination: 0),
        Entry("PATH001", DiagnosticPhase.Planning, key: "dest1", destination: 1),
        Entry("WARN008", DiagnosticPhase.Planning, key: "none", severity: DiagnosticSeverity.Warning),
        Entry("PATH002", DiagnosticPhase.Publication, key: "pub", destination: 0),
    ];

    private static IEnumerable<string> Codes(DiagnosticBuffer buffer) =>
        buffer.Drain().Select(diagnostic => diagnostic.Code);

    // The cardinality key is carried in the message so a drained stream can be identified.
    private static IEnumerable<string> Keys(DiagnosticBuffer buffer) =>
        buffer.Drain().Select(diagnostic => diagnostic.Message);

    private static IEnumerable<string> Paths(DiagnosticBuffer buffer) =>
        buffer.Drain().Select(diagnostic => diagnostic.Path ?? string.Empty);
}

/// <summary>
/// The Section 15.4 driver loop over the Section 15.1 step order.
/// </summary>
[TestFixture]
public sealed class PipelineRunTests
{
    [Test]
    public void ARunStartsRunningWithNoStepBehindIt()
    {
        var run = new PipelineRun();

        run.State.ShouldBe(PipelineRunState.Running);
        run.LastStep.ShouldBeNull();
        run.AbortedAfter.ShouldBeNull();
    }

    [Test]
    public void TheFirstStepMustBeStepOne()
    {
        var run = new PipelineRun();

        Should.Throw<InvalidOperationException>(() =>
            run.Run(PipelineStep.ParseInputs, PipelineRun.Seed(1), (value, _) => StepOutcome.Produced(value)));
    }

    [Test]
    public void StepsRunInSpecificationOrder()
    {
        var run = new PipelineRun();
        var first = run.Run(PipelineStep.ParseSchemes, PipelineRun.Seed(1), (value, _) => StepOutcome.Produced(value));

        Should.Throw<InvalidOperationException>(() =>
            run.Run(PipelineStep.CompileSubstitutePatterns, first, (value, _) => StepOutcome.Produced(value)));
    }

    [Test]
    public void AStepCannotRunTwice()
    {
        var run = new PipelineRun();
        var first = run.Run(PipelineStep.ParseSchemes, PipelineRun.Seed(1), (value, _) => StepOutcome.Produced(value));

        Should.Throw<InvalidOperationException>(() =>
            run.Run(PipelineStep.ParseSchemes, first, (value, _) => StepOutcome.Produced(value)));
    }

    [Test]
    public void AProductOfALaterStepIsACycle()
    {
        var run = new PipelineRun();
        var forged = new StepProduct<int>(PipelineStep.Publish, 1);

        Should.Throw<InvalidOperationException>(() =>
            run.Run(PipelineStep.ParseSchemes, forged, (value, _) => StepOutcome.Produced(value)));
    }

    [Test]
    public void AProductOfTheSameStepIsACycle()
    {
        var run = new PipelineRun();
        var forged = new StepProduct<int>(PipelineStep.ParseSchemes, 1);

        Should.Throw<InvalidOperationException>(() =>
            run.Run(PipelineStep.ParseSchemes, forged, (value, _) => StepOutcome.Produced(value)));
    }

    [Test]
    public void AStepMayConsumeAProductOfAnyEarlierStepNotOnlyItsPredecessor()
    {
        var run = new PipelineRun();
        var first = run.Run(PipelineStep.ParseSchemes, PipelineRun.Seed(1), (value, _) => StepOutcome.Produced(value));
        run.Run(PipelineStep.CompileInputOptions, first, (value, _) => StepOutcome.Produced(value));

        var third = run.Run(PipelineStep.CompileSubstitutePatterns, first, (value, _) => StepOutcome.Produced(value + 1));

        third.ShouldNotBeNull().Value.ShouldBe(2);
    }

    [Test]
    public void TheSeedProductPrecedesEveryStep() =>
        PipelineRun.Seed(1).Step.ShouldBeNull();

    [Test]
    public void AProductCarriesTheStepThatProducedIt()
    {
        var run = new PipelineRun();
        var first = run.Run(PipelineStep.ParseSchemes, PipelineRun.Seed(1), (value, _) => StepOutcome.Produced(value));
        var second = run.Run(PipelineStep.CompileInputOptions, first, (value, _) => StepOutcome.Produced(value));

        first.ShouldNotBeNull().Step.ShouldBe(PipelineStep.ParseSchemes);
        second.ShouldNotBeNull().Step.ShouldBe(PipelineStep.CompileInputOptions);
    }

    [Test]
    public void TheLastStepIsRecordedWhetherItRanOrWasSkipped()
    {
        var run = new PipelineRun();
        var failed = run.Run(PipelineStep.ParseSchemes, PipelineRun.Seed(1), (_, diagnostics) =>
        {
            diagnostics.Add(Error("PARSE001", DiagnosticPhase.Scheme, "a"));
            return StepOutcome.Failed<int>();
        });

        run.LastStep.ShouldBe(PipelineStep.ParseSchemes);
        run.Run(PipelineStep.CompileInputOptions, failed, (value, _) => StepOutcome.Produced(value));
        run.LastStep.ShouldBe(PipelineStep.CompileInputOptions);
    }

    [Test]
    public void AllTwentyStepsRunToCompletion()
    {
        var run = new PipelineRun();
        var ran = new List<PipelineStep>();
        StepProduct<int>? product = PipelineRun.Seed(0);

        foreach (var step in PipelineSteps.All)
        {
            var current = step;
            product = run.Run(step, product, (value, _) =>
            {
                ran.Add(current);
                return StepOutcome.Produced(value + 1);
            });
        }

        ran.ShouldBe(PipelineSteps.All.ToArray());
        product.ShouldNotBeNull().Value.ShouldBe(20);
        run.State.ShouldBe(PipelineRunState.Finished);
    }

    [Test]
    public void ATwentyFirstStepIsRefused()
    {
        var run = new PipelineRun();
        StepProduct<int>? product = PipelineRun.Seed(0);
        foreach (var step in PipelineSteps.All)
        {
            product = run.Run(step, product, (value, _) => StepOutcome.Produced(value));
        }

        Should.Throw<InvalidOperationException>(() =>
            run.Run((PipelineStep)21, product, (value, _) => StepOutcome.Produced(value)));
    }

    [Test]
    public void AFailedStepProducesNothing()
    {
        var run = new PipelineRun();

        var product = run.Run(PipelineStep.ParseSchemes, PipelineRun.Seed(1), (_, diagnostics) =>
        {
            diagnostics.Add(Error("PARSE001", DiagnosticPhase.Scheme, "a"));
            return StepOutcome.Failed<int>();
        });

        product.ShouldBeNull();
    }

    [Test]
    public void AStepHandedNothingDoesNotRun()
    {
        var run = new PipelineRun();
        var failed = run.Run(PipelineStep.ParseSchemes, PipelineRun.Seed(1), (_, diagnostics) =>
        {
            diagnostics.Add(Error("PARSE001", DiagnosticPhase.Scheme, "a"));
            return StepOutcome.Failed<int>();
        });

        var ran = false;
        var product = run.Run(PipelineStep.CompileInputOptions, failed, (value, _) =>
        {
            ran = true;
            return StepOutcome.Produced(value);
        });

        ran.ShouldBeFalse();
        product.ShouldBeNull();
    }

    [Test]
    public void AnIndependentStepStillRunsAfterASiblingFails()
    {
        // Section 15.4: a phase completes every independent check that does not depend on a failed
        // result, so its diagnostics are collected before the phase boundary stops the run.
        var run = new PipelineRun();
        var first = run.Run(PipelineStep.ParseSchemes, PipelineRun.Seed(1), (value, _) => StepOutcome.Produced(value));

        var failed = run.Run(PipelineStep.CompileInputOptions, first, (_, diagnostics) =>
        {
            diagnostics.Add(Error("SCHEME001", DiagnosticPhase.Scheme, "options"));
            return StepOutcome.Failed<int>();
        });

        var ran = false;
        run.Run(PipelineStep.CompileSubstitutePatterns, first, (value, diagnostics) =>
        {
            ran = true;
            diagnostics.Add(Error("SCHEME001", DiagnosticPhase.Scheme, "substitute"));
            return StepOutcome.Produced(value);
        });

        failed.ShouldBeNull();
        ran.ShouldBeTrue();
        run.Diagnostics.Count.ShouldBe(2);
    }

    [Test]
    public void ABlockingDiagnosticDoesNotStopTheRestOfItsOwnPhase()
    {
        var run = new PipelineRun();
        StepProduct<int>? product = PipelineRun.Seed(0);
        var ran = new List<PipelineStep>();

        foreach (var step in PipelineSteps.All.Where(step => step.Phase() == DiagnosticPhase.Scheme))
        {
            var current = step;
            product = run.Run(step, product, (value, diagnostics) =>
            {
                ran.Add(current);
                if (current == PipelineStep.ParseSchemes)
                {
                    diagnostics.Add(Error("SCHEME001", DiagnosticPhase.Scheme, "first"));
                }

                return StepOutcome.Produced(value + 1);
            });
        }

        ran.Count.ShouldBe(4);
        run.State.ShouldBe(PipelineRunState.Aborted);
    }

    [Test]
    public void APhaseBoundaryAbortsWhenABlockingDiagnosticExists()
    {
        var run = RunThroughSchemePhase(withError: true);

        run.State.ShouldBe(PipelineRunState.Aborted);
        run.AbortedAfter.ShouldBe(PipelineStep.CompileInputMerges);
    }

    [Test]
    public void APhaseBoundaryDoesNotAbortOnWarningsAlone()
    {
        var run = RunThroughSchemePhase(withError: false);

        run.State.ShouldBe(PipelineRunState.Running);
        run.AbortedAfter.ShouldBeNull();
    }

    [Test]
    public void NoStepOfALaterPhaseRunsAfterAnAbort()
    {
        var run = RunThroughSchemePhase(withError: true);
        var ran = false;

        var product = run.Run(PipelineStep.ParseInputs, PipelineRun.Seed(1), (value, _) =>
        {
            ran = true;
            return StepOutcome.Produced(value);
        });

        ran.ShouldBeFalse();
        product.ShouldBeNull();
    }

    [Test]
    public void AnAbortedRunStaysAbortedThroughStepTwenty()
    {
        var run = RunThroughSchemePhase(withError: true);

        foreach (var step in PipelineSteps.All.Where(step => step.Number() > 4))
        {
            run.Run(step, PipelineRun.Seed(1), (value, _) => StepOutcome.Produced(value));
        }

        run.State.ShouldBe(PipelineRunState.Aborted);
        run.AbortedAfter.ShouldBe(PipelineStep.CompileInputMerges);
    }

    [Test]
    public void AnAbortedRunStillRefusesAnOutOfOrderStep()
    {
        var run = RunThroughSchemePhase(withError: true);

        Should.Throw<InvalidOperationException>(() =>
            run.Run(PipelineStep.Publish, PipelineRun.Seed(1), (value, _) => StepOutcome.Produced(value)));
    }

    [Test]
    public void AStepCannotFailWithoutABlockingDiagnostic()
    {
        // Section 15.4 aborts on blocking diagnostics. A step that fails silently would let the run
        // walk into the next phase with nothing to work on.
        var run = new PipelineRun();

        Should.Throw<InvalidOperationException>(() =>
            run.Run(PipelineStep.ParseSchemes, PipelineRun.Seed(1), (_, _) => StepOutcome.Failed<int>()));
    }

    [Test]
    public void AWarningIsNotEnoughToJustifyAFailedStep()
    {
        var run = new PipelineRun();

        Should.Throw<InvalidOperationException>(() =>
            run.Run(PipelineStep.ParseSchemes, PipelineRun.Seed(1), (_, diagnostics) =>
            {
                diagnostics.Add(Warning("WARN001", DiagnosticPhase.Scheme, "missing"));
                return StepOutcome.Failed<int>();
            }));
    }

    [Test]
    public void AStepMayEmitDiagnosticsAndStillSucceed()
    {
        var run = new PipelineRun();

        var product = run.Run(PipelineStep.ParseSchemes, PipelineRun.Seed(1), (value, diagnostics) =>
        {
            diagnostics.Add(Warning("WARN002", DiagnosticPhase.Scheme, "alias"));
            return StepOutcome.Produced(value);
        });

        product.ShouldNotBeNull().Value.ShouldBe(1);
        run.Diagnostics.Count.ShouldBe(1);
        run.State.ShouldBe(PipelineRunState.Running);
    }

    [Test]
    public void ASkippedStepContributesNoDiagnostics()
    {
        var run = new PipelineRun();
        var failed = run.Run(PipelineStep.ParseSchemes, PipelineRun.Seed(1), (_, diagnostics) =>
        {
            diagnostics.Add(Error("PARSE001", DiagnosticPhase.Scheme, "a"));
            return StepOutcome.Failed<int>();
        });

        run.Run(PipelineStep.CompileInputOptions, failed, (value, diagnostics) =>
        {
            diagnostics.Add(Error("SCHEME001", DiagnosticPhase.Scheme, "never"));
            return StepOutcome.Produced(value);
        });

        run.Diagnostics.Count.ShouldBe(1);
    }

    [Test]
    public void DiagnosticsAccumulateAcrossStepsInSectionTwentyFourOrder()
    {
        var run = new PipelineRun();
        StepProduct<int>? product = PipelineRun.Seed(0);

        foreach (var step in PipelineSteps.All.Where(step => step.Number() <= 4))
        {
            var current = step;
            product = run.Run(step, product, (value, diagnostics) =>
            {
                diagnostics.Add(Warning("WARN00" + current.Number(), DiagnosticPhase.Scheme, current.ToString()));
                return StepOutcome.Produced(value);
            });
        }

        run.Diagnostics.Drain().Select(diagnostic => diagnostic.Code)
            .ShouldBe(["WARN001", "WARN002", "WARN003", "WARN004"]);
    }

    [Test]
    public void PublicationIsNotAPhaseGateBecauseItIsTheLastStep()
    {
        var run = new PipelineRun();
        StepProduct<int>? product = PipelineRun.Seed(0);

        foreach (var step in PipelineSteps.All)
        {
            var current = step;
            product = run.Run(step, product, (value, diagnostics) =>
            {
                if (current == PipelineStep.Publish)
                {
                    diagnostics.Add(Error("PATH002", DiagnosticPhase.Publication, "out.xml"));
                }

                return StepOutcome.Produced(value);
            });
        }

        run.State.ShouldBe(PipelineRunState.Finished);
        run.Diagnostics.HasBlockingError.ShouldBeTrue();
    }

    [Test]
    public void ANullStepBodyIsRefused()
    {
        var run = new PipelineRun();

        Should.Throw<ArgumentNullException>(() =>
            run.Run<int, int>(PipelineStep.ParseSchemes, PipelineRun.Seed(1), null!));
    }

    private static PipelineRun RunThroughSchemePhase(bool withError)
    {
        var run = new PipelineRun();
        StepProduct<int>? product = PipelineRun.Seed(0);

        foreach (var step in PipelineSteps.All.Where(step => step.Phase() == DiagnosticPhase.Scheme))
        {
            var current = step;
            product = run.Run(step, product, (value, diagnostics) =>
            {
                if (current == PipelineStep.CompileInputOptions)
                {
                    diagnostics.Add(withError
                        ? Error("SCHEME001", DiagnosticPhase.Scheme, "options")
                        : Warning("WARN002", DiagnosticPhase.Scheme, "options"));
                }

                return StepOutcome.Produced(value + 1);
            });
        }

        return run;
    }

    private static BufferedDiagnostic Error(string code, DiagnosticPhase phase, string key) =>
        Entry(code, phase, DiagnosticSeverity.Error, key);

    private static BufferedDiagnostic Warning(string code, DiagnosticPhase phase, string key) =>
        Entry(code, phase, DiagnosticSeverity.Warning, key);

    private static BufferedDiagnostic Entry(
        string code,
        DiagnosticPhase phase,
        DiagnosticSeverity severity,
        string key) =>
        new(
            new DiagnosticOccurrence(new Diagnostic(code, severity, phase, "\u00A715.4", key), key),
            StableOrderingKey.First);
}
