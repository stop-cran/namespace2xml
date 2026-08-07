using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Pipeline;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 8.7 sequence inference: which mappings are classified as sequence-inferable, and what
/// the projection at pipeline step 11 does to the ones that are.
/// </summary>
/// <remarks>
/// Every expectation here is read from Section 8.7 and Section 5.4. The three worked examples the
/// section states in full are asserted verbatim, because a specification that shows its answer is
/// the least ambiguous fixture available.
/// </remarks>
[TestFixture]
public sealed class SequenceInferenceTests
{
    // ---- Classification ------------------------------------------------------------------------

    /// <summary>
    /// "All its surviving concrete child names are canonical nonnegative decimal ordering values."
    /// </summary>
    [Test]
    public void AMappingWhoseChildrenAreAllCanonicalIndicesBecomesASequence()
    {
        Render("app.0=x\napp.1=y\n").ShouldBe("0=x\n1=y\n");
    }

    /// <summary>"Gaps and nonzero bases are allowed" and "missing values do not create null placeholders".</summary>
    [Test]
    public void AGapCreatesNoPlaceholder()
    {
        // Ordering values 2 and 7 survive as two items; Section 5.4 renders them at dense display
        // positions 0 and 1. Five null placeholders between them would be the alternative reading,
        // and the clause rejects it.
        Render("app.2=x\napp.7=y\n").ShouldBe("0=x\n1=y\n");
    }

    /// <summary>"Leading-zero spellings such as <c>00</c> and <c>01</c> ... prevent sequence interpretation."</summary>
    [TestCase("00", TestName = "ADoubleZeroSpellingPreventsInference")]
    [TestCase("01", TestName = "ALeadingZeroSpellingPreventsInference")]
    public void ALeadingZeroSpellingIsAnOrdinaryKey(string spelling)
    {
        // The clause is about the whole mapping, not just the offending key: one such spelling
        // makes every sibling an ordinary key too, so `0` keeps its own name rather than becoming
        // a dense index.
        Render($"app.0=x\napp.{spelling}=y\n").ShouldBe($"0=x\n{spelling}=y\n");
    }

    /// <summary>
    /// "A canonically spelled decimal above the supported maximum is an ordinary mapping key and
    /// prevents sequence interpretation."
    /// </summary>
    [Test]
    public void ASpellingAboveTheSupportedMaximumIsAnOrdinaryKey()
    {
        // 9223372036854775808 is long.MaxValue + 1: canonically spelled, and one past the limit
        // the clause names.
        Render("app.0=x\napp.9223372036854775808=y\n")
            .ShouldBe("0=x\n9223372036854775808=y\n");
    }

    /// <summary>The supported maximum itself is inside the range, so it does infer.</summary>
    [Test]
    public void TheSupportedMaximumItselfIsAnOrderingValue()
    {
        Render("app.9223372036854775807=y\n").ShouldBe("0=y\n");
    }

    /// <summary>"A surviving empty mapping remains a mapping."</summary>
    [Test]
    public void AnEmptyMappingRemainsAMapping()
    {
        // Vacuously, an empty mapping's children are "all canonical ordering values". The clause
        // exists to say that this reading is wrong. A JSON object spells the presence explicitly,
        // which a namespace profile cannot do; Section 19.4 then emits the empty namespace file
        // because no scalar survives under it.
        var (result, sink) = Transformation(
            ("app.json", "{\"app\": {\"sub\": {}, \"k\": \"v\"}}"),
            ("scheme.txt", "app.output=namespace\n"));

        result.ExitCode.ShouldBe(0);
        sink.Written["app.properties"].ShouldBe("k=v\n");
    }

    /// <summary>
    /// "A surviving empty mapping remains a mapping" — so it still competes with a sequence at the
    /// same path, and Section 16.4 reports the conflict a flat destination has to resolve.
    /// </summary>
    /// <remarks>
    /// An empty mapping vacuously satisfies "all its surviving child names are ordering values".
    /// Inferring it would absorb its mapping shape-mark into the sequence and make this warning
    /// disappear, which is precisely the reading the nonemptiness clause exists to forbid.
    /// </remarks>
    [Test]
    public void AnEmptyMappingStillCompetesWithASequence()
    {
        var (result, _) = Transformation(
            ("empty.json", "{\"a\": {}}"),
            ("seq.yaml", "a: [z]\n"),
            ("scheme.txt", "a.output=namespace\n"));

        result.ExitCode.ShouldBe(0);
        Codes(result).ShouldContain("TYPE002");
    }

    /// <summary>"All its surviving concrete child names" — one ordinary sibling is enough to decline.</summary>
    [Test]
    public void ANonNumericSiblingPreventsInference()
    {
        Render("app.0=x\napp.name=y\n").ShouldBe("0=x\nname=y\n");
    }

    /// <summary>Inference is recursive: a nested numeric mapping is classified on its own children.</summary>
    [Test]
    public void ANestedNumericMappingInfersIndependently()
    {
        // `app.list` infers; `app` does not, because `list` is not an ordering value. A projection
        // that only considered the root, or stopped at the first inferable node, would miss this.
        Render("app.list.0=x\napp.list.1=y\n").ShouldBe("list.0=x\nlist.1=y\n");
    }

    // ---- The three worked examples ---------------------------------------------------------

    /// <summary>
    /// Section 8.7's first worked example: <c>a.0.x=one; a.1.x=two</c> then <c>a.1.x=three</c>
    /// "produces a two-item sequence: <c>one</c>, <c>three</c>".
    /// </summary>
    [Test]
    public void ExplicitIndicesPatchRatherThanConcatenate()
    {
        RenderSources(
            "a",
            ("first.txt", "a.0.x=one\na.1.x=two\n"),
            ("second.txt", "a.1.x=three\n"))
            .ShouldBe("0.x=one\n1.x=three\n");
    }

    /// <summary>
    /// Section 8.7's second worked example: native YAML arrays <c>[one, two]</c> then
    /// <c>[three]</c> "produce <c>one</c>, <c>two</c>, <c>three</c>".
    /// </summary>
    [Test]
    public void NativeArraysConcatenateInSourceOrder()
    {
        RenderSources(
            "a",
            ("first.yaml", "a: [one, two]\n"),
            ("second.yaml", "a: [three]\n"))
            .ShouldBe("0=one\n1=two\n2=three\n");
    }

    /// <summary>
    /// Section 8.7's third worked example: <c>a.0=x; a.1=y</c> followed by YAML <c>a: [z]</c>
    /// "produces ordering values <c>0=x</c>, <c>1=y</c>, and <c>2=z</c>", because "the native item
    /// is implicit and therefore receives a fresh value above the high-water mark".
    /// </summary>
    [Test]
    public void AnImplicitItemAfterExplicitIndicesTakesAFreshValue()
    {
        RenderSources(
            "a",
            ("first.txt", "a.0=x\na.1=y\n"),
            ("second.yaml", "a: [z]\n"))
            .ShouldBe("0=x\n1=y\n2=z\n");
    }

    /// <summary>
    /// The converse of the third example: an explicit index lands *on* an existing native item
    /// rather than beside it, because it supplies its own ordering value.
    /// </summary>
    [Test]
    public void AnExplicitIndexPatchesAnEarlierNativeItem()
    {
        // The YAML item is allocated ordering value 0, so `a.0=patched` addresses it. Two items
        // would mean the explicit contribution had been treated as implicit.
        RenderSources(
            "a",
            ("first.yaml", "a: [original]\n"),
            ("second.txt", "a.0=patched\n"))
            .ShouldBe("0=patched\n");
    }

    // ---- Interaction with the surrounding steps ----------------------------------------------

    /// <summary>
    /// "Numeric-map inference occurs once, after wildcard generation and permanent ignores reach
    /// their fixed point ... so templates and ignore masks can match them."
    /// </summary>
    [Test]
    public void AMaskMatchesANumericKeyBeforeInference()
    {
        // `!app.1` is written against the mapping key, which is the address available to a mask.
        // Requiring the mask to know it was addressing a sequence item would invert the ordering
        // the clause fixes.
        Render("app.0=x\napp.1=y\napp.2=z\n!app.1\n").ShouldBe("0=x\n1=z\n");
    }

    /// <summary>
    /// A mask that removes every numeric child leaves an empty mapping, which "remains a mapping"
    /// rather than becoming an empty sequence.
    /// </summary>
    [Test]
    public void MaskingEveryIndexLeavesAMapping()
    {
        var (result, sink) = Transformation(
            ("input.txt", "app.list.0=x\napp.k=v\n!app.list.0\n"),
            ("scheme.txt", "app.output=namespace\n"));

        result.ExitCode.ShouldBe(0);
        sink.Written["app.properties"].ShouldBe("k=v\n");
    }

    /// <summary>
    /// Section 15.1: "inference replaces that contribution's mapping projection". The replaced
    /// facet must stop claiming mapping shape, or a flat destination sees a node offering both
    /// containers and warns about a conflict between a sequence and the mapping it replaced.
    /// </summary>
    [Test]
    public void InferenceDoesNotLeaveTheReplacedMappingFacetBehind()
    {
        var (result, _) = Transformation(
            ("first.txt", "a.0=x\n"),
            ("second.yaml", "a: [z]\n"),
            ("scheme.txt", "a.output=namespace\n"));

        result.ExitCode.ShouldBe(0);
        Codes(result).ShouldNotContain("TYPE002");
    }

    /// <summary>
    /// The same shape conflict is still reported when it is real: a non-numeric mapping cannot
    /// infer, so the mapping and the native sequence genuinely compete.
    /// </summary>
    [Test]
    public void AGenuineContainerConflictIsStillReported()
    {
        var (result, _) = Transformation(
            ("first.txt", "a.name=x\n"),
            ("second.yaml", "a: [z]\n"),
            ("scheme.txt", "a.output=namespace\n"));

        Codes(result).ShouldContain("TYPE002");
    }

    /// <summary>
    /// Section 8.7: "When multiple sources contribute native implicit sequences at one path and no
    /// explicit <c>merge</c> directive applies, emit one compatibility warning explaining that
    /// implicit items concatenate while explicit ordering values patch."
    /// </summary>
    [Test]
    public void TwoNativeArraysAtOnePathWarnAboutConcatenation()
    {
        var (result, _) = Transformation(
            ("first.yaml", "a: [one, two]\n"),
            ("second.yaml", "a: [three]\n"),
            ("scheme.txt", "a.output=namespace\n"));

        result.ExitCode.ShouldBe(0);
        Codes(result).Count(code => code == "WARN004").ShouldBe(1, "cardinality is once per sequence path");
    }

    /// <summary>"...and no explicit <c>merge</c> directive applies."</summary>
    /// <remarks>
    /// The directive says which reading was meant, so the ambiguity the warning reports is gone.
    /// <c>deep</c> is the default strategy, which is the point: the clause turns on whether a
    /// directive was written, not on which strategy it chose.
    /// </remarks>
    [Test]
    public void AnExplicitMergeDirectiveSilencesTheCompatibilityWarning()
    {
        var (result, _) = Transformation(
            ("first.yaml", "a: [one, two]\n"),
            ("second.yaml", "a: [three]\n"),
            ("scheme.txt", "a.output=namespace\na.merge=deep\n"));

        result.ExitCode.ShouldBe(0);
        Codes(result).ShouldNotContain("WARN004");
    }

    /// <summary>
    /// "Multiple sources contribute <em>native implicit</em> sequences" — explicit indices patch
    /// rather than concatenating, so the surprise the warning names does not arise.
    /// </summary>
    [Test]
    public void ExplicitIndicesMeetingNativeItemsDoNotWarn()
    {
        var (result, _) = Transformation(
            ("first.txt", "a.0=x\na.1=y\n"),
            ("second.yaml", "a: [z]\n"),
            ("scheme.txt", "a.output=namespace\n"));

        result.ExitCode.ShouldBe(0);
        Codes(result).ShouldNotContain("WARN004");
    }

    /// <summary>
    /// The two sources reach one path through different addresses -- one writes the containing
    /// sequence item, the other the numeric mapping key -- and Section 15.1 makes those "one
    /// structural overlay node". Their native arrays therefore meet, concatenate, and are exactly
    /// what Section 8.7 asks to be warned about.
    /// </summary>
    /// <remarks>
    /// This fold happens at step 9 rather than step 8, because until ordering values are exposed
    /// the item and the key are not yet the same path. A warning raised only while merging source
    /// contributions misses it silently.
    /// </remarks>
    [Test]
    public void NativeArraysMeetingThroughDifferentAddressesAlsoWarn()
    {
        var (result, sink) = Transformation(
            ("item.yaml", "a:\n  - list: [p]\n"),
            ("key.yaml", "a:\n  0:\n    list: [q]\n"),
            ("scheme.txt", "a.output=namespace\n"));

        result.ExitCode.ShouldBe(0);
        sink.Written["a.properties"].ShouldBe("0.list.0=p\n0.list.1=q\n");
        Codes(result).Count(code => code == "WARN004").ShouldBe(1);
    }

    /// <summary>A directive at that path answers the question, so the warning does not arise.</summary>
    [Test]
    public void ADirectiveAtTheCrossAddressPathSilencesTheWarning()
    {
        var (result, _) = Transformation(
            ("item.yaml", "a:\n  - list: [p]\n"),
            ("key.yaml", "a:\n  0:\n    list: [q]\n"),
            ("scheme.txt", "a.output=namespace\na.0.list.merge=deep\n"));

        result.ExitCode.ShouldBe(0);
        Codes(result).ShouldNotContain("WARN004");
    }

    /// <summary>
    /// Section 16.10 spells the directive <c>[path.]merge=…</c>, so a bare <c>merge</c> governs the
    /// overlay root, where two root-level arrays can meet.
    /// </summary>
    /// <remarks>
    /// The root has no name, so it cannot be a key of the path map, and "declared <c>deep</c> at
    /// the root" has to be carried separately from "nothing was declared". Both produce the same
    /// strategy, and only one of them silences this warning.
    /// </remarks>
    [Test]
    public void ABareRootMergeDirectiveSilencesTheWarningAtTheRoot()
    {
        var withoutDirective = Transformation(
            ("first.yaml", "- one\n"),
            ("second.yaml", "- two\n"),
            ("scheme.txt", "output=namespace\nfilename=root\n"));

        withoutDirective.Result.ExitCode.ShouldBe(0);
        Codes(withoutDirective.Result).ShouldContain("WARN004");
        withoutDirective.Sink.Written["root"].ShouldBe("0=one\n1=two\n");

        var withDirective = Transformation(
            ("first.yaml", "- one\n"),
            ("second.yaml", "- two\n"),
            ("scheme.txt", "output=namespace\nfilename=root\nmerge=deep\n"));

        withDirective.Result.ExitCode.ShouldBe(0);
        Codes(withDirective.Result).ShouldNotContain("WARN004");
    }

    private static string Render(string input) =>
        RenderSources("app", ("input.txt", input));

    private static string RenderSources(string root, params (string Path, string Text)[] inputs)
    {
        var sink = new TransformationTests.Sink();
        var sources = new TransformationTests.Sources(
            [("scheme.txt", $"{root}.output=namespace\n"), .. inputs]);

        var arguments = ImmutableArray.CreateBuilder<string>();
        arguments.Add("-s");
        arguments.Add("scheme.txt");

        foreach (var (path, _) in inputs)
        {
            arguments.Add("-i");
            arguments.Add(path);
        }

        var result = TransformationTests.Run(sink, sources, [.. arguments]);

        result.ExitCode.ShouldBe(0, DescribeFailure(result));

        return sink.Written[$"{root}.properties"];
    }

    private static (TransformationResult Result, TransformationTests.Sink Sink) Transformation(
        params (string Path, string Text)[] sources)
    {
        var sink = new TransformationTests.Sink();
        var arguments = ImmutableArray.CreateBuilder<string>();

        foreach (var (path, _) in sources)
        {
            arguments.Add(path == "scheme.txt" ? "-s" : "-i");
            arguments.Add(path);
        }

        var result = TransformationTests.Run(
            sink,
            new TransformationTests.Sources(sources),
            [.. arguments]);

        return (result, sink);
    }

    private static ImmutableArray<string> Codes(TransformationResult result) =>
        [.. result.Diagnostics.Select(d => d.Code)];

    private static string DescribeFailure(TransformationResult result) =>
        result.Unsupported is { } unsupported
            ? $"declined: {unsupported.Capability} \u2014 {unsupported.Detail}"
            : "the run did not succeed";
}
