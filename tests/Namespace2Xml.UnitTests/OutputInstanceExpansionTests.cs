using System.Collections.Immutable;
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
            Records(document), 1, ProfileSource.OfFile("p.txt"), new DiagnosticBuffer())
        .Overlay;

    private ImmutableArray<OutputInstance> Expand(string scheme, string data)
    {
        var read = SchemeReader.Read(Records(scheme), 2, "s.properties", diagnostics);
        var configuration = SchemeCompiler.Compile(read.Entries, diagnostics);
        var outcome = PlanningPhase.ExpandWildcards(configuration, Model(data), diagnostics);

        outcome.Unsupported.ShouldBeNull();

        return outcome.Value;
    }

    private static string[] Selectors(ImmutableArray<OutputInstance> instances) =>
        [.. instances.Select(instance => instance.Selector.ToString())];

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

        var instances = PlanningPhase
            .ExpandWildcards(configuration, Model("a.x=1"), diagnostics)
            .Value;

        instances.ShouldHaveSingleItem().Filename.ShouldBeNull();

        PlanningPhase.ApplyTransformations([], configuration)
            .Unsupported.ShouldNotBeNull().Spec.ShouldBe("\u00A716");
    }
}
