using System.Collections.Immutable;
using Namespace2Xml.Budgets;
using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Inputs;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 8.6 permanent exclusion masks, as pipeline step 8 and step 10 apply them.
/// </summary>
/// <remarks>
/// Every expectation here is authored from Section 8.6, never from what the pruner produces.
/// </remarks>
[TestFixture]
public class ExclusionMaskTests
{
    private static OrdinaryPart Ordinary(string text) => new([new LiteralToken(text)]);

    private static ProfileContribution Read(string document, int ordinal) =>
        NamespaceProfileReader.Read(
            [
                .. document
                    .Split('\n')
                    .Select((line, index) => NamespaceRecordClassifier.Classify(line, index + 1)),
            ],
            ordinal,
            ProfileSource.OfFile($"p{ordinal}.txt"),
            new DiagnosticBuffer());

    /// <summary>Reads one document and applies the masks it declares to its own contributions.</summary>
    private static OverlayNode Masked(string document)
    {
        var read = Read(document, 1);

        return ExclusionMask.Of(read.Masks.Select(mask => mask.Pattern)).Apply(read.Overlay);
    }

    /// <summary>Reads several documents and applies the run-wide union to the first one.</summary>
    private static OverlayNode MaskedAcrossSources(params string[] documents)
    {
        var read = documents.Select(Read).ToImmutableArray();

        return ExclusionMask
            .Of(read.SelectMany(contribution => contribution.Masks).Select(mask => mask.Pattern))
            .Apply(read[0].Overlay);
    }

    private static OverlayNode? Find(OverlayNode node, params string[] path)
    {
        var current = node;

        foreach (var step in path)
        {
            if (!current.Children.TryGetValue(Ordinary(step), out var child))
            {
                return null;
            }

            current = child;
        }

        return current;
    }

    private static string Value(OverlayNode? node) =>
        node.ShouldNotBeNull().Payload.ShouldNotBeNull().ToCanonicalText();

    // Section 8.6: "a later concrete or generated contribution cannot recreate the path".

    /// <summary>
    /// Section 8.6's own worked example: "a.x=1 / !a.* / a.x=2 produces no a.x value."
    /// </summary>
    [Test]
    public void TheSpecifiedExampleProducesNoValueAtTheMaskedPath() =>
        Find(Masked("a.x=1\n!a.*\na.x=2\n"), "a", "x").ShouldBeNull();

    /// <summary>
    /// Section 8.6 suppresses a contribution "regardless of whether it appears before or after the
    /// ignore entry", so a mask written first still reaches an entry written later.
    /// </summary>
    [Test]
    public void AMaskSuppressesAnEntryWrittenAfterIt() =>
        Find(Masked("!a.x\na.x=1\n"), "a", "x").ShouldBeNull();

    /// <summary>The same clause read the other way: a mask written last reaches an earlier entry.</summary>
    [Test]
    public void AMaskSuppressesAnEntryWrittenBeforeIt() =>
        Find(Masked("a.x=1\n!a.x\n"), "a", "x").ShouldBeNull();

    /// <summary>
    /// Section 8.6 leaves unmatched siblings alone: only "every concrete or generated contribution
    /// matching the pattern is suppressed".
    /// </summary>
    [Test]
    public void AnUnmatchedSiblingSurvives() =>
        Value(Find(Masked("a.x=1\na.y=2\n!a.x\n"), "a", "y")).ShouldBe("2");

    // Section 8.6: "suppressed paths and descendants never become wildcard candidates, reference
    // targets, output-selector matches, or rendered content".

    /// <summary>A mask on a path takes the whole subtree beneath it.</summary>
    [Test]
    public void AMaskTakesTheDescendantsOfTheMatchedPath()
    {
        var masked = Masked("a.b.c=1\na.b.d=2\n!a.b\n");

        Find(masked, "a", "b").ShouldBeNull();
    }

    /// <summary>
    /// A mask reaches a descendant many levels below the matched depth, because the exclusion is
    /// of a subtree rather than of one path.
    /// </summary>
    [Test]
    public void AMaskTakesADeeplyNestedDescendant() =>
        Find(Masked("a.b.c.d.e=1\n!a.b\n"), "a", "b").ShouldBeNull();

    /// <summary>
    /// A pattern matches a path prefix, so a two-component pattern cannot suppress a one-component
    /// path: <c>!a.*</c> empties <c>a</c> without removing the value at <c>a</c> itself.
    /// </summary>
    [Test]
    public void AWildcardChildMaskDoesNotSuppressTheParentItself()
    {
        var masked = Masked("a=1\na.x=2\n!a.*\n");

        Value(Find(masked, "a")).ShouldBe("1");
        Find(masked, "a", "x").ShouldBeNull();
    }

    // Section 8.6: "Ignore patterns use the wildcard rules in Section 12."

    /// <summary>A wildcard in a mask matches by the Section 12 rules, not by whole components.</summary>
    [Test]
    public void AMaskWildcardMatchesWithinAComponent()
    {
        var masked = Masked("a.bx=1\na.xb=2\n!a.*x\n");

        Find(masked, "a", "bx").ShouldBeNull();
        Value(Find(masked, "a", "xb")).ShouldBe("2");
    }

    /// <summary>
    /// Section 12.2 makes an inconsistent repeated capture a nonmatch, so a mask that repeats a
    /// named capture suppresses only the paths where both occurrences carry the same text.
    /// </summary>
    [Test]
    public void ARepeatedCaptureSuppressesOnlyConsistentPaths()
    {
        var masked = Masked("a.p.p=1\na.p.q=2\n!a.*[n].*[n]\n");

        Find(masked, "a", "p", "p").ShouldBeNull();
        Value(Find(masked, "a", "p", "q")).ShouldBe("2");
    }

    // Section 8.6: "multiple ignore masks form a union".

    /// <summary>Two masks in one source suppress both of their matches.</summary>
    [Test]
    public void MultipleMasksFormAUnion()
    {
        var masked = Masked("a.x=1\na.y=2\na.z=3\n!a.x\n!a.y\n");

        Find(masked, "a", "x").ShouldBeNull();
        Find(masked, "a", "y").ShouldBeNull();
        Value(Find(masked, "a", "z")).ShouldBe("3");
    }

    /// <summary>
    /// Section 8.6 calls the mask "run-wide", so one declared in a later source suppresses a path
    /// contributed by an earlier one. This is the exception to later-source precedence, which
    /// otherwise would let the earlier contribution stand because nothing later overrode it.
    /// </summary>
    [Test]
    public void AMaskInALaterSourceSuppressesAnEarlierSourcesPath() =>
        Find(MaskedAcrossSources("a.x=1\n", "!a.x\n"), "a", "x").ShouldBeNull();

    // Section 8.6: "comments bound to suppressed paths are suppressed with them".

    /// <summary>A comment bound to a masked entry goes with the entry.</summary>
    [Test]
    public void ACommentBoundToASuppressedPathIsSuppressed()
    {
        var masked = Masked("#note\na.x=1\n!a.x\n");

        Find(masked, "a", "x").ShouldBeNull();
        masked.Comments.ShouldBeEmpty();
        Find(masked, "a").ShouldNotBeNull().Comments.ShouldBeEmpty();
    }

    // Section 8.6: "masked contributions still reserve any canonical ordering value for high-water
    // stability".

    /// <summary>
    /// Removing the highest numeric child must not lower the path's Section 5.4 high-water mark:
    /// an ignore entry must not renumber the items around it, and a later automatic allocation must
    /// not reuse the masked value.
    /// </summary>
    [Test]
    public void AMaskedNumericChildKeepsItsHighWaterReservation() =>
        Find(Masked("a.0=x\na.7=y\n!a.7\n"), "a")
            .ShouldNotBeNull()
            .SequenceHighWater.ShouldBe(7);

    /// <summary>
    /// The reservation survives even when every numeric child is masked, so the mark cannot be
    /// recovered from anything the pruned tree still holds.
    /// </summary>
    [Test]
    public void AFullyMaskedNumericMappingKeepsItsHighWaterReservation()
    {
        var masked = Find(Masked("a.4=x\n!a.*\n"), "a").ShouldNotBeNull();

        masked.Children.ShouldBeEmpty();
        masked.SequenceHighWater.ShouldBe(4);
    }

    // Section 15.1 makes a sequence item and the mapping child spelled with its ordering value
    // "one structural overlay node", so a mask reaches a native array item under that spelling.

    /// <summary>Reads a JSON document into an overlay and applies one mask pattern to it.</summary>
    private static OverlayNode MaskedJson(string document, string pattern)
    {
        var diagnostics = new DiagnosticBuffer();
        var limits = ResourceLimits.Defaults;

        var root = JsonInputReader.Read(
            document,
            limits,
            new SourceBudget(limits, 0),
            ProfileSource.OfFile("d.json"),
            DiagnosticPhase.Input,
            diagnostics,
            StableOrderingKey.FromSource(1, 1));

        var contribution = StructuredProfileReader.Read(
            root.ShouldNotBeNull(),
            sourceOrdinal: 1,
            ProfileSource.OfFile("d.json"),
            diagnostics,
            out var unsupported);

        unsupported.ShouldBeNull();
        diagnostics.Drain().ShouldBeEmpty();

        var mask = Read($"!{pattern}\n", 2).Masks.Select(entry => entry.Pattern);

        return ExclusionMask.Of(mask).Apply(contribution.Overlay);
    }

    /// <summary>A mask spelled with an ordering value removes the native sequence item at it.</summary>
    [Test]
    public void AMaskRemovesANativeSequenceItem()
    {
        var masked = Find(MaskedJson("""{"a":["x","y","z"]}""", "a.1"), "a").ShouldNotBeNull();

        masked.Sequence.Keys.Order().ShouldBe([0, 2]);
    }

    /// <summary>
    /// Removing a native item must not lower the path's Section 5.4 high-water mark, so masking
    /// the last item still leaves the mark where allocation left it.
    /// </summary>
    [Test]
    public void AMaskedNativeSequenceItemKeepsItsHighWaterReservation()
    {
        var masked = Find(MaskedJson("""{"a":["x","y","z"]}""", "a.2"), "a").ShouldNotBeNull();

        masked.Sequence.Keys.Order().ShouldBe([0, 1]);
        masked.SequenceHighWater.ShouldBe(2);
    }

    /// <summary>A mask below an item reaches into the item rather than removing it.</summary>
    [Test]
    public void AMaskInsideANativeSequenceItemLeavesTheItemInPlace()
    {
        var masked = Find(
            MaskedJson("""{"a":[{"k":"x","j":"y"}]}""", "a.0.k"), "a").ShouldNotBeNull();

        masked.Sequence.Keys.Order().ShouldBe([0]);
        masked.Sequence[0].Node.Children.Keys.Select(name => ((OrdinaryPart)name).LiteralText)
            .ShouldBe(["j"]);
    }

    // Section 8.6 is a predicate over absolute paths, which Section 15.1 step 10 also consults.

    /// <summary>A path with no mask over it is not suppressed.</summary>
    [Test]
    public void AnUnmatchedPathIsNotSuppressed() =>
        ExclusionMask.Of([new QualifiedName([Ordinary("a"), Ordinary("x")])])
            .Suppresses([Ordinary("a"), Ordinary("y")])
            .ShouldBeFalse();

    /// <summary>A descendant of a masked path is suppressed in its own right.</summary>
    [Test]
    public void ADescendantOfAMaskedPathIsSuppressed() =>
        ExclusionMask.Of([new QualifiedName([Ordinary("a")])])
            .Suppresses([Ordinary("a"), Ordinary("b"), Ordinary("c")])
            .ShouldBeTrue();

    /// <summary>An empty mask suppresses nothing and leaves the overlay identical.</summary>
    [Test]
    public void AnEmptyMaskLeavesTheContributionUntouched()
    {
        var read = Read("a.x=1\n", 1);

        ExclusionMask.None.Apply(read.Overlay).ShouldBeSameAs(read.Overlay);
    }
}
