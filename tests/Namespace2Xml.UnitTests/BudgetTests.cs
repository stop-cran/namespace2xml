using System.Reflection;
using Namespace2Xml.Budgets;
using Namespace2Xml.Cli;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 7.3 two-tier budgets and the Section 22 attribution order. Expectations are authored from
/// those clauses and from the Section 16.2 bound table.
/// </summary>
[TestFixture]
public sealed class BudgetTests
{
    private static ResourceLimits Limits(Func<ResourceLimits, ResourceLimits> configure) =>
        configure(ResourceLimits.Defaults);

    // ---- Section 7.3: a parse worker cannot observe a global total -------------------------------

    /// <summary>
    /// Section 7.3: "A parser must not be able to observe any global total". This is the structural
    /// gate the clause asks for. It fails if anyone gives <see cref="SourceBudget"/> a field through
    /// which another source's contribution, or a shared counter, could arrive.
    /// </summary>
    [Test]
    public void SourceBudgetIsIsolatedFromGlobalTotals()
    {
        var permitted = new[] { typeof(ResourceLimits), typeof(long), typeof(BudgetFault?) };

        var offending = typeof(SourceBudget)
            .GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => !permitted.Contains(field.FieldType))
            .Select(field => $"{field.FieldType.Name} {field.Name}")
            .ToList();

        offending.ShouldBeEmpty(
            "a parse worker may hold configured bounds and its own counters, and nothing else");
    }

    [Test]
    public void SourceBudgetTakesNoGlobalBudgetAnywhereInItsApi()
    {
        var reachable = typeof(SourceBudget)
            .GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .OfType<MethodBase>()
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Append(typeof(SourceBudget).GetProperty(nameof(SourceBudget.Tally))!.PropertyType);

        reachable.ShouldNotContain(typeof(GlobalBudget));
    }

    // ---- Section 7.3 and 16.2: per-source bounds are not cumulative ------------------------------

    [Test]
    public void ThePerFileByteBoundIsCheckedWithinOneSource()
    {
        var budget = new SourceBudget(Limits(l => l with { MaxInputBytes = 10 }), 0);

        budget.TryAddInputBytes(6).ShouldBeTrue();
        budget.TryAddInputBytes(4).ShouldBeTrue();
        budget.TryAddInputBytes(1).ShouldBeFalse();
        budget.Fault!.Value.Bound.ShouldBe(ResourceBound.MaxInputBytes);
        budget.Fault!.Value.Limit.ShouldBe(10);
    }

    /// <summary>
    /// Section 7.3: per-file byte limits "are never cumulative across sources". Two sources at the
    /// bound are two admissible sources.
    /// </summary>
    [Test]
    public void ThePerFileByteBoundNeverAccumulatesAcrossSources()
    {
        var limits = Limits(l => l with { MaxInputBytes = 10 });
        var first = new SourceBudget(limits, 0);
        var second = new SourceBudget(limits, 1);

        first.TryAddInputBytes(10).ShouldBeTrue();
        second.TryAddInputBytes(10).ShouldBeTrue();

        first.Fault.ShouldBeNull();
        second.Fault.ShouldBeNull();
    }

    [Test]
    public void DepthIsALevelAndNotARunningTotal()
    {
        var budget = new SourceBudget(Limits(l => l with { MaxDepth = 3 }), 0);

        budget.TryEnterDepth(3).ShouldBeTrue();
        budget.TryEnterDepth(3).ShouldBeTrue();
        budget.TryEnterDepth(2).ShouldBeTrue();
        budget.TryEnterDepth(4).ShouldBeFalse();
        budget.Fault!.Value.Bound.ShouldBe(ResourceBound.MaxDepth);
    }

    [Test]
    public void AttributesAreCheckedPerElementAndNotPerDocument()
    {
        var budget = new SourceBudget(Limits(l => l with { MaxXmlAttributes = 2 }), 0);

        budget.TryAddXmlAttributes(2, elementOrder: 0).ShouldBeTrue();
        budget.TryAddXmlAttributes(2, elementOrder: 1).ShouldBeTrue();
        budget.TryAddXmlAttributes(3, elementOrder: 2).ShouldBeFalse();
        budget.Fault!.Value.Bound.ShouldBe(ResourceBound.MaxXmlAttributes);
        budget.Fault!.Value.ElementOrder.ShouldBe(2);
    }

    /// <summary>
    /// A parser reaches positions in document order, so the first crossing it sees is already the
    /// earliest by Section 22 order and a later one must not displace it.
    /// </summary>
    [Test]
    public void ThePerSourceFaultIsTheFirstCrossingReached()
    {
        var budget = new SourceBudget(Limits(l => l with { MaxDepth = 1, MaxXmlAttributes = 1 }), 0);

        budget.TryEnterDepth(9, documentOrder: 5).ShouldBeFalse();
        budget.TryAddXmlAttributes(9, documentOrder: 9).ShouldBeFalse();

        budget.Fault!.Value.Bound.ShouldBe(ResourceBound.MaxDepth);
        budget.Fault!.Value.DocumentOrder.ShouldBe(5);
    }

    [Test]
    public void GlobalCountsAreTalliedByTheWorkerAndJudgedByNobodyThere()
    {
        var budget = new SourceBudget(Limits(l => l with { MaxNodes = 1, MaxComments = 1, MaxCommentBytes = 1 }), 0);

        budget.AddNodes(1_000);
        budget.AddComments(50, 4_000);

        budget.Fault.ShouldBeNull();
        budget.Tally.ShouldBe(new SourceTally(0, 1_000, 50, 4_000));
    }

    // ---- Section 7.3: the join -------------------------------------------------------------------

    [Test]
    public void SourcesWithinTheGlobalBoundsAreAllAdmitted()
    {
        var budget = new GlobalBudget(Limits(l => l with { MaxTotalInputBytes = 10 }));

        budget.TryAdmit(new SourceTally(4, 0, 0, 0), 0, out var first).ShouldBeTrue();
        budget.TryAdmit(new SourceTally(6, 0, 0, 0), 1, out var second).ShouldBeTrue();

        first.ShouldBeNull();
        second.ShouldBeNull();
        budget.InputStreamClosed.ShouldBeFalse();
    }

    /// <summary>
    /// Section 7.3: "The first source whose cumulative contribution would cross a global bound
    /// receives LIMIT001." The bound is cumulative, so a source that would fit on its own can still
    /// be the one that crosses.
    /// </summary>
    [Test]
    public void TheFirstSourceThatCrossesACumulativeBoundIsTheOneReported()
    {
        var budget = new GlobalBudget(Limits(l => l with { MaxTotalInputBytes = 10 }));

        budget.TryAdmit(new SourceTally(6, 0, 0, 0), 0, out _).ShouldBeTrue();
        budget.TryAdmit(new SourceTally(5, 0, 0, 0), 1, out var fault).ShouldBeFalse();

        fault.ShouldNotBeNull();
        fault!.Value.Bound.ShouldBe(ResourceBound.MaxTotalInputBytes);
        fault!.Value.SourceOrdinal.ShouldBe(1);
    }

    /// <summary>
    /// Section 7.3: "that source and every later source in that stream contribute no parsed model,
    /// including later sources of a different kind".
    /// </summary>
    [Test]
    public void EveryLaterSourceIsRefusedOnceTheStreamCloses()
    {
        var budget = new GlobalBudget(Limits(l => l with { MaxNodes = 1 }));

        budget.TryAdmit(new SourceTally(0, 2, 0, 0), 0, out _).ShouldBeFalse();
        budget.InputStreamClosed.ShouldBeTrue();
        budget.TryAdmit(SourceTally.Empty, 1, out _).ShouldBeFalse();
        budget.TryAdmit(SourceTally.Empty, 2, out _).ShouldBeFalse();
    }

    /// <summary>
    /// Section 22 makes <c>LIMIT001</c> once per invocation, so only the first refusal produces a
    /// fault to report.
    /// </summary>
    [Test]
    public void OnlyTheFirstRefusalProducesAFault()
    {
        var budget = new GlobalBudget(Limits(l => l with { MaxNodes = 1 }));

        budget.TryAdmit(new SourceTally(0, 2, 0, 0), 0, out var first);
        budget.TryAdmit(new SourceTally(0, 2, 0, 0), 1, out var second);
        budget.TryAdmit(new SourceTally(0, 2, 0, 0), 2, out var third);

        first.ShouldNotBeNull();
        second.ShouldBeNull();
        third.ShouldBeNull();
    }

    [Test]
    public void ARefusedSourceContributesNothingToTheRunningTotals()
    {
        var budget = new GlobalBudget(Limits(l => l with { MaxTotalInputBytes = 10, MaxNodes = 1 }));

        budget.TryAdmit(new SourceTally(3, 5, 0, 0), 0, out _).ShouldBeFalse();
        budget.TryAdmit(new SourceTally(3, 0, 0, 0), 1, out _).ShouldBeFalse();
    }

    /// <summary>
    /// Section 22: when several bounds are crossed at one position, the tie is broken by "the bound
    /// name compared as unsigned UTF-8 bytes". A source's contribution is one position.
    /// </summary>
    [Test]
    public void SeveralBoundsCrossedByOneSourceAreBrokenByBoundName()
    {
        var budget = new GlobalBudget(
            Limits(l => l with { MaxTotalInputBytes = 1, MaxNodes = 1, MaxComments = 1, MaxCommentBytes = 1 }));

        budget.TryAdmit(new SourceTally(9, 9, 9, 9), 0, out var fault).ShouldBeFalse();

        fault!.Value.Bound.ShouldBe(ResourceBound.MaxCommentBytes);
        fault!.Value.Spelling.ShouldBe("--max-comment-bytes");
    }

    [Test]
    public void AdmissionIsUnaffectedByHowManyWorkersProducedTheTallies()
    {
        var limits = Limits(l => l with { MaxNodes = 10 });
        var tallies = new[]
        {
            new SourceTally(0, 4, 0, 0),
            new SourceTally(0, 4, 0, 0),
            new SourceTally(0, 4, 0, 0),
        };

        var forward = new GlobalBudget(limits);
        var results = tallies
            .Select((tally, index) => forward.TryAdmit(tally, index, out _))
            .ToList();

        results.ShouldBe([true, true, false]);
    }

    [Test]
    public void AGlobalBoundOfZeroRefusesAnyContributionAndAdmitsAnEmptyOne()
    {
        var budget = new GlobalBudget(Limits(l => l with { MaxNodes = 0 }));

        budget.TryAdmit(SourceTally.Empty, 0, out _).ShouldBeTrue();
        budget.TryAdmit(new SourceTally(0, 1, 0, 0), 1, out var fault).ShouldBeFalse();
        fault!.Value.Bound.ShouldBe(ResourceBound.MaxNodes);
    }

    /// <summary>
    /// The running totals must not overflow before they are compared, which they would if the check
    /// were written as <c>running + contribution &gt; limit</c>.
    /// </summary>
    [Test]
    public void ACrossingNearTheSigned64BitMaximumIsDetectedRatherThanWrapped()
    {
        var budget = new GlobalBudget(Limits(l => l with { MaxNodes = long.MaxValue }));

        budget.TryAdmit(new SourceTally(0, long.MaxValue - 1, 0, 0), 0, out _).ShouldBeTrue();
        budget.TryAdmit(new SourceTally(0, 2, 0, 0), 1, out var fault).ShouldBeFalse();

        fault!.Value.Bound.ShouldBe(ResourceBound.MaxNodes);
    }

    // ---- Section 16.2: pipeline-phase bounds ------------------------------------------------------

    [Test]
    public void APipelineBoundAccumulatesUntilItIsCrossed()
    {
        var budget = new GlobalBudget(Limits(l => l with { MaxGenerated = 5 }));

        budget.TryConsume(ResourceBound.MaxGenerated, 3, out _).ShouldBeTrue();
        budget.TryConsume(ResourceBound.MaxGenerated, 2, out _).ShouldBeTrue();
        budget.TryConsume(ResourceBound.MaxGenerated, 1, out var fault).ShouldBeFalse();

        fault.Bound.ShouldBe(ResourceBound.MaxGenerated);
        fault.Limit.ShouldBe(5);
        budget.Consumed(ResourceBound.MaxGenerated).ShouldBe(5);
    }

    /// <summary>
    /// Section 16.2: "Accounting occurs before allocation or expansion whenever possible." A refused
    /// consumption charges nothing.
    /// </summary>
    [Test]
    public void ARefusedConsumptionChargesNothing()
    {
        var budget = new GlobalBudget(Limits(l => l with { MaxOutputs = 5 }));

        budget.TryConsume(ResourceBound.MaxOutputs, 100, out _).ShouldBeFalse();

        budget.Consumed(ResourceBound.MaxOutputs).ShouldBe(0);
    }

    [Test]
    public void PipelineBoundsAreIndependentOfEachOther()
    {
        var budget = new GlobalBudget(Limits(l => l with { MaxOutputs = 1, MaxGenerated = 1 }));

        budget.TryConsume(ResourceBound.MaxOutputs, 1, out _).ShouldBeTrue();
        budget.TryConsume(ResourceBound.MaxGenerated, 1, out _).ShouldBeTrue();
        budget.TryConsume(ResourceBound.MaxOutputs, 1, out _).ShouldBeFalse();
    }

    [Test]
    public void APipelineLevelIsCheckedWithoutBeingConsumed()
    {
        var budget = new GlobalBudget(Limits(l => l with { MaxReferenceDepth = 2 }));

        budget.TryEnter(ResourceBound.MaxReferenceDepth, 2, out _).ShouldBeTrue();
        budget.TryEnter(ResourceBound.MaxReferenceDepth, 2, out _).ShouldBeTrue();
        budget.TryEnter(ResourceBound.MaxReferenceDepth, 3, out var fault).ShouldBeFalse();

        fault.Bound.ShouldBe(ResourceBound.MaxReferenceDepth);
        budget.Consumed(ResourceBound.MaxReferenceDepth).ShouldBe(0);
    }

    [Test]
    public void PipelineConsumptionDoesNotDisturbTheInputStream()
    {
        var budget = new GlobalBudget(Limits(l => l with { MaxGenerated = 0 }));

        budget.TryConsume(ResourceBound.MaxGenerated, 1, out _).ShouldBeFalse();

        budget.InputStreamClosed.ShouldBeFalse();
    }

    // ---- Section 16.2 and 22: bound naming and attribution order ----------------------------------

    [Test]
    public void EveryBoundHasASpelling()
    {
        foreach (var bound in Enum.GetValues<ResourceBound>())
        {
            var spelling = ResourceBoundNames.Spelling(bound);

            spelling.ShouldStartWith("--max-");
            spelling.ShouldBe(spelling.ToLowerInvariant());
        }
    }

    [Test]
    public void EveryBoundHasADistinctSpellingAndAConfiguredLimit()
    {
        var bounds = Enum.GetValues<ResourceBound>();
        var budget = new GlobalBudget(ResourceLimits.Defaults);

        bounds.Select(ResourceBoundNames.Spelling).Distinct().Count().ShouldBe(bounds.Length);
        bounds.ShouldAllBe(bound => budget.LimitOf(bound) > 0);
    }

    /// <summary>
    /// Section 22 attribution order: source, then document order, then element order, then bound name.
    /// </summary>
    [Test]
    public void AttributionPrefersTheEarlierSource()
    {
        var early = new BudgetFault(ResourceBound.MaxNodes, 1, SourceOrdinal: 0, DocumentOrder: 99, ElementOrder: 99);
        var late = new BudgetFault(ResourceBound.MaxComments, 1, SourceOrdinal: 1, DocumentOrder: 0, ElementOrder: 0);

        BudgetFaultOrder.Earlier(early, late).ShouldBe(early);
        BudgetFaultOrder.Earlier(late, early).ShouldBe(early);
    }

    [Test]
    public void AttributionPrefersTheEarlierDocumentPositionWithinOneSource()
    {
        var early = new BudgetFault(ResourceBound.MaxNodes, 1, SourceOrdinal: 3, DocumentOrder: 1, ElementOrder: 99);
        var late = new BudgetFault(ResourceBound.MaxComments, 1, SourceOrdinal: 3, DocumentOrder: 2, ElementOrder: 0);

        BudgetFaultOrder.Earlier(late, early).ShouldBe(early);
    }

    [Test]
    public void AttributionPrefersTheEarlierElementWithinOneDocumentPosition()
    {
        var early = new BudgetFault(ResourceBound.MaxNodes, 1, SourceOrdinal: 3, DocumentOrder: 7, ElementOrder: 1);
        var late = new BudgetFault(ResourceBound.MaxComments, 1, SourceOrdinal: 3, DocumentOrder: 7, ElementOrder: 2);

        BudgetFaultOrder.Earlier(late, early).ShouldBe(early);
    }

    [Test]
    public void AttributionBreaksARemainingTieByBoundNameAsUnsignedBytes()
    {
        var faults = Enum.GetValues<ResourceBound>()
            .Select(bound => new BudgetFault(bound, 1, SourceOrdinal: 0))
            .ToList();

        faults.Sort(BudgetFaultOrder.Instance);

        faults.Select(fault => fault.Spelling)
            .ShouldBe(faults.Select(fault => fault.Spelling).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The tie-break must be ordinal. Under a culture-sensitive collation a hyphen can be ignorable,
    /// which would swap these two and make the blamed bound depend on the host locale.
    /// </summary>
    [Test]
    public void TheBoundNameTieBreakIsNotCultureSensitive()
    {
        var commentBytes = new BudgetFault(ResourceBound.MaxCommentBytes, 1, SourceOrdinal: 0);
        var comments = new BudgetFault(ResourceBound.MaxComments, 1, SourceOrdinal: 0);

        BudgetFaultOrder.Earlier(comments, commentBytes).ShouldBe(commentBytes);
    }

    [Test]
    public void APipelineFaultCarriesNoSourceOrdinal()
    {
        var budget = new GlobalBudget(Limits(l => l with { MaxOutputs = 0 }));

        budget.TryConsume(ResourceBound.MaxOutputs, 1, out var fault).ShouldBeFalse();

        fault.SourceOrdinal.ShouldBe(-1);
    }
}
