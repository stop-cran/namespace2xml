using Namespace2Xml.Overlay;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 4.7 stable ordering key. Expectations are authored from the specification clause, which
/// states that the five components compare lexicographically in declaration order and that a
/// component which does not apply is zero.
/// </summary>
[TestFixture]
public sealed class StableOrderingKeyTests
{
    [Test]
    public void TheFirstKeyIsAllZeroes()
    {
        var first = StableOrderingKey.First;

        first.SourceOrdinal.ShouldBe(0);
        first.ItemOrdinal.ShouldBe(0);
        first.TransformationOrdinal.ShouldBe(0);
        first.MatchOrdinal.ShouldBe(0);
        first.GenerationOrdinal.ShouldBe(0);
    }

    [Test]
    public void AKeyForASourceItemLeavesTheInapplicableComponentsZero()
    {
        var key = StableOrderingKey.FromSource(3, 7);

        key.ShouldBe(new StableOrderingKey(3, 7, 0, 0, 0));
    }

    [Test]
    public void AKeyEqualsItself()
    {
        var key = new StableOrderingKey(1, 2, 3, 4, 5);
        var same = new StableOrderingKey(1, 2, 3, 4, 5);

        key.CompareTo(same).ShouldBe(0);
        (key <= same).ShouldBeTrue();
        (key >= same).ShouldBeTrue();
        (key < same).ShouldBeFalse();
        (key > same).ShouldBeFalse();
    }

    // Each case moves exactly one component, so the case name states which component decides.
    [TestCase(1, 0, 0, 0, 0, TestName = "TheSourceOrdinalDecidesFirst")]
    [TestCase(0, 1, 0, 0, 0, TestName = "TheItemOrdinalDecidesSecond")]
    [TestCase(0, 0, 1, 0, 0, TestName = "TheTransformationOrdinalDecidesThird")]
    [TestCase(0, 0, 0, 1, 0, TestName = "TheMatchOrdinalDecidesFourth")]
    [TestCase(0, 0, 0, 0, 1, TestName = "TheGenerationOrdinalDecidesLast")]
    public void RaisingOneComponentMakesAKeyLater(
        long source,
        long item,
        long transformation,
        long match,
        long generation)
    {
        var earlier = StableOrderingKey.First;
        var later = new StableOrderingKey(source, item, transformation, match, generation);

        (later > earlier).ShouldBeTrue();
        (earlier < later).ShouldBeTrue();
        StableOrderingKey.Later(earlier, later).ShouldBe(later);
        StableOrderingKey.Later(later, earlier).ShouldBe(later);
    }

    [Test]
    public void AnEarlierComponentOutranksEveryLaterOne()
    {
        var earlier = new StableOrderingKey(1, 0, 0, 0, 0);
        var later = new StableOrderingKey(
            2,
            long.MinValue + 1,
            long.MinValue + 1,
            long.MinValue + 1,
            long.MinValue + 1);

        (later > earlier).ShouldBeTrue();
    }

    /// <summary>
    /// Section 5.3: a generated entry inherits its rule's precedence position and is ordered after
    /// the plain source entry at that position, because the components that distinguish it are zero
    /// for the source entry.
    /// </summary>
    [Test]
    public void AGeneratedEntryFollowsThePlainSourceEntryAtTheSamePosition()
    {
        var sourceEntry = StableOrderingKey.FromSource(2, 5);
        var generated = sourceEntry with { TransformationOrdinal = 1 };

        (generated > sourceEntry).ShouldBeTrue();
    }

    /// <summary>
    /// Section 5.3: generated entries from one rule are ordered by the source order of their
    /// matches, and use the generation ordinal only to break ties.
    /// </summary>
    [Test]
    public void MatchOrderOutranksGenerationOrderAmongGeneratedEntries()
    {
        var rule = new StableOrderingKey(2, 5, 1, 0, 0);
        var firstMatchLastGenerated = rule with { MatchOrdinal = 0, GenerationOrdinal = 99 };
        var secondMatchFirstGenerated = rule with { MatchOrdinal = 1, GenerationOrdinal = 0 };

        (secondMatchFirstGenerated > firstMatchLastGenerated).ShouldBeTrue();
    }

    [Test]
    public void ComparisonIsATotalOrderOverAMixedCorpus()
    {
        var random = new Random(20260610);
        var keys = Enumerable.Range(0, 400)
            .Select(_ => new StableOrderingKey(
                random.Next(3),
                random.Next(3),
                random.Next(3),
                random.Next(3),
                random.Next(3)))
            .ToList();

        foreach (var left in keys)
        {
            foreach (var right in keys)
            {
                var forward = Math.Sign(left.CompareTo(right));
                var backward = Math.Sign(right.CompareTo(left));

                forward.ShouldBe(-backward, $"{left} against {right} is not antisymmetric");
                (forward == 0).ShouldBe(left == right, $"{left} against {right} disagrees with equality");

                foreach (var middle in keys.Take(20))
                {
                    if (left.CompareTo(middle) <= 0 && middle.CompareTo(right) <= 0)
                    {
                        (left.CompareTo(right) <= 0).ShouldBeTrue(
                            $"{left} <= {middle} <= {right} is not transitive");
                    }
                }
            }
        }
    }

    [Test]
    public void SortingUsesTheSpecifiedComponentOrder()
    {
        var keys = new[]
        {
            new StableOrderingKey(0, 0, 0, 0, 1),
            new StableOrderingKey(0, 0, 0, 1, 0),
            new StableOrderingKey(0, 0, 1, 0, 0),
            new StableOrderingKey(0, 1, 0, 0, 0),
            new StableOrderingKey(1, 0, 0, 0, 0),
        };

        var shuffled = keys.Reverse().ToList();
        shuffled.Sort();

        shuffled.ShouldBe(keys);
    }
}
