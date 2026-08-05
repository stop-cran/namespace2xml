using Namespace2Xml.Overlay;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 5.4 sequence ordering. Expectations are authored from the clause, not captured from the
/// allocator.
/// </summary>
[TestFixture]
public sealed class SequenceOrderingAllocatorTests
{
    [Test]
    public void AFreshPathStartsItsMarkAtMinusOne()
    {
        new SequenceOrderingAllocator().HighWaterMark.ShouldBe(-1);
    }

    [Test]
    public void TheFirstImplicitValueIsZero()
    {
        var allocator = new SequenceOrderingAllocator();

        allocator.TryAllocate(out var value).ShouldBeTrue();
        value.ShouldBe(0);
    }

    [Test]
    public void ImplicitValuesAreAllocatedOneAtATimeAsHighWaterPlusOne()
    {
        var allocator = new SequenceOrderingAllocator();
        var allocated = new List<long>();

        for (var index = 0; index < 5; index++)
        {
            allocator.TryAllocate(out var value).ShouldBeTrue();
            allocated.Add(value);
        }

        allocated.ShouldBe([0, 1, 2, 3, 4]);
        allocator.HighWaterMark.ShouldBe(4);
    }

    /// <summary>
    /// Section 5.4: "A later native sequence therefore concatenates after all earlier allocated
    /// sequence items."
    /// </summary>
    [Test]
    public void ALaterNativeSequenceConcatenatesAfterEarlierItems()
    {
        var allocator = new SequenceOrderingAllocator();

        allocator.TryAllocate(out _);
        allocator.TryAllocate(out _);
        allocator.TryAllocate(out var firstOfSecondSequence);

        firstOfSecondSequence.ShouldBe(2);
    }

    [Test]
    public void SupplyingAValueAboveTheMarkRaisesIt()
    {
        var allocator = new SequenceOrderingAllocator();

        allocator.Supply(10);

        allocator.HighWaterMark.ShouldBe(10);
    }

    [Test]
    public void AnImplicitValueAfterAnExplicitOneFollowsTheRaisedMark()
    {
        var allocator = new SequenceOrderingAllocator();

        allocator.Supply(10);
        allocator.TryAllocate(out var value).ShouldBeTrue();

        value.ShouldBe(11);
    }

    /// <summary>
    /// Section 5.4: "Reusing an explicit ordering value overrides the existing item at that value by
    /// ordinary source order." Overriding addresses an existing position and must not raise the mark.
    /// </summary>
    [Test]
    public void SupplyingAValueAtOrBelowTheMarkLeavesItAlone()
    {
        var allocator = new SequenceOrderingAllocator();

        allocator.Supply(10);
        allocator.Supply(10);
        allocator.Supply(3);
        allocator.Supply(0);

        allocator.HighWaterMark.ShouldBe(10);
    }

    [Test]
    public void OverridingAnEarlierItemDoesNotDisturbTheNextAllocation()
    {
        var allocator = new SequenceOrderingAllocator();

        allocator.TryAllocate(out _);
        allocator.TryAllocate(out _);
        allocator.TryAllocate(out _);
        allocator.Supply(1);
        allocator.TryAllocate(out var value).ShouldBeTrue();

        value.ShouldBe(3);
    }

    /// <summary>
    /// Section 5.4: the mark records values "including values later removed or replaced", and
    /// automatic allocation "never shifts, defragments, or reuses an ordering value because an item
    /// was removed or replaced". Removal is invisible to the allocator, which is the mechanism.
    /// </summary>
    [Test]
    public void TheMarkNeverLowers()
    {
        var allocator = new SequenceOrderingAllocator();

        allocator.Supply(100);
        allocator.Supply(4);
        allocator.TryAllocate(out _);
        allocator.Supply(0);

        allocator.HighWaterMark.ShouldBe(101);
    }

    /// <summary>Section 5.4: "Gaps and nonzero bases are retained internally."</summary>
    [Test]
    public void ANonzeroBaseIsRetained()
    {
        var allocator = new SequenceOrderingAllocator();

        allocator.Supply(1000);
        allocator.TryAllocate(out var first).ShouldBeTrue();
        allocator.TryAllocate(out var second).ShouldBeTrue();

        first.ShouldBe(1001);
        second.ShouldBe(1002);
    }

    [Test]
    public void AGapNeverAllocatesIntoItself()
    {
        var allocator = new SequenceOrderingAllocator();

        allocator.Supply(0);
        allocator.Supply(5);
        allocator.TryAllocate(out var value).ShouldBeTrue();

        value.ShouldBe(6);
    }

    /// <summary>
    /// Section 5.4 append rebasing: "first raise the current high-water mark to at least its
    /// supplied value, then allocate its new value as high-water + 1".
    /// </summary>
    [Test]
    public void RebasingRaisesTheMarkBeforeAllocating()
    {
        var allocator = new SequenceOrderingAllocator();

        allocator.TryRebase(10, out var value).ShouldBeTrue();

        value.ShouldBe(11);
        allocator.HighWaterMark.ShouldBe(11);
    }

    [Test]
    public void RebasingASuppliedValueBelowTheMarkStillAppends()
    {
        var allocator = new SequenceOrderingAllocator();

        allocator.Supply(20);
        allocator.TryRebase(3, out var value).ShouldBeTrue();

        value.ShouldBe(21);
    }

    /// <summary>
    /// Processing a contribution's items in ascending original value, as Section 5.4 requires,
    /// preserves their relative order and appends them after everything already allocated.
    /// </summary>
    [Test]
    public void RebasingAContributionInAscendingOrderPreservesRelativeOrder()
    {
        var allocator = new SequenceOrderingAllocator();

        allocator.Supply(7);

        var rebased = new List<long>();

        foreach (var supplied in new long[] { 2, 5, 40 })
        {
            allocator.TryRebase(supplied, out var value).ShouldBeTrue();
            rebased.Add(value);
        }

        rebased.ShouldBe([8, 9, 41]);
    }

    [Test]
    public void AllocationAtTheMaximumOrderingValueFails()
    {
        var allocator = new SequenceOrderingAllocator();

        allocator.Supply(SequenceOrderingAllocator.MaxOrderingValue);

        allocator.TryAllocate(out _).ShouldBeFalse();
        allocator.HighWaterMark.ShouldBe(SequenceOrderingAllocator.MaxOrderingValue);
    }

    [Test]
    public void AllocationOneBelowTheMaximumSucceedsAndTheNextFails()
    {
        var allocator = new SequenceOrderingAllocator();

        allocator.Supply(SequenceOrderingAllocator.MaxOrderingValue - 1);

        allocator.TryAllocate(out var value).ShouldBeTrue();
        value.ShouldBe(SequenceOrderingAllocator.MaxOrderingValue);
        allocator.TryAllocate(out _).ShouldBeFalse();
    }

    [Test]
    public void RebasingAtTheMaximumOrderingValueFails()
    {
        var allocator = new SequenceOrderingAllocator();

        allocator.TryRebase(SequenceOrderingAllocator.MaxOrderingValue, out _).ShouldBeFalse();
    }

    [Test]
    public void AFailedAllocationDoesNotWrapTheMark()
    {
        var allocator = new SequenceOrderingAllocator();

        allocator.Supply(SequenceOrderingAllocator.MaxOrderingValue);
        allocator.TryAllocate(out _);
        allocator.TryAllocate(out _);

        allocator.HighWaterMark.ShouldBe(SequenceOrderingAllocator.MaxOrderingValue);
    }

    [TestCase(0L, ExpectedResult = true)]
    [TestCase(1L, ExpectedResult = true)]
    [TestCase(long.MaxValue, ExpectedResult = true)]
    [TestCase(-1L, ExpectedResult = false)]
    [TestCase(long.MinValue, ExpectedResult = false)]
    public bool OrderingValuesRunFromZeroToTheSigned64BitMaximum(long value) =>
        SequenceOrderingAllocator.IsOrderingValue(value);

    [Test]
    public void SupplyingANegativeValueIsARejectedProgrammingError()
    {
        var allocator = new SequenceOrderingAllocator();

        Should.Throw<ArgumentOutOfRangeException>(() => allocator.Supply(-1));
    }

    /// <summary>
    /// Each sequence path has its own mark, so allocation on one path never advances another.
    /// </summary>
    [Test]
    public void EachPathHasItsOwnMark()
    {
        var first = new SequenceOrderingAllocator();
        var second = new SequenceOrderingAllocator();

        first.Supply(500);
        second.TryAllocate(out var value).ShouldBeTrue();

        value.ShouldBe(0);
    }
}
