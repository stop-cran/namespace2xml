using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Inputs;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Pipeline step 8: Section 16.10 strategies and the Section 17 merge rules they select.
/// </summary>
/// <remarks>
/// Every expectation here is authored from the specification clause named in the test, never from
/// what the merger currently produces.
/// </remarks>
[TestFixture]
public class OverlayMergerTests
{
    private DiagnosticBuffer diagnostics = null!;

    [SetUp]
    public void SetUp() => diagnostics = new DiagnosticBuffer();

    private static NamePart Ordinary(string text) => new OrdinaryPart([new LiteralToken(text)]);

    private static QualifiedName Path(params string[] steps) =>
        new([.. steps.Select(Ordinary)]);

    private static OverlayNode Source(string document, int ordinal) =>
        NamespaceProfileReader.Read(
            [
                .. document
                    .Split('\n')
                    .Select((line, index) => NamespaceRecordClassifier.Classify(line, index + 1)),
            ],
            ordinal,
            ProfileSource.OfFile($"p{ordinal}.txt"),
            new DiagnosticBuffer())
        .Overlay;

    private static OverlayNode Descend(OverlayNode node, params string[] path) =>
        path.Aggregate(node, (current, step) => current.Children[Ordinary(step)]);

    private static MergeStrategyMap Strategy(MergeStrategy strategy, params string[] path) =>
        MergeStrategyMap.Create([new(Path(path), strategy)]);

    private OverlayNode Merge(MergeStrategyMap strategies, params string[] documents) =>
        new OverlayMerger(strategies, diagnostics)
            .MergeAll(documents.Select((document, index) => Source(document, index + 1)));

    private OverlayNode Merge(params string[] documents) =>
        Merge(MergeStrategyMap.Default, documents);

    private static string Value(OverlayNode node) =>
        node.Payload.ShouldNotBeNull().ToCanonicalText();

    private static string[] ChildNames(OverlayNode node) =>
        [.. node.OrderedChildren.Select(child => ((OrdinaryPart)child.Key).LiteralText!)];

    // Section 17.1: "mapping plus mapping: recursively merge matching keys".

    [Test]
    public void MergingTwoMappingsKeepsEveryKeyFromBoth()
    {
        var merged = Merge("a.b=1", "a.c=2");

        Value(Descend(merged, "a", "b")).ShouldBe("1");
        Value(Descend(merged, "a", "c")).ShouldBe("2");
    }

    /// <summary>Section 17.1: "scalar or null payload plus scalar or null payload: later payload wins."</summary>
    [Test]
    public void ALaterSourceOverridesAnEarlierPayload() =>
        Value(Descend(Merge("a.b=1", "a.b=2"), "a", "b")).ShouldBe("2");

    /// <summary>
    /// Section 17.1 judges the payload contest on the Section 4.4 payload mark, so a later source
    /// that contributes no payload at all must not erase the payload an earlier source gave.
    /// </summary>
    [Test]
    public void ALaterSourceWithNoPayloadLeavesTheEarlierPayloadAlone() =>
        Value(Descend(Merge("a=3", "a.b=x"), "a")).ShouldBe("3");

    /// <summary>
    /// Section 17.1: "scalar/null payload plus mapping or sequence contribution: retain both in the
    /// overlay with independent source marks."
    /// </summary>
    [Test]
    public void AScalarAndAContainerBothSurviveTheMerge()
    {
        var node = Descend(Merge("a=1", "a.b=2"), "a");

        Value(node).ShouldBe("1");
        Value(Descend(node, "b")).ShouldBe("2");
    }

    /// <summary>
    /// Section 4.4 steps 1 to 3: the later of the two contributions decides the exclusive shape.
    /// Marks must be combined facet by facet for this to hold across a merge.
    /// </summary>
    [Test]
    public void TheLaterOfTheTwoRetainedFacetsWinsTheExclusiveShapeContest()
    {
        Descend(Merge("a=1", "a.b=2"), "a").Marks.RendersAsMapping.ShouldBeTrue();
        Descend(Merge("a.b=2", "a=1"), "a").Marks.RendersAsScalar.ShouldBeTrue();
    }

    // Section 5.2 mapping order.

    /// <summary>
    /// Section 5.2: "Adding a new child therefore never moves its parent." A second source that
    /// only adds a child to <c>a</c> must leave <c>a</c> ahead of the sibling declared after it.
    /// </summary>
    [Test]
    public void ASecondSourceThatOnlyAddsAChildDoesNotMoveTheParent() =>
        ChildNames(Merge("a.b=1\nz=9", "a.c=2")).ShouldBe(["a", "z"]);

    /// <summary>
    /// Section 5.2: "Overriding a mapping key moves that exact key ... to the winning
    /// contribution's position mark."
    /// </summary>
    [Test]
    public void OverridingAKeyMovesItToTheLaterPosition() =>
        ChildNames(Merge("a=1\nz=9", "a=2")).ShouldBe(["z", "a"]);

    /// <summary>
    /// Section 5.2 gives an intermediate node "the position mark of the earliest contribution that
    /// required it", but a node that is intermediate in one source and directly addressed in a
    /// later one "does move to the later direct contribution".
    /// </summary>
    [Test]
    public void ADirectContributionMovesANodeThatWasOnlyIntermediateBefore() =>
        ChildNames(Merge("a.b=1\nz=9", "a=2")).ShouldBe(["z", "a"]);

    // Section 17.1 comments.

    /// <summary>
    /// Section 17.1: comments "accumulate and survive merge whenever their logical path survives".
    /// </summary>
    [Test]
    public void CommentsAccumulateAcrossSources() =>
        Descend(Merge("#one\na=1", "#two\na=2"), "a")
            .OrderedComments.Select(comment => comment.Text)
            .ShouldBe(["one", "two"]);

    // Section 5.4 high-water accounting.

    /// <summary>
    /// Section 5.4: "any mapping child whose name is canonically spelled as a decimal value within
    /// the supported range reserves that ordering value at its own source position during concrete
    /// merging, whether or not its containing mapping ultimately qualifies for sequence inference."
    /// </summary>
    [Test]
    public void ANumericMappingChildReservesItsOrderingValue() =>
        Descend(Merge("a.5=x"), "a").SequenceHighWater.ShouldBe(5);

    /// <summary>
    /// The same clause's "whether or not" makes the reservation unconditional, so a sibling that
    /// disqualifies the mapping from sequence inference does not release it.
    /// </summary>
    [Test]
    public void ANonNumericSiblingDoesNotReleaseTheReservation() =>
        Descend(Merge("a.5=x\na.b=y"), "a").SequenceHighWater.ShouldBe(5);

    /// <summary>Section 8.7: a leading-zero spelling "is an ordinary mapping key".</summary>
    [Test]
    public void ALeadingZeroChildReservesNothing() =>
        Descend(Merge("a.05=x"), "a").SequenceHighWater
            .ShouldBe(SequenceOrderingAllocator.InitialHighWaterMark);

    /// <summary>Section 5.4: the mark "records the greatest ordering value ever allocated".</summary>
    [Test]
    public void ALaterNumericChildRaisesTheMark() =>
        Descend(Merge("a.2=x", "a.9=y"), "a").SequenceHighWater.ShouldBe(9);

    /// <summary>The same clause: "ever" means the mark never falls.</summary>
    [Test]
    public void ALowerLaterNumericChildDoesNotLowerTheMark() =>
        Descend(Merge("a.9=x", "a.2=y"), "a").SequenceHighWater.ShouldBe(9);

    /// <summary>A merge that adds nothing numeric must not lose the earlier reservation.</summary>
    [Test]
    public void AReservationSurvivesASourceWithNoNumericChildren() =>
        Descend(Merge("a.5=x", "a.b=y"), "a").SequenceHighWater.ShouldBe(5);

    /// <summary>
    /// Section 17.2: "Explicit canonical numeric mapping keys are run-global ordering values at
    /// their sequence path under <c>deep</c> and patch matching values. They are not rebased."
    /// </summary>
    [Test]
    public void DeepMergePatchesAMatchingNumericKeyInPlace()
    {
        var node = Descend(Merge("a.0=x\na.1=y", "a.1=z"), "a");

        ChildNames(node).ShouldBe(["0", "1"]);
        Value(Descend(node, "1")).ShouldBe("z");
    }

    // Section 16.10 replace.

    /// <summary>Section 16.10 <c>replace</c>: "the later complete value replaces the earlier value".</summary>
    [Test]
    public void ReplaceDropsTheEarlierChildren() =>
        ChildNames(Descend(Merge(Strategy(MergeStrategy.Replace, "a"), "a.b=1", "a.c=2"), "a"))
            .ShouldBe(["c"]);

    /// <summary>
    /// Section 17.2: <c>replace</c> "removes the earlier visible sequence projection but does not
    /// lower the path's allocation high-water mark".
    /// </summary>
    [Test]
    public void ReplaceDoesNotLowerTheHighWaterMark()
    {
        var node = Descend(Merge(Strategy(MergeStrategy.Replace, "a"), "a.5=x", "a.b=y"), "a");

        ChildNames(node).ShouldBe(["b"]);
        node.SequenceHighWater.ShouldBe(5);
    }

    /// <summary>
    /// Section 17.1 keeps comments "whenever their logical path survives". A replacement at
    /// <c>a</c> leaves <c>a</c> itself in place, so comments bound to <c>a</c> survive it.
    /// </summary>
    [Test]
    public void ReplaceKeepsCommentsBoundToTheSurvivingPath() =>
        Descend(Merge(Strategy(MergeStrategy.Replace, "a"), "#one\na=1", "a.b=2"), "a")
            .OrderedComments.Select(comment => comment.Text)
            .ShouldBe(["one"]);

    /// <summary>
    /// Section 17.1 omits comments when the logical path is absent "through ... replacement of an
    /// ancestor that removes the path".
    /// </summary>
    [Test]
    public void ReplaceDropsCommentsOnThePathsItRemoves()
    {
        var node = Descend(Merge(Strategy(MergeStrategy.Replace, "a"), "#one\na.b=1", "a.c=2"), "a");

        ChildNames(node).ShouldBe(["c"]);
        node.OrderedComments.ShouldBeEmpty();
    }

    /// <summary>
    /// Section 16.10: "A <c>merge</c> directive governs only the node it matches; descendants use
    /// their independently effective strategy, defaulting to <c>deep</c>."
    /// </summary>
    [Test]
    public void ReplaceGovernsOnlyTheNodeItMatches()
    {
        var merged = Merge(Strategy(MergeStrategy.Replace, "a", "b"), "a.b.x=1\na.q=7", "a.b.y=2");

        ChildNames(Descend(merged, "a", "b")).ShouldBe(["y"]);
        Value(Descend(merged, "a", "q")).ShouldBe("7");
    }

    // Section 16.10 error.

    /// <summary>
    /// Section 16.10 <c>error</c>: "any distinct second source or generated contribution at the
    /// path is an error."
    /// </summary>
    [Test]
    public void ErrorRejectsASecondSourceContributionAtThePath()
    {
        Merge(Strategy(MergeStrategy.Error, "a"), "a.b=1", "a.c=2");

        var diagnostic = diagnostics.Drain().ShouldHaveSingleItem();

        diagnostic.Code.ShouldBe("TYPE001");
        diagnostic.Path.ShouldBe("a");
    }

    /// <summary>
    /// Section 16.10 counts a contribution as being at <c>P</c> when it contributes "any descendant
    /// under <c>P</c>", however deep.
    /// </summary>
    [Test]
    public void ADistantDescendantIsAContributionAtThePath()
    {
        Merge(Strategy(MergeStrategy.Error, "a"), "a=1", "a.b.c.d=2");

        diagnostics.Drain().ShouldHaveSingleItem().Code.ShouldBe("TYPE001");
    }

    /// <summary>
    /// Section 16.10 <c>error</c> applies "after entries inside each source contribution have been
    /// folded", so many entries in one source are one contribution.
    /// </summary>
    [Test]
    public void ErrorAcceptsManyEntriesFromASingleSource()
    {
        Merge(Strategy(MergeStrategy.Error, "a"), "a.b=1\na.c=2\na.d=3");

        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 16.10: descendants of an <c>error</c> node "use their independently effective
    /// strategy, defaulting to <c>deep</c>", so the conflict is reported once and only at the node
    /// the directive names.
    /// </summary>
    [Test]
    public void ErrorGovernsOnlyTheNodeItMatches()
    {
        var merged = Merge(Strategy(MergeStrategy.Error, "a"), "a.b=1", "a.b=2");

        diagnostics.Drain().ShouldHaveSingleItem().Path.ShouldBe("a");
        Value(Descend(merged, "a", "b")).ShouldBe("2");
    }

    // Section 16.10 append.

    /// <summary>
    /// Section 16.10 <c>append</c>: "every item in the later sequence contribution, including
    /// explicitly indexed items, is rebased in ascending original ordering value onto fresh implicit
    /// ordering values above the current high-water mark".
    /// </summary>
    [Test]
    public void AppendRebasesLaterItemsAboveTheHighWaterMark()
    {
        var node = Descend(
            Merge(Strategy(MergeStrategy.Append, "a"), "a.0=x\na.1=y", "a.0=p\na.1=q"), "a");

        node.OrderedSequence.Select(item => item.Key).ShouldBe([2L, 3L]);
        node.OrderedSequence.Select(item => Value(item.Value.Node)).ShouldBe(["p", "q"]);
        node.SequenceHighWater.ShouldBe(3);
    }

    /// <summary>
    /// Section 5.4: "process items in ascending original ordering value. For each item, first raise
    /// the current high-water mark to at least its supplied value, then allocate its new value as
    /// <c>high-water + 1</c>." Processing <c>3</c> before <c>1</c> would give both other values.
    /// </summary>
    [Test]
    public void AppendProcessesItemsInAscendingOriginalOrderingValue()
    {
        var node = Descend(
            Merge(Strategy(MergeStrategy.Append, "a"), "a.0=x", "a.3=p\na.1=q"), "a");

        node.OrderedSequence.Select(item => item.Key).ShouldBe([2L, 4L]);
        node.OrderedSequence.Select(item => Value(item.Value.Node)).ShouldBe(["q", "p"]);
    }

    /// <summary>
    /// Section 16.10 rebases items "onto fresh implicit ordering values", and Section 5.4 adds that
    /// "the original value is no longer addressable for that rebased item". An item that kept its
    /// explicit provenance would advertise a supplied value it no longer has, and Section 17.1
    /// patches on explicit provenance rather than concatenating.
    /// </summary>
    [Test]
    public void ARebasedItemIsImplicitAtItsNewOrderingValue()
    {
        var node = Descend(
            Merge(Strategy(MergeStrategy.Append, "a"), "a.0=x\na.1=y", "a.0=p"), "a");

        node.Sequence[2].Provenance.ShouldBe(OrderingProvenance.Implicit);
    }

    /// <summary>
    /// Section 15.1 step 8: "the earliest or sole contribution retains its supplied ordering
    /// values", and Section 5.4: "A first or sole source contribution is not rebased merely because
    /// <c>merge=append</c> is configured."
    /// </summary>
    [Test]
    public void ASoleContributionIsNotRebased()
    {
        var node = Descend(Merge(Strategy(MergeStrategy.Append, "a"), "a.0=x\na.1=y"), "a");

        ChildNames(node).ShouldBe(["0", "1"]);
        node.Sequence.ShouldBeEmpty();
    }

    /// <summary>
    /// Section 15.1 step 8 rebases only "when a strictly earlier surviving sequence-eligible
    /// contribution exists". An earlier ordinary mapping is not one, so the later contribution is
    /// the earliest sequence-eligible one and keeps its supplied values.
    /// </summary>
    [Test]
    public void AppendDoesNotRebaseWhenNoEarlierContributionIsSequenceEligible()
    {
        var node = Descend(
            Merge(Strategy(MergeStrategy.Append, "a"), "a.b=1", "a.0=x\na.1=y"), "a");

        ChildNames(node).ShouldBe(["b", "0", "1"]);
        node.Sequence.ShouldBeEmpty();
    }

    /// <summary>
    /// Section 16.10: "a source contribution that is a nonempty all-canonical-numeric mapping is
    /// sequence-eligible for this purpose; other non-sequence use is an error."
    /// </summary>
    [Test]
    public void AppendRejectsANonSequenceContribution()
    {
        Merge(Strategy(MergeStrategy.Append, "a"), "a.0=x", "a.b=y");

        diagnostics.Drain().ShouldHaveSingleItem().Code.ShouldBe("TYPE001");
    }

    /// <summary>
    /// Section 16.10 calls only a "nonempty" numeric mapping sequence-eligible, so a contribution
    /// with nothing to append is non-sequence use rather than an empty append.
    /// </summary>
    [Test]
    public void AppendRejectsAScalarContribution()
    {
        Merge(Strategy(MergeStrategy.Append, "a"), "a.0=x", "a=1");

        diagnostics.Drain().ShouldHaveSingleItem().Code.ShouldBe("TYPE001");
    }

    /// <summary>
    /// Section 15.1 step 8: <c>append</c> "leaves no mapping projection for later inference", so the
    /// consumed mapping must contribute sequence shape and not mapping shape. Section 4.4 decides
    /// the exclusive shape from those marks.
    /// </summary>
    [Test]
    public void AnAppendedMappingContributesSequenceShapeNotMappingShape() =>
        Descend(Merge(Strategy(MergeStrategy.Append, "a"), "a.0=x", "a.0=p"), "a")
            .Marks.RendersAsSequence.ShouldBeTrue();

    /// <summary>
    /// Section 8.7 makes a leading-zero key an ordinary mapping key, so a mapping containing one is
    /// not "all-canonical-numeric" and is not sequence-eligible.
    /// </summary>
    [Test]
    public void ALeadingZeroKeyDisqualifiesAMappingFromAppend()
    {
        Merge(Strategy(MergeStrategy.Append, "a"), "a.0=x", "a.01=y");

        diagnostics.Drain().ShouldHaveSingleItem().Code.ShouldBe("TYPE001");
    }

    /// <summary>
    /// Section 15.1 step 8: <c>append</c> "leaves no mapping projection for later inference", so the
    /// rebased contribution must not also arrive as mapping children.
    /// </summary>
    [Test]
    public void AppendLeavesNoMappingProjectionForTheRebasedContribution() =>
        ChildNames(Descend(Merge(Strategy(MergeStrategy.Append, "a"), "a.0=x", "a.1=p"), "a"))
            .ShouldBe(["0"]);

    /// <summary>
    /// Section 5.4: "Allocating above the maximum ordering value is a blocking limit error." The
    /// mark here is already at the maximum, so the rebase has nowhere to go.
    /// </summary>
    [Test]
    public void AppendingAboveTheMaximumOrderingValueIsALimitError()
    {
        Merge(
            Strategy(MergeStrategy.Append, "a"),
            "a.9223372036854775807=x",
            "a.0=y");

        diagnostics.Drain().ShouldHaveSingleItem().Code.ShouldBe("LIMIT001");
    }

    /// <summary>
    /// The same clause applies to ordinary concatenation: an implicit item allocates
    /// <c>high-water + 1</c>, and there is no such value here.
    /// </summary>
    [Test]
    public void ConcatenatingAboveTheMaximumOrderingValueIsALimitError()
    {
        var earlier = OverlayNode.Empty(NodeMarks.At(StableOrderingKey.FromSource(1, 0)))
            .WithSequenceItem(long.MaxValue, SequenceItem.Numbered(Payload("x", 1)));

        var later = OverlayNode.Empty(NodeMarks.At(StableOrderingKey.FromSource(2, 0)))
            .WithSequenceItem(0, SequenceItem.Native(Payload("y", 2)));

        new OverlayMerger(MergeStrategyMap.Default, diagnostics).Merge(earlier, later);

        diagnostics.Drain().ShouldHaveSingleItem().Code.ShouldBe("LIMIT001");
    }

    // Section 17.1 native sequences, which reach the merger from structured input.

    /// <summary>
    /// Section 17.1: "implicit later items concatenate". Section 5.4: "A later native sequence
    /// therefore concatenates after all earlier allocated sequence items."
    /// </summary>
    [Test]
    public void ImplicitLaterSequenceItemsConcatenate()
    {
        var merged = new OverlayMerger(MergeStrategyMap.Default, diagnostics)
            .Merge(NativeSequence(1, "x", "y"), NativeSequence(2, "p"));

        merged.OrderedSequence.Select(item => item.Key).ShouldBe([0L, 1L, 2L]);
        merged.OrderedSequence.Select(item => Value(item.Value.Node)).ShouldBe(["x", "y", "p"]);
    }

    /// <summary>
    /// Section 17.1: "explicit later ordering values patch matching items", and Section 5.4:
    /// "Reusing an explicit ordering value overrides the existing item at that value."
    /// </summary>
    [Test]
    public void ExplicitLaterOrderingValuesPatchMatchingItems()
    {
        var later = OverlayNode.Empty(NodeMarks.At(StableOrderingKey.FromSource(2, 0)))
            .WithSequenceItem(1, SequenceItem.Numbered(Payload("patched", 2)));

        var merged = new OverlayMerger(MergeStrategyMap.Default, diagnostics)
            .Merge(NativeSequence(1, "x", "y"), later);

        merged.OrderedSequence.Select(item => item.Key).ShouldBe([0L, 1L]);
        merged.OrderedSequence.Select(item => Value(item.Value.Node)).ShouldBe(["x", "patched"]);
    }

    /// <summary>
    /// Section 5.4: "Automatic allocation never shifts, defragments, or reuses an ordering value
    /// because an item was removed or replaced." A patch at an existing value must not consume a
    /// fresh one for the items that follow it.
    /// </summary>
    [Test]
    public void PatchingAnExistingValueDoesNotConsumeAFreshOne()
    {
        var later = OverlayNode.Empty(NodeMarks.At(StableOrderingKey.FromSource(2, 0)))
            .WithSequenceItem(0, SequenceItem.Numbered(Payload("patched", 2)))
            .WithSequenceItem(7, SequenceItem.Native(Payload("appended", 2)));

        var merged = new OverlayMerger(MergeStrategyMap.Default, diagnostics)
            .Merge(NativeSequence(1, "x", "y"), later);

        merged.OrderedSequence.Select(item => item.Key).ShouldBe([0L, 1L, 2L]);
        merged.SequenceHighWater.ShouldBe(2);
    }

    private static OverlayNode Payload(string text, int source) =>
        OverlayNode.OfPayload(
            ScalarPayload.Untyped(text), StableOrderingKey.FromSource(source, 0));

    private static OverlayNode NativeSequence(int source, params string[] values)
    {
        var node = OverlayNode.Empty(NodeMarks.At(StableOrderingKey.FromSource(source, 0)));

        foreach (var value in values)
        {
            node.TryAppendSequenceItem(SequenceItem.Native(Payload(value, source)), out node)
                .ShouldBeTrue();
        }

        return node;
    }
}
