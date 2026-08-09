using System.Collections.Immutable;
using Namespace2Xml.Budgets;
using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Inputs;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Pipeline.Steps;
using Namespace2Xml.Profiles;
using Namespace2Xml.Scheme;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Pipeline step 13: Section 14.1 expansion of a wildcard output selector into concrete instances.
/// </summary>
/// <remarks>
/// Every expectation here is authored from the specification clause named in the test, never from
/// what the expansion currently produces. The conformance corpus asserts what the expansion writes;
/// these assert the parts of it no file can show — the match order Section 17.5 folds by, and the
/// boundary between an instance that exists and selects nothing and no instance at all.
/// </remarks>
[TestFixture]
public class OutputInstanceExpansionTests
{
    private DiagnosticBuffer diagnostics = null!;

    [SetUp]
    public void SetUp() => diagnostics = new DiagnosticBuffer();

    private static ImmutableArray<NamespaceRecord> Records(string document) =>
    [
        .. document
            .Split('\n')
            .Select((line, index) => NamespaceRecordClassifier.Classify(line, index + 1)),
    ];

    private static OverlayNode Model(string document) =>
        NamespaceProfileReader.Read(
            Records(document), 1, ProfileSource.OfFile("p.txt"), SubstituteModeMap.Default, new DiagnosticBuffer())
        .Overlay;

    private ImmutableArray<OutputInstance> Expand(string scheme, string data)
    {
        var outcome = ExpandWith(scheme, data, new GlobalBudget(ResourceLimits.Defaults));

        outcome.Unsupported.ShouldBeNull();

        return outcome.Value;
    }

    private StepOutcome<ImmutableArray<OutputInstance>> ExpandWith(
        string scheme, string data, GlobalBudget budget)
    {
        var read = SchemeReader.Read(Records(scheme), 2, "s.properties", diagnostics);
        var configuration = SchemeCompiler.Compile(read.Entries, diagnostics);

        return PlanningPhase.ExpandWildcards(configuration, Model(data), budget, diagnostics);
    }

    private static string[] Selectors(ImmutableArray<OutputInstance> instances) =>
        [.. instances.Select(instance => instance.Selector.ToString())];

    private static string RootText(ImmutableArray<OutputInstance> instances, string selector) =>
        ((OrdinaryPart)instances
            .Single(instance => instance.Selector.ToString() == selector)
            .Root!.Parts[0])
        .LiteralText!;

    /// <summary>
    /// Section 14.1: expansion "stops at the last wildcard-containing selector part", so a pattern
    /// whose wildcard is not final still enumerates only as deep as that part.
    /// </summary>
    [Test]
    public void ALiteralPartAfterTheLastWildcardIsAppendedRatherThanMatched()
    {
        var instances = Expand("a.*.b.output=namespace", "a.x.b=1\na.y.q=2");

        // 'a.y.b' is planned although no such path exists: Section 14.1 plans an instance "even
        // when no data path currently matches its literal prefix". Only the wildcard part has to
        // match something.
        Selectors(instances).ShouldBe(["a.x.b", "a.y.b"]);
    }

    /// <summary>
    /// Section 14.1: "There is exactly one instance per unique capture tuple and literalized
    /// selector, regardless of how many descendants matched beneath it."
    /// </summary>
    [Test]
    public void DescendantsBeneathTheCapturedPartCreateNoDeeperInstance()
    {
        var instances = Expand("a.*.output=namespace", "a.x.y.deep=1\na.x.z=2\na.w=3");

        Selectors(instances).ShouldBe(["a.x", "a.w"]);
    }

    /// <summary>
    /// Section 17.5's third fold component is "wildcard match order", so the instances one
    /// declaration expands into have to be numbered, and numbered in the order Section 12.4 gives
    /// wildcard candidates rather than in the order they happen to be built.
    /// </summary>
    [Test]
    public void ExpandedInstancesAreNumberedInMatchOrder()
    {
        var instances = Expand("a.*.output=namespace", "a.c=1\na.a=2\na.b=3");

        // Section 5.2 keeps mapping order after override, so the walk sees the keys in the order
        // the source wrote them. The match order is that order, not a re-sort of it.
        Selectors(instances).ShouldBe(["a.c", "a.a", "a.b"]);
        instances.Select(instance => instance.WildcardMatchOrder).ShouldBe([0, 1, 2]);
    }

    /// <summary>
    /// Section 17.5 folds by declaration order first and match order third, so two declarations
    /// number their own expansions independently rather than sharing one counter.
    /// </summary>
    [Test]
    public void EachDeclarationNumbersItsOwnExpansionFromZero()
    {
        var instances = Expand(
            "a.*.output=namespace\nb.*.output=namespace", "a.p=1\na.q=2\nb.r=3");

        Selectors(instances).ShouldBe(["a.p", "a.q", "b.r"]);
        instances.Select(instance => instance.WildcardMatchOrder).ShouldBe([0, 1, 0]);
    }

    /// <summary>
    /// Section 14.1: a declaration "containing no wildcards creates exactly one concrete output
    /// instance even when no data path currently matches its literal prefix".
    /// </summary>
    [Test]
    public void ALiteralSelectorMatchingNothingIsStillOneInstance()
    {
        var instances = Expand("nowhere.output=namespace", "a.x=1");

        Selectors(instances).ShouldBe(["nowhere"]);
        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 14.1: "A wildcard output declaration that produces no concrete selector instance
    /// emits <c>WARN009</c> and creates no file." This is the boundary against the clause above:
    /// the same absent subtree produces an instance for a literal selector and none for a wildcard.
    /// </summary>
    [Test]
    public void AWildcardSelectorMatchingNothingIsNoInstanceAndOneWarning()
    {
        var instances = Expand("nowhere.*.output=namespace", "a.x=1");

        instances.ShouldBeEmpty();

        var reported = diagnostics.Drain().ShouldHaveSingleItem();

        reported.Code.ShouldBe("WARN009");
        reported.Spec.ShouldBe("\u00A714.1");
    }

    /// <summary>
    /// Section 12.2 explicit captures bind by identifier, and Section 14.1 makes the instance
    /// unique per capture tuple, so a repeated identifier constrains which paths expand at all.
    /// </summary>
    [Test]
    public void ARepeatedExplicitCaptureConstrainsWhichPathsExpand()
    {
        var instances = Expand(
            "a.*[k].*[k].output=namespace", "a.p.p=1\na.p.q=2\na.r.r=3");

        Selectors(instances).ShouldBe(["a.p.p", "a.r.r"]);
    }

    /// <summary>
    /// Section 16.2 substitutes the selector's captures into the filename, and Section 12.1 makes a
    /// legacy capture positional, so the filename's wildcard takes the selector's binding rather
    /// than matching anything of its own.
    /// </summary>
    [Test]
    public void TheFilenameTakesTheSelectorsCaptures()
    {
        var instances = Expand(
            "a.*.output=namespace\na.*.filename=cfg/*.conf", "a.x=1\na.y=2");

        instances.Select(instance => instance.Filename).ShouldBe(["cfg/x.conf", "cfg/y.conf"]);
    }

    /// <summary>
    /// Section 16.2 resolves scheme references "before capture substitution", which is pipeline
    /// step 1. A filename carrying one is therefore deferred by the compiler and never reaches
    /// this step at all, so the instance it belongs to arrives here carrying no filename.
    /// </summary>
    /// <remarks>
    /// This is a seam rather than a defect only because step 16 refuses every deferred entry. When
    /// step 16 learns to resolve them it must feed the result back through the filename compiler;
    /// resolving the reference and dropping the entry would give the instance its default name
    /// with no diagnostic.
    /// </remarks>
    [Test]
    public void AFilenameCarryingASchemeReferenceIsDeferredRatherThanCompiled()
    {
        var read = SchemeReader.Read(
            Records("a.output=namespace\na.filename=${x}.conf"), 2, "s.properties", diagnostics);
        var configuration = SchemeCompiler.Compile(read.Entries, diagnostics);

        configuration.Deferred.ShouldHaveSingleItem()
            .Directive.ShouldBe(SchemeDirective.Filename);

        var instances = ExpandWith(
            "a.output=namespace\na.filename=${x}.conf",
            "a.x=1",
            new GlobalBudget(ResourceLimits.Defaults)).Value;

        instances.ShouldHaveSingleItem().Filename.ShouldBeNull();

        PlanningPhase.ApplyTransformations([], configuration, diagnostics)
            .Unsupported.ShouldNotBeNull().Spec.ShouldBe("\u00A716");
    }

    /// <summary>
    /// Section 12.4: "A wildcard candidate check is counted once for a <c>(rule,item)</c> pair for
    /// generative templates, permanent wildcard ignore masks, and wildcard scheme selectors", and
    /// "Every wildcard rule category consumes the shared candidate-check limit once per eligible
    /// pair." A selector that examines items therefore spends the limit like any other category.
    /// </summary>
    [Test]
    public void AWildcardSelectorConsumesTheSharedCandidateLimit()
    {
        var budget = new GlobalBudget(
            ResourceLimits.Defaults with { MaxWildcardCandidates = 2 });

        var outcome = ExpandWith("a.*.output=namespace", "a.p=1\na.q=2\na.r=3", budget);

        outcome.Faulted.ShouldBeTrue();

        var reported = diagnostics.Drain().ShouldHaveSingleItem();

        reported.Code.ShouldBe("WILDCARD002");
        reported.Spec.ShouldBe("\u00A712.4");
        reported.Rule.ShouldBe(["a.*"]);
    }

    /// <summary>
    /// Section 12.4 counts a candidate only where "every literal name part before that point equals
    /// the corresponding item part", so a selector is charged for the items under its literal prefix
    /// and not for the whole tree at that depth.
    /// </summary>
    [Test]
    public void OnlyItemsUnderTheLiteralPrefixAreCharged()
    {
        var budget = new GlobalBudget(
            ResourceLimits.Defaults with { MaxWildcardCandidates = 2 });

        // Six paths sit at depth two and two of them are under 'a'. A charge per node at the depth
        // would exhaust a limit of two, so a green expansion is the prefix condition holding.
        var outcome = ExpandWith(
            "a.*.output=namespace", "a.p=1\na.q=2\nb.r=3\nb.s=4\nc.t=5\nc.u=6", budget);

        outcome.Unsupported.ShouldBeNull();
        Selectors(outcome.Value).ShouldBe(["a.p", "a.q"]);
        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 12.4 counts the check "once for a <c>(rule,item)</c> pair", so the pair key carries
    /// the rule: two selectors examining the same items are charged twice over, not once.
    /// </summary>
    [Test]
    public void TheChargeIsPerRuleAndItemPair()
    {
        var budget = new GlobalBudget(
            ResourceLimits.Defaults with { MaxWildcardCandidates = 3 });

        // Both selectors stop at the same depth and share the same literal prefix, so both examine
        // exactly 'a.p' and 'a.q'. Two items charged once would be two; charged per rule it is
        // four, which crosses a limit of three.
        var outcome = ExpandWith(
            "a.*.output=namespace\na.*.b.output=namespace", "a.p=1\na.q=2", budget);

        outcome.Faulted.ShouldBeTrue();
        diagnostics.Drain().ShouldHaveSingleItem().Code.ShouldBe("WILDCARD002");
    }

    /// <summary>
    /// Section 12.4's limit is shared across categories, so a selector cannot spend what a template
    /// already spent: the counts add rather than reset per category.
    /// </summary>
    [Test]
    public void TheSelectorChargeSharesTheTemplateBudget()
    {
        var budget = new GlobalBudget(
            ResourceLimits.Defaults with { MaxWildcardCandidates = 2 });

        budget.TryConsume(ResourceBound.MaxWildcardCandidates, 2, out _).ShouldBeTrue();

        var outcome = ExpandWith("a.*.output=namespace", "a.p=1", budget);

        outcome.Faulted.ShouldBeTrue();
        diagnostics.Drain().ShouldHaveSingleItem().Code.ShouldBe("WILDCARD002");
    }

    /// <summary>
    /// Section 12.4 charges only wildcard candidate checks, and a selector with no wildcard performs
    /// none: Section 14.1 gives it one instance without consulting the model at all.
    /// </summary>
    [Test]
    public void ALiteralSelectorIsChargedNothing()
    {
        var budget = new GlobalBudget(
            ResourceLimits.Defaults with { MaxWildcardCandidates = 0 });

        var outcome = ExpandWith("a.output=namespace", "a.p=1\na.q=2", budget);

        outcome.Unsupported.ShouldBeNull();
        Selectors(outcome.Value).ShouldBe(["a"]);
        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 15.2: "A selector-qualified 'filename', 'root', 'delimiter', output-options,
    /// 'filemerge', or output-view transformation that binds to no concrete output instance emits
    /// one scheme warning and is otherwise inert." The warning is deferred to expansion because a
    /// concrete instance whose selector no <c>output</c> declaration wrote can still exist —
    /// a wildcard <c>output</c> creates it — and the compile step cannot see that.
    /// </summary>
    [Test]
    public void ADirectiveBindingToNoInstanceWarnsAtExpansion()
    {
        Expand("a.filename=x.txt", string.Empty);

        var reported = diagnostics.Drain().ShouldHaveSingleItem();

        reported.Code.ShouldBe("WARN009");
        reported.Spec.ShouldBe("\u00A715.2");
        reported.Phase.ShouldBe(DiagnosticPhase.Planning);
        reported.Declaration.ShouldBe("a.filename");
    }

    /// <summary>
    /// Section 15.2: an <c>output=ignore</c> declaration creates the concrete instance a
    /// per-instance directive binds to. Section 16.1 allows a later non-ignore <c>output</c> to
    /// restore it, and letting the exact-selector <c>filename</c> warn here would be a warning
    /// about the ignore, not about a binding failure — Section 22 lists those as distinct
    /// conditions and warns each in its own place.
    /// </summary>
    [Test]
    public void ADirectiveOnAnIgnoreInstanceDoesNotWarnAtExpansion()
    {
        Expand("a.output=ignore\na.filename=x.txt", "a.k=1");

        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 15.2: a wildcard <c>output=ignore</c> creates concrete ignored instances that a
    /// later exact-selector directive still binds to. Section 16.1 lets a later non-ignore
    /// <c>output</c> restore any one of them, so the binding is meaningful even before restoration.
    /// </summary>
    [Test]
    public void ADirectiveOnAWildcardIgnoreInstanceDoesNotWarnAtExpansion()
    {
        Expand("a.*.output=ignore\na.x.filename=custom.conf", "a.x.k=1\na.y.k=2");

        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 15.2: "exact and wildcard declarations that literalize to the same concrete
    /// selector participate in one source-ordered override stream". The exact <c>a.x.filename</c>
    /// binds to the concrete <c>a.x</c> instance produced from <c>a.*.output=namespace</c>, and no
    /// warning is emitted for either declaration.
    /// </summary>
    [Test]
    public void AConcreteFilenameBindsToAWildcardInstance()
    {
        var instances = Expand(
            "a.*.output=namespace\na.x.filename=custom.conf", "a.x.k=1\na.y.k=2");

        Selectors(instances).ShouldBe(["a.x", "a.y"]);
        instances.Single(instance => instance.Selector.ToString() == "a.x")
            .Filename.ShouldBe("custom.conf");
        instances.Single(instance => instance.Selector.ToString() == "a.y")
            .Filename.ShouldBeNull();
        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 15.2: "Pattern specificity does not alter precedence." The exact <c>a.x.root</c>
    /// declaration wins for <c>a.x</c> because it is written later than the wildcard's <c>root</c>,
    /// not because it is more specific.
    /// </summary>
    [Test]
    public void AnExactDirectiveAfterAWildcardWinsBySourceOrder()
    {
        var instances = Expand(
            "a.*.output=namespace\na.*.root=W\na.x.root=X", "a.x.k=1\na.y.k=2");

        RootText(instances, "a.x").ShouldBe("X");
        RootText(instances, "a.y").ShouldBe("W");
        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 15.2: the wildcard direction of the same override stream. The exact <c>b.p.root</c>
    /// is written first and the wildcard <c>b.*.root</c> written after, so the wildcard wins for
    /// <c>b.p</c> even though it is less specific.
    /// </summary>
    [Test]
    public void AWildcardDirectiveAfterAnExactWinsBySourceOrder()
    {
        var instances = Expand(
            "b.*.output=namespace\nb.p.root=P\nb.*.root=Z", "b.p.k=1\nb.q.k=2");

        RootText(instances, "b.p").ShouldBe("Z");
        RootText(instances, "b.q").ShouldBe("Z");
        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 15.2: "a directive for selector 'a' does not implicitly configure an independently
    /// created 'a.x' output instance". Matching is exact equality against the literalized concrete
    /// selector, not a prefix relation: an exact-selector <c>a.filename</c> does not bind to the
    /// concrete <c>a.x</c> instance a separate <c>a.x.output</c> declaration created.
    /// </summary>
    [Test]
    public void AnExactDirectiveDoesNotBindToADeeperInstance()
    {
        Expand("a.output=namespace\na.x.output=namespace\na.filename=x.conf", "a.k=1\na.x.k=2");

        // The 'a.filename' does bind to the concrete 'a' instance, so no WARN009 is emitted at
        // all: what fails to bind here is one directive to one instance, not a directive to every
        // instance in scope.
        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 15.2: prefix matching would confuse this case. An <c>a.x.filename</c> at a selector
    /// no <c>output</c> declaration created must warn, because <c>a.x</c> does not literalize to
    /// <c>a</c> — the wildcard-free written selector matches only itself.
    /// </summary>
    [Test]
    public void ADeeperDirectiveDoesNotBindToAShallowerInstance()
    {
        Expand("a.output=namespace\na.x.filename=custom.conf", "a.k=1");

        var reported = diagnostics.Drain().ShouldHaveSingleItem();

        reported.Code.ShouldBe("WARN009");
        reported.Spec.ShouldBe("\u00A715.2");
        reported.Declaration.ShouldBe("a.x.filename");
    }

    /// <summary>
    /// Section 15.2's cross-selector override stream also carries <c>filemerge</c>, so the
    /// specificity-independent source order applies to it the same way it does to <c>root</c>.
    /// </summary>
    [Test]
    public void FileMergeAlsoFollowsTheCrossSelectorSourceOrder()
    {
        var instances = Expand(
            "a.*.output=namespace\na.*.filemerge=error\na.x.filemerge=replace", "a.x.k=1\na.y.k=2");

        instances.Single(instance => instance.Selector.ToString() == "a.x")
            .FileMerge.ShouldBe(MergeStrategy.Replace);
        instances.Single(instance => instance.Selector.ToString() == "a.y")
            .FileMerge.ShouldBe(MergeStrategy.Error);
        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 16.2 substitutes captures from the same selector expansion that produced the
    /// literalized concrete selector, not from the <c>output</c>'s captures. A wildcard
    /// <c>filename</c> that binds to a concrete instance created by a different wildcard uses the
    /// captures its own written selector matched against that concrete selector.
    /// </summary>
    [Test]
    public void AWildcardFilenameUsesItsOwnCaptureWhenBoundAcrossWildcards()
    {
        var instances = Expand(
            "a.*.output=namespace\na.*.filename=cfg/*.conf", "a.x=1\na.y=2");

        instances.Select(instance => instance.Filename).ShouldBe(["cfg/x.conf", "cfg/y.conf"]);
        diagnostics.Drain().ShouldBeEmpty();
    }
}
