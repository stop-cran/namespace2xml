using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Output;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Pipeline.Steps;
using Namespace2Xml.Profiles;
using Namespace2Xml.Scheme;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 15.1 step 18, the Section 17.5 destination collision fold.
/// </summary>
/// <remarks>
/// These pin the parts of Section 17.5 that no published file can show. Dense sequence rendering
/// hides destination ordering values, and Section 21.3 publication order is only visible in the
/// order files reach a sink, so the publication key a fold carries forward has to be asserted here.
/// Every expectation is authored from the specification clause the test names.
/// </remarks>
[TestFixture]
public class DestinationFoldTests
{
    private static QualifiedName Name(string dotted) =>
        new([.. dotted.Split('.').Select(Ordinary)]);

    private static OrdinaryPart Ordinary(string text) => new([new LiteralToken(text)]);

    private static DestinationPath Path(string written)
    {
        var lexed = ValueLexer.Lex(written, ValueSyntax.Profile(WildcardSyntax.Unnamed));

        lexed.Value.ShouldNotBeNull();

        DestinationPathComposer.TryCompose(
            lexed.Value,
            new WildcardCaptures([], ImmutableDictionary<string, string>.Empty),
            out var path,
            out _).ShouldBeTrue();

        return path.ShouldNotBeNull();
    }

    private static OutputInstance Instance(string selector, MergeStrategy fileMerge, long declarationOrder)
    {
        var site = new DeclarationSite($"{selector}.output", "scheme.txt", 1);

        return new OutputInstance(
            new SelectorKey(Name(selector)),
            [OutputFormat.Namespace],
            declarationOrder,
            FilenameTemplate: null,
            Root: null,
            Delimiter: null,
            IniOutput.Default,
            fileMerge,
            WildcardCaptures.Empty,
            WildcardMatchOrder: 0,
            FilenameDeclaration: null,
            FileMergeDeclaration: fileMerge == MergeStrategy.Deep
                ? null
                : new DeclarationSite($"{selector}.filemerge", "scheme.txt", 2),
            IniOptionsDeclaration: null,
            site);
    }

    private static DestinationContribution Contribution(
        string selector,
        string destination,
        string leaf,
        string value,
        long declarationOrder,
        MergeStrategy fileMerge = MergeStrategy.Deep,
        OutputFormat format = OutputFormat.Namespace)
    {
        var instance = Instance(selector, fileMerge, declarationOrder);
        var node = OverlayNode
            .Intermediate(StableOrderingKey.First)
            .WithChild(Ordinary(leaf), OverlayNode.OfPayload(ScalarPayload.OfString(value), StableOrderingKey.First));

        return new DestinationContribution(
            new OutputView(instance, format, FormatOrdinal: 0, node, Root: []),
            Path(destination),
            new FoldKey(declarationOrder, 0, 0, selector));
    }

    private static DestinationContribution Sequenced(
        string selector,
        string destination,
        long highWater,
        long declarationOrder,
        OutputFormat format = OutputFormat.Namespace)
    {
        var list = OverlayNode
            .Intermediate(StableOrderingKey.First)
            .WithSequenceItem(
                0,
                SequenceItem.Native(
                    OverlayNode.OfPayload(ScalarPayload.OfString(selector), StableOrderingKey.First)))
            .WithReservedOrderingValue(highWater);

        return new DestinationContribution(
            new OutputView(
                Instance(selector, MergeStrategy.Deep, declarationOrder),
                format,
                FormatOrdinal: 0,
                OverlayNode.Intermediate(StableOrderingKey.First).WithChild(Ordinary("list"), list),
                Root: []),
            Path(destination),
            new FoldKey(declarationOrder, 0, 0, selector));
    }

    private static ImmutableArray<DestinationContribution> Fold(
        DiagnosticBuffer diagnostics,
        params DestinationContribution[] contributions)
    {
        var outcome = PlanningPhase.FoldDestinationCollisions([.. contributions], diagnostics);

        outcome.Faulted.ShouldBeFalse();

        return outcome.Value;
    }

    /// <summary>
    /// Section 17.5: "Same-format <c>replace</c> preserves the earliest prior publication key even
    /// when no prior sequence high-water state exists".
    /// </summary>
    [Test]
    public void ASameFormatFoldKeepsTheEarliestPublicationKey()
    {
        var diagnostics = new DiagnosticBuffer();

        var folded = Fold(
            diagnostics,
            Contribution("a", "out.conf", "k", "1", declarationOrder: 0, MergeStrategy.Replace),
            Contribution("b", "out.conf", "k", "2", declarationOrder: 7, MergeStrategy.Replace));

        folded.Length.ShouldBe(1);
        folded[0].Key.DeclarationOrder.ShouldBe(0);
    }

    /// <summary>
    /// The same rule under the Section 16.11 default, so that the preservation is a property of the
    /// same-format fold and not of one strategy.
    /// </summary>
    [Test]
    public void ADeepFoldAlsoKeepsTheEarliestPublicationKey()
    {
        var diagnostics = new DiagnosticBuffer();

        var folded = Fold(
            diagnostics,
            Contribution("a", "out.conf", "k", "1", declarationOrder: 2),
            Contribution("b", "out.conf", "m", "2", declarationOrder: 9));

        folded.Length.ShouldBe(1);
        folded[0].Key.DeclarationOrder.ShouldBe(2);
    }

    /// <summary>
    /// Section 17.5: "only cross-format replacement resets it", the counterpart to the two tests
    /// above.
    /// </summary>
    [Test]
    public void ACrossFormatReplacementResetsThePublicationKey()
    {
        var diagnostics = new DiagnosticBuffer();

        var folded = Fold(
            diagnostics,
            Contribution("a", "out.conf", "k", "1", declarationOrder: 0),
            Contribution("b", "out.conf", "m", "2", declarationOrder: 7, format: OutputFormat.Ini));

        folded.Length.ShouldBe(1);
        folded[0].Key.DeclarationOrder.ShouldBe(7);
    }

    /// <summary>
    /// Section 16.11: "<c>error</c>: any second contribution to that destination is a blocking
    /// collision error", and the fold reports it rather than merging.
    /// </summary>
    [Test]
    public void FileMergeErrorRejectsTheSecondContribution()
    {
        var diagnostics = new DiagnosticBuffer();

        PlanningPhase.FoldDestinationCollisions(
            [
                Contribution("a", "out.conf", "k", "1", declarationOrder: 0, MergeStrategy.Error),
                Contribution("b", "out.conf", "m", "2", declarationOrder: 1, MergeStrategy.Error),
            ],
            diagnostics).Faulted.ShouldBeTrue();

        diagnostics.HasBlockingError.ShouldBeTrue();
    }

    /// <summary>
    /// Section 17.5: "A cross-format replacement discards the complete accumulated plan for that
    /// destination, including document data, comments, renderer state, sequence provenance, and
    /// every destination high-water mark."
    /// </summary>
    /// <remarks>
    /// No published file can show this. Section 5.4 makes namespace and INI projection "display
    /// fresh dense indices", and the structured formats have no indices at all, so a destination
    /// high-water mark that survived a cross-format replacement would change nothing about any
    /// output until a later contribution allocated against it — and by then the run has moved on.
    /// The mark is asserted directly here for that reason.
    /// <para>
    /// The wrong implementation this guards against is a real one: line 2137 of the specification
    /// says "a destination accumulator absorbs the incoming high-water mark for a path", and reading
    /// that as applying to every fold — rather than only to the same-format fold two paragraphs
    /// above it — produces a cross-format branch that carries the accumulated marks forward. Written
    /// out as a mutation, that implementation fails this test and passes every other test in the
    /// repository.
    /// </para>
    /// </remarks>
    [Test]
    public void ACrossFormatReplacementDiscardsTheDestinationHighWaterMark()
    {
        var diagnostics = new DiagnosticBuffer();

        var folded = Fold(
            diagnostics,
            Sequenced("a", "out.conf", highWater: 5, declarationOrder: 0),
            Sequenced("b", "out.conf", highWater: 0, declarationOrder: 1, format: OutputFormat.Ini));

        folded.Length.ShouldBe(1);
        folded[0].View.View.Children[Ordinary("list")].SequenceHighWater.ShouldBe(0);
    }

    /// <summary>
    /// The counterpart: a same-format fold absorbs the incoming contribution rather than replacing
    /// the accumulator, so Section 17.5's "A destination accumulator absorbs the incoming high-water
    /// mark for a path" leaves the accumulated mark standing.
    /// </summary>
    [Test]
    public void ASameFormatFoldKeepsTheDestinationHighWaterMark()
    {
        var diagnostics = new DiagnosticBuffer();

        var folded = Fold(
            diagnostics,
            Sequenced("a", "out.conf", highWater: 5, declarationOrder: 0),
            Sequenced("b", "out.conf", highWater: 0, declarationOrder: 1));

        folded.Length.ShouldBe(1);
        folded[0].View.View.Children[Ordinary("list")].SequenceHighWater.ShouldBe(6);
    }

    /// <summary>
    /// Two destinations are carried out of the fold independently, so a collision at one does not
    /// disturb the publication key of the other.
    /// </summary>
    [Test]
    public void DistinctDestinationsKeepTheirOwnPublicationKeys()
    {
        var diagnostics = new DiagnosticBuffer();

        var folded = Fold(
            diagnostics,
            Contribution("a", "one.conf", "k", "1", declarationOrder: 0),
            Contribution("b", "two.conf", "k", "2", declarationOrder: 1),
            Contribution("c", "one.conf", "m", "3", declarationOrder: 5));

        folded.Length.ShouldBe(2);
        folded.Select(contribution => contribution.Key.DeclarationOrder).ShouldBe([0L, 1L]);
    }
}
