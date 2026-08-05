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

    [Test]
    public void RecordingAContributionAtTheSameKeyChangesNothing()
    {
        var marks = NodeMarks.ForMapping(Middle);

        marks.WithPayload(Middle).ShouldBe(marks);
        marks.WithMapping(Middle).ShouldBe(marks);
        marks.WithDescendant(Middle).ShouldBe(marks);
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
}
