using System.Linq;
using Namespace2Xml.Overlay;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 4.4 position marks and shape marks. Expectations are authored from the clause, in
/// particular its separation of "the latest contribution that addresses this node itself" from "the
/// latest contribution requiring this shape anywhere beneath it".
/// </summary>
[TestFixture]
public sealed class NodeMarksTests
{
    private static readonly StableOrderingKey Early = new(1, 0, 0, 0, 0);
    private static readonly StableOrderingKey Middle = new(2, 0, 0, 0, 0);
    private static readonly StableOrderingKey Late = new(3, 0, 0, 0, 0);

    [Test]
    public void APayloadContributionSetsNoShapeMark()
    {
        var marks = NodeMarks.ForPayload(Early);

        marks.Position.ShouldBe(Early);
        marks.MappingShape.ShouldBeNull();
        marks.SequenceShape.ShouldBeNull();
        marks.ContainerShape.ShouldBeNull();
    }

    [Test]
    public void ANodeWithNoShapeContributionIsNeitherAMappingNorASequence()
    {
        var marks = NodeMarks.ForPayload(Early);

        marks.RendersAsMapping.ShouldBeFalse();
        marks.RendersAsSequence.ShouldBeFalse();
    }

    [Test]
    public void AMappingContributionSetsBothThePositionAndTheMappingShapeMark()
    {
        var marks = NodeMarks.ForMapping(Early);

        marks.Position.ShouldBe(Early);
        marks.MappingShape.ShouldBe(Early);
        marks.SequenceShape.ShouldBeNull();
        marks.ContainerShape.ShouldBe(Early);
        marks.RendersAsMapping.ShouldBeTrue();
        marks.RendersAsSequence.ShouldBeFalse();
    }

    [Test]
    public void ASequenceContributionSetsBothThePositionAndTheSequenceShapeMark()
    {
        var marks = NodeMarks.ForSequence(Early);

        marks.Position.ShouldBe(Early);
        marks.SequenceShape.ShouldBe(Early);
        marks.MappingShape.ShouldBeNull();
        marks.ContainerShape.ShouldBe(Early);
        marks.RendersAsSequence.ShouldBeTrue();
        marks.RendersAsMapping.ShouldBeFalse();
    }

    /// <summary>
    /// Section 4.4: the container shape-mark is the later of the two, and Section 5 gives the later
    /// contribution the win at an exclusive destination.
    /// </summary>
    [Test]
    public void TheLaterShapeContributionWinsTheContest()
    {
        var mappingFirst = NodeMarks.ForMapping(Early).WithSequence(Late);
        var sequenceFirst = NodeMarks.ForSequence(Early).WithMapping(Late);

        mappingFirst.ContainerShape.ShouldBe(Late);
        mappingFirst.RendersAsSequence.ShouldBeTrue();
        mappingFirst.RendersAsMapping.ShouldBeFalse();

        sequenceFirst.ContainerShape.ShouldBe(Late);
        sequenceFirst.RendersAsMapping.ShouldBeTrue();
        sequenceFirst.RendersAsSequence.ShouldBeFalse();
    }

    /// <summary>
    /// An absent shape mark must lose to any present one, including one at the very first ordering
    /// key. Spelling absence as the smallest key would make it win that tie.
    /// </summary>
    [Test]
    public void AnAbsentShapeMarkLosesToAShapeMarkAtTheFirstKey()
    {
        var marks = NodeMarks.ForMapping(StableOrderingKey.First);

        marks.RendersAsMapping.ShouldBeTrue();
        marks.RendersAsSequence.ShouldBeFalse();
        marks.ContainerShape.ShouldBe(StableOrderingKey.First);
    }

    /// <summary>
    /// Section 5.2: "Adding a new child therefore never moves its parent." A descendant refreshes the
    /// ancestor's mapping shape-mark and leaves its position mark where it was.
    /// </summary>
    [Test]
    public void ADescendantRefreshesTheMappingShapeMarkWithoutMovingThePosition()
    {
        var marks = NodeMarks.ForMapping(Early).WithDescendant(Late);

        marks.Position.ShouldBe(Early);
        marks.MappingShape.ShouldBe(Late);
    }

    [Test]
    public void ADescendantGivesMappingShapeToANodeThatHadNone()
    {
        var marks = NodeMarks.ForPayload(Early).WithDescendant(Middle);

        marks.Position.ShouldBe(Early);
        marks.MappingShape.ShouldBe(Middle);
        marks.RendersAsMapping.ShouldBeTrue();
    }

    /// <summary>
    /// A descendant contributes mapping shape only: a child beneath a node says nothing about whether
    /// that node's own contributions were sequence items.
    /// </summary>
    [Test]
    public void ADescendantNeverTouchesTheSequenceShapeMark()
    {
        var marks = NodeMarks.ForSequence(Middle).WithDescendant(Late);

        marks.SequenceShape.ShouldBe(Middle);
        marks.MappingShape.ShouldBe(Late);
        marks.RendersAsMapping.ShouldBeTrue();
    }

    /// <summary>
    /// A deep descendant can therefore flip an ancestor to mapping shape without moving it, which is
    /// the whole reason the two kinds of mark are separate.
    /// </summary>
    [Test]
    public void ADescendantCanWinTheShapeContestWithoutReorderingTheNode()
    {
        var marks = NodeMarks.ForSequence(Early).WithDescendant(Late);

        marks.Position.ShouldBe(Early);
        marks.RendersAsMapping.ShouldBeTrue();
    }

    [Test]
    public void MarksOnlyEverAdvance()
    {
        var marks = NodeMarks.ForMapping(Late)
            .WithPayload(Early)
            .WithMapping(Early)
            .WithSequence(Middle)
            .WithDescendant(Early);

        marks.Position.ShouldBe(Late);
        marks.MappingShape.ShouldBe(Late);
        marks.SequenceShape.ShouldBe(Middle);
    }

    /// <summary>
    /// Marks are idempotent under re-recording the same contribution. The contribution has to be of
    /// the same kind: Section 4.7 makes two contributions with one key the same contribution, so a
    /// mapping-presence and a scalar contribution cannot share a key, and recording a payload
    /// against a mapping's key is a state no input produces rather than a no-op.
    /// </summary>
    [Test]
    public void RecordingAContributionAtTheSameKeyChangesNothing()
    {
        var mapping = NodeMarks.ForMapping(Middle);

        mapping.WithMapping(Middle).ShouldBe(mapping);
        mapping.WithDescendant(Middle).ShouldBe(mapping);

        var payload = NodeMarks.ForPayload(Middle);

        payload.WithPayload(Middle).ShouldBe(payload);

        var sequence = NodeMarks.ForSequence(Middle);

        sequence.WithSequence(Middle).ShouldBe(sequence);
        sequence.WithSequenceItem(Middle).ShouldBe(sequence);
    }

    /// <summary>
    /// Section 4.4 step 1 needs "the latest scalar/null contribution at the node", which the
    /// position mark cannot supply once an explicit mapping-presence contribution has advanced it.
    /// </summary>
    [Test]
    public void ThePayloadMarkTracksScalarContributionsOnly()
    {
        NodeMarks.ForMapping(Early).PayloadMark.ShouldBeNull();
        NodeMarks.ForSequence(Early).PayloadMark.ShouldBeNull();
        NodeMarks.At(Early).PayloadMark.ShouldBeNull();

        var marks = NodeMarks.ForPayload(Early).WithMapping(Late);

        marks.Position.ShouldBe(Late);
        marks.PayloadMark.ShouldBe(Early);
    }

    /// <summary>
    /// Section 4.4 steps 1 to 3, in the direction the Section 4.4 example calls out: "reversing
    /// source order makes the later scalar win".
    /// </summary>
    [Test]
    public void TheLaterOfThePayloadAndTheContainerDecidesTheShape()
    {
        var containerLater = NodeMarks.ForPayload(Early).WithDescendant(Late);

        containerLater.RendersAsContainer.ShouldBeTrue();
        containerLater.RendersAsMapping.ShouldBeTrue();
        containerLater.RendersAsScalar.ShouldBeFalse();

        var payloadLater = NodeMarks.ForMapping(Early).WithPayload(Late);

        payloadLater.RendersAsScalar.ShouldBeTrue();
        payloadLater.RendersAsContainer.ShouldBeFalse();
        payloadLater.RendersAsMapping.ShouldBeFalse();
        payloadLater.RendersAsSequence.ShouldBeFalse();
    }

    /// <summary>
    /// Section 4.4 step 3 judges the payload against the container shape-mark, which is "the later
    /// of the mapping and sequence shape-marks". A sequence contribution therefore loses to a later
    /// payload exactly as a mapping contribution does; asserting only the mapping direction would
    /// leave an implementation that consulted the payload for one facet and not the other intact.
    /// </summary>
    [Test]
    public void ASequenceAlsoLosesToALaterPayload()
    {
        var sequenceLater = NodeMarks.ForPayload(Early).WithSequenceItem(Late);

        sequenceLater.RendersAsSequence.ShouldBeTrue();
        sequenceLater.RendersAsScalar.ShouldBeFalse();

        var payloadLater = NodeMarks.ForSequence(Early).WithPayload(Late);

        payloadLater.RendersAsScalar.ShouldBeTrue();
        payloadLater.RendersAsContainer.ShouldBeFalse();
        payloadLater.RendersAsSequence.ShouldBeFalse();
        payloadLater.RendersAsMapping.ShouldBeFalse();
    }

    [Test]
    public void ANodeWithNoContributionsRendersAsNothing()
    {
        var marks = NodeMarks.At(Early);

        marks.RendersAsScalar.ShouldBeFalse();
        marks.RendersAsContainer.ShouldBeFalse();
        marks.RendersAsMapping.ShouldBeFalse();
        marks.RendersAsSequence.ShouldBeFalse();
    }

    /// <summary>
    /// A sequence item is a descendant: it refreshes the sequence shape-mark and leaves the
    /// position mark alone, exactly as a mapping child does for the mapping shape-mark.
    /// </summary>
    [Test]
    public void ASequenceItemRefreshesShapeWithoutMovingTheNode()
    {
        var marks = NodeMarks.ForSequence(Early).WithSequenceItem(Late);

        marks.Position.ShouldBe(Early);
        marks.SequenceShape.ShouldBe(Late);
        marks.MappingShape.ShouldBeNull();
    }

    /// <summary>
    /// Section 4.4: the position mark is "the latest contribution that addresses this node itself".
    /// An explicit mapping-presence contribution addresses the node, so it moves it -- unlike a
    /// descendant, which does not.
    /// </summary>
    [Test]
    public void AMappingContributionAtTheNodeAdvancesItsPosition()
    {
        var marks = NodeMarks.ForPayload(Early).WithMapping(Late);

        marks.Position.ShouldBe(Late);
        marks.MappingShape.ShouldBe(Late);
    }

    [Test]
    public void ASequenceContributionAtTheNodeAdvancesItsPosition()
    {
        var marks = NodeMarks.ForPayload(Early).WithSequence(Late);

        marks.Position.ShouldBe(Late);
        marks.SequenceShape.ShouldBe(Late);
    }

    /// <summary>
    /// The distinction is exactly the difference between contributing shape here and contributing it
    /// from below: the same key moves the node in one case and not the other.
    /// </summary>
    [Test]
    public void OnlyTheContributionAtTheNodeMovesIt()
    {
        var here = NodeMarks.ForPayload(Early).WithMapping(Late);
        var below = NodeMarks.ForPayload(Early).WithDescendant(Late);

        here.MappingShape.ShouldBe(below.MappingShape);
        here.Position.ShouldBe(Late);
        below.Position.ShouldBe(Early);
    }

    [Test]
    public void APayloadContributionAdvancesThePositionWithoutGivingShape()
    {
        var marks = NodeMarks.ForPayload(Early).WithPayload(Late);

        marks.Position.ShouldBe(Late);
        marks.MappingShape.ShouldBeNull();
        marks.SequenceShape.ShouldBeNull();
    }

    [Test]
    public void ContributionOrderDoesNotChangeTheResultingMarks()
    {
        var forward = NodeMarks.ForMapping(Early).WithSequence(Middle).WithDescendant(Late);
        var backward = NodeMarks.ForSequence(Middle).WithMapping(Early).WithDescendant(Late);

        forward.ShouldBe(backward);
    }

    /// <summary>
    /// Section 22 owes one <c>WARN010</c> per source contribution, so two documents writing the
    /// same node are two origins rather than one.
    /// </summary>
    [Test]
    public void EachNativeMappingContributionIsRetainedSeparately()
    {
        var marks = NodeMarks.ForMapping(Early)
            .WithNativeMapping(Early, "one.json")
            .WithNativeMapping(Late, "two.json");

        marks.NativeMappings.Select(origin => origin.Source)
            .ShouldBe(["one.json", "two.json"]);
    }

    /// <summary>
    /// Section 24 fixes the order of the resulting diagnostics, so the origins cannot be presented
    /// in the order the tree happened to be walked.
    /// </summary>
    [Test]
    public void NativeMappingOriginsAreOrderedByTheirOrderingKeyRatherThanByArrival()
    {
        var marks = NodeMarks.ForMapping(Early)
            .WithNativeMapping(Late, "late.json")
            .WithNativeMapping(Early, "early.json")
            .WithNativeMapping(Middle, "middle.json");

        marks.NativeMappings.Select(origin => origin.Key)
            .ShouldBe([Early, Middle, Late]);
    }

    /// <summary>
    /// One warning is owed per source contribution, not one per merge that carried it, so a
    /// contribution combined into a node twice remains one origin.
    /// </summary>
    [Test]
    public void ARepeatedNativeMappingContributionIsNotCountedTwice()
    {
        var marks = NodeMarks.ForMapping(Early)
            .WithNativeMapping(Early, "one.json")
            .WithNativeMapping(Early, "one.json");

        marks.NativeMappings.Length.ShouldBe(1);
    }

    /// <summary>
    /// Combining two nodes must not lose either side's contributions, because a warning names the
    /// document that wrote the keys and both documents did.
    /// </summary>
    [Test]
    public void CombiningTwoNodesUnionsTheirNativeMappingOrigins()
    {
        var left = NodeMarks.ForMapping(Early).WithNativeMapping(Early, "one.json");
        var right = NodeMarks.ForMapping(Late).WithNativeMapping(Late, "two.json");

        left.Combine(right).NativeMappings.Select(origin => origin.Source)
            .ShouldBe(["one.json", "two.json"]);
    }

    /// <summary>
    /// Section 8.7 infers a sequence from a node that still carries its native mapping origins, so
    /// erasing the mapping shape must not erase the record of who wrote it.
    /// </summary>
    [Test]
    public void InferringASequenceKeepsTheNativeMappingOrigins()
    {
        var marks = NodeMarks.ForMapping(Early)
            .WithNativeMapping(Early, "one.json")
            .AsInferredSequence();

        marks.RendersAsSequence.ShouldBeTrue();
        marks.NativeMappings.Length.ShouldBe(1);
    }

    /// <summary>
    /// A mask that removes the mapping removes the contribution that produced it, so there is no
    /// longer a source whose keys the output could discard.
    /// </summary>
    [Test]
    public void RemovingTheMappingClearsTheNativeMappingOrigins()
    {
        var marks = NodeMarks.ForMapping(Early)
            .WithNativeMapping(Early, "one.json")
            .WithoutMapping();

        marks.NativeMappings.ShouldBeEmpty();
    }

    /// <summary>
    /// Section 3.2 owes the warning for "a mapping inferred at step 11", so a node step 11 declined
    /// to infer keeps no record that could later earn one.
    /// </summary>
    [Test]
    public void DecliningToInferAMappingDiscardsTheNativeMappingOrigins()
    {
        var marks = NodeMarks.ForMapping(Early)
            .WithNativeMapping(Early, "one.json")
            .WithoutNativeMappings();

        marks.NativeMappings.ShouldBeEmpty();
    }

    /// <summary>
    /// Discarding the origins settles who is warned about, not what the node renders as, so the
    /// mapping a Section 17.1 shape contest is about survives the pruning intact.
    /// </summary>
    [Test]
    public void DiscardingTheNativeMappingOriginsLeavesEveryShapeMarkAlone()
    {
        var marks = NodeMarks.ForMapping(Early)
            .WithNativeMapping(Early, "one.json")
            .WithSequenceItem(Late);

        var pruned = marks.WithoutNativeMappings();

        pruned.NativeMappings.ShouldBeEmpty();
        pruned.MappingShape.ShouldBe(marks.MappingShape);
        pruned.SequenceShape.ShouldBe(marks.SequenceShape);
        pruned.RendersAsSequence.ShouldBe(marks.RendersAsSequence);
    }
}
