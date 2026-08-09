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
/// Section 12 wildcard templates: what a rule matches, what it substitutes into the value, and how
/// the Section 12.4 fixed point terminates and is accounted for.
/// </summary>
/// <remarks>
/// Every expectation is read from Section 12. The two worked examples the section states in full —
/// the Section 12.3 name expansion and the Section 12.2 explicit-capture rule — are asserted
/// verbatim, and the rest are the clauses those examples do not reach.
/// </remarks>
[TestFixture]
public sealed class WildcardTemplateTests
{
    // ---- 12.1 Legacy captures -----------------------------------------------------------------

    /// <summary>
    /// Section 12.3's worked example, verbatim: <c>a.x=1 / a.y=2 / a.*.z=3</c> generates
    /// <c>a.x.z=3</c> and <c>a.y.z=3</c>.
    /// </summary>
    /// <remarks>
    /// "The generated descendants are retained alongside the scalar payloads already present at
    /// <c>a.x</c> and <c>a.y</c>", so the two original payloads must survive the expansion.
    /// </remarks>
    [Test]
    public void TheWorkedNameExpansionExample()
    {
        Render("a.x=1\na.y=2\na.*.z=3\n").ShouldBe("x=1\nx.z=3\ny=2\ny.z=3\n");
    }

    /// <summary>
    /// "A wildcard matches only within one name part and never crosses a namespace delimiter."
    /// </summary>
    /// <remarks>
    /// <c>a.*</c> is two parts, so it can only name a depth-two item. If the delimiter were
    /// ordinary text the pattern would also match <c>a.p.q</c> and generate <c>a.p.q.z</c>.
    /// </remarks>
    [Test]
    public void AWildcardNeverCrossesTheDelimiter()
    {
        Render("a.p.q=1\na.*.z=3\n").ShouldBe("p.q=1\np.z=3\n");
    }

    /// <summary>
    /// "Captures are assigned left to right, each taking the shortest text that still permits the
    /// remaining pattern to match."
    /// </summary>
    /// <remarks>
    /// <c>*b*</c> against <c>abcbc</c> has two partitions: <c>a|cbc</c> and <c>abc|c</c>. The
    /// clause names the first, which is also the one a non-greedy regular expression would give and
    /// the opposite of what a greedy one would. Substituting both captures is what makes the choice
    /// observable — a match alone would succeed either way.
    /// </remarks>
    [Test]
    public void SeveralCapturesInOnePartTakeTheShortestTextLeftToRight()
    {
        Render("a.abcbc=1\na.*b*.out=<*>|<*>\n").ShouldBe("abcbc=1\nabcbc.out=<a>|<cbc>\n");
    }

    /// <summary>
    /// "If a legacy value contains more wildcard substitutions than the name produced, the last
    /// capture is repeated for compatibility."
    /// </summary>
    [Test]
    public void AThirdSubstitutionRepeatsTheLastCapture()
    {
        Render("a.p.b.q.keep=1\na.*.b.*.val=<*>-<*>-<*>\n")
            .ShouldBe("p.b.q.keep=1\np.b.q.val=<p>-<q>-<q>\n");
    }

    /// <summary>"If it contains fewer, unused captures are ignored."</summary>
    [Test]
    public void AnUnusedCaptureIsIgnored()
    {
        Render("a.p.b.q.keep=1\na.*.b.*.val=only<*>\n")
            .ShouldBe("p.b.q.keep=1\np.b.q.val=only<p>\n");
    }

    /// <summary>
    /// "In an ordinary entry whose name defines no unnamed captures, <c>*</c> is literal text, so
    /// values such as <c>pattern=*.txt</c> require no escape."
    /// </summary>
    /// <remarks>
    /// The emitted document escapes the asterisk again, because Section 19.1 spells a literal
    /// asterisk defensively rather than reasoning about the name it will be read back under. What
    /// the assertion pins is that the asterisk survived as itself: had the value been lexed as a
    /// capture substitution the entry would have had no capture to substitute.
    /// </remarks>
    [Test]
    public void AnAsteriskInAnOrdinaryValueIsLiteralText()
    {
        Render("a.pattern=*.txt\n").ShouldBe("pattern=\\*.txt\n");
    }

    /// <summary>
    /// "<c>\*</c> remains the explicit literal spelling in a template value."
    /// </summary>
    /// <remarks>
    /// The name defines an unnamed capture, so an unescaped asterisk here would have been replaced
    /// by <c>x</c>. The escape is what keeps it an asterisk, and Section 19.1 re-escapes it on the
    /// way out.
    /// </remarks>
    [Test]
    public void AnEscapedAsteriskInATemplateValueIsLiteralText()
    {
        Render("a.x=1\na.*.z=\\*\n").ShouldBe("x=1\nx.z=\\*\n");
    }

    // ---- 12.2 Explicit captures ---------------------------------------------------------------

    /// <summary>
    /// Section 12.2's worked example, verbatim:
    /// <c>a.*[0].b.*[1].val=text1*[1]-repeat-*[1]</c>.
    /// </summary>
    /// <remarks>
    /// Both substitutions name capture <c>1</c>, so both take the second capture. A positional
    /// reading would have produced the first capture in the first position, which is the whole
    /// difference between the two forms.
    /// </remarks>
    [Test]
    public void TheWorkedExplicitCaptureExample()
    {
        Render("a.p.b.q.keep=1\na.r.b.s.keep=2\na.*[0].b.*[1].val=text1*[1]-repeat-*[1]\n")
            .ShouldBe(
                "p.b.q.keep=1\np.b.q.val=text1q-repeat-q\n"
                + "r.b.s.keep=2\nr.b.s.val=text1s-repeat-s\n");
    }

    /// <summary>
    /// "The same identifier reused in the name must match the same text" and "inconsistent repeated
    /// captures are nonmatches".
    /// </summary>
    /// <remarks>
    /// A nonmatch, not an error: the run succeeds and simply generates nothing for
    /// <c>a.p.b.q</c>. Reporting it would make an ordinary selective template a failure.
    /// </remarks>
    [Test]
    public void ARepeatedIdentifierMustMatchTheSameTextAndOtherwiseDoesNotMatch()
    {
        Render("a.p.b.p.keep=1\na.p.b.q.keep=2\na.*[0].b.*[0].val=hit\n")
            .ShouldBe("p.b.p.keep=1\np.b.p.val=hit\np.b.q.keep=2\n");
    }

    /// <summary>"An undefined capture is an error."</summary>
    [Test]
    public void AValueSubstitutingAnUndefinedCaptureIsAnError()
    {
        var (result, _) = Transform("a.x=1\na.*[9].val=*[8]\n");

        result.ExitCode.ShouldBe(1);
        Codes(result).ShouldBe(["WILDCARD001"]);
    }

    /// <summary>"A single rule must not mix explicit and legacy unnamed captures."</summary>
    [Test]
    public void MixingCaptureFormsInOneRuleIsAnError()
    {
        var (result, _) = Transform("a.x=1\na.*[0].b.*.val=x\n");

        result.ExitCode.ShouldBe(1);
        Codes(result).ShouldBe(["WILDCARD001"]);
    }

    /// <summary>
    /// Both faults are properties of the rule alone, so one run reports every bad rule rather than
    /// stopping at the first.
    /// </summary>
    /// <remarks>
    /// Section 15.4 continues after a blocking error precisely so that a run reports everything it
    /// can decide. A rule dropped at validation still lets its well-formed neighbours run.
    /// </remarks>
    [Test]
    public void EveryMalformedRuleIsReportedInOneRun()
    {
        var (result, _) = Transform("a.x=1\na.*[0].b.*.val=x\na.*[9].val=*[8]\n");

        Codes(result).ShouldBe(["WILDCARD001", "WILDCARD001"]);
    }

    /// <summary>
    /// "Capture text inserted into a generated name is literal text inside one name part. It is
    /// never re-lexed as delimiter, wildcard, reference, or escape syntax."
    /// </summary>
    /// <remarks>
    /// The matched part is spelled <c>x\.y</c>, so its text contains a delimiter character. Were
    /// the generated name re-lexed, that character would split it into two parts and the entry
    /// would be emitted at <c>x.y.z</c> instead of under the single part the match found.
    /// </remarks>
    [Test]
    public void CaptureTextInAGeneratedNameIsNeverRelexed()
    {
        Render("a.x\\.y.keep=1\na.*.z=2\n").ShouldBe("x\\u{2E}y.keep=1\nx\\u{2E}y.z=2\n");
    }

    // ---- 12.3 Matching scope ------------------------------------------------------------------

    /// <summary>
    /// "Sequences expose their stable ordering values as decimal name parts" and "when a generated
    /// suffix targets a sequence item, it is deep-merged into that item's overlay node".
    /// </summary>
    /// <remarks>
    /// Deep-merged into the item, not grafted beside it: a contribution placed as an ordinary
    /// mapping child named <c>0</c> would give the container a second, competing facet and the
    /// item would never see it. Two items are used so that a rule matching only the first would be
    /// visible.
    /// </remarks>
    [Test]
    public void AGeneratedSuffixIsDeepMergedIntoTheSequenceItem()
    {
        var (result, sink) = Transformation(
            ("first.yaml", "b:\n  - x: 1\n  - x: 2\n"),
            ("second.txt", "b.*.z=deep\n"),
            ("scheme.txt", "b.output=namespace\n"));

        result.ExitCode.ShouldBe(0);
        sink.Written["b.properties"].ShouldBe("0.x=1\n0.z=deep\n1.x=2\n1.z=deep\n");
    }

    /// <summary>
    /// Section 15.1 makes a numeric mapping child and the sequence item at its ordering value "one
    /// structural overlay node", so the two addresses are one item and are matched once.
    /// </summary>
    /// <remarks>
    /// Two candidate charges for one logical item would also mean two generated contributions at
    /// one name, which Section 12.4 forbids: "each generative (rule, matched logical item) pair is
    /// applied at most once".
    /// </remarks>
    [Test]
    public void AnItemReachableThroughBothFacetsIsMatchedOnce()
    {
        var (result, sink) = Transformation(
            ("first.yaml", "a:\n  - x: 1\n"),
            ("second.txt", "a.0.y=2\n"),
            ("third.txt", "a.*.z=3\n"),
            ("scheme.txt", "a.output=namespace\n"));

        result.ExitCode.ShouldBe(0);
        sink.Written["a.properties"].ShouldBe("0.x=1\n0.y=2\n0.z=3\n");
    }

    // ---- 12.4 Fixed-point evaluation ----------------------------------------------------------

    /// <summary>
    /// "Every template must be matched against every eligible concrete or generated entry present
    /// in the current fixed-point evaluation, regardless of whether the matched entry originated
    /// before or after the template."
    /// </summary>
    [Test]
    public void ATemplateMatchesAnEntryWrittenAfterIt()
    {
        Render2(("first.txt", "a.*.z=generated\n"), ("second.txt", "a.x=1\n"))
            .ShouldBe("x=1\nx.z=generated\n");
    }

    /// <summary>
    /// "Source order controls precedence, not visibility": the earlier template still sees the
    /// later entry, and still loses to it.
    /// </summary>
    [Test]
    public void AnEarlierTemplateLosesToALaterConcreteContribution()
    {
        Render2(
            ("first.txt", "a.*.z=fromtemplate\n"),
            ("second.txt", "a.x=1\na.x.z=concrete\n"))
            .ShouldBe("x=1\nx.z=concrete\n");
    }

    /// <summary>The converse: a later template outranks an earlier concrete contribution.</summary>
    [Test]
    public void ALaterTemplateBeatsAnEarlierConcreteContribution()
    {
        Render2(
            ("first.txt", "a.x=1\na.x.z=concrete\n"),
            ("second.txt", "a.*.z=fromtemplate\n"))
            .ShouldBe("x=1\nx.z=fromtemplate\n");
    }

    /// <summary>
    /// "Items generated during the wave become eligible in the next wave", so a rule can match what
    /// another rule generated.
    /// </summary>
    /// <remarks>
    /// Three rules at increasing depth need three waves. A single-pass implementation produces
    /// <c>x.b</c> and stops, because <c>a.*.*.c</c> has no depth-three item to look at when the
    /// only pass runs.
    /// </remarks>
    [Test]
    public void AGeneratedEntryBecomesEligibleInTheNextWave()
    {
        Render("a.x=1\na.*.b=2\na.*.*.c=3\na.*.*.*.d=4\n")
            .ShouldBe("x=1\nx.b=2\nx.b.c=3\nx.b.c.d=4\n");
    }

    /// <summary>
    /// Section 12.5: "if several rules produce the same name, the later rule wins".
    /// </summary>
    [Test]
    public void TheLaterOfTwoRulesProducingOneNameWins()
    {
        Render("a.x=1\na.*.z=first\na.*.z=second\n").ShouldBe("x=1\nx.z=second\n");
    }

    /// <summary>
    /// "Consequently, <c>merge=error</c> can intentionally make a wildcard-generated contribution
    /// fail when another contribution already exists at its target path."
    /// </summary>
    /// <remarks>
    /// The same input without the template is accepted, which is what shows the generated
    /// contribution — rather than the two concrete entries — is the second one at that path.
    /// </remarks>
    [Test]
    public void MergeErrorRejectsAGeneratedContributionAtAnOccupiedPath()
    {
        var scheme = "a.output=namespace\na.x.z.merge=error\n";

        var withoutTemplate = Transformation(
            ("in.txt", "a.x=1\na.x.z=concrete\n"), ("scheme.txt", scheme));

        withoutTemplate.Result.ExitCode.ShouldBe(0);

        var withTemplate = Transformation(
            ("in.txt", "a.x=1\na.x.z=concrete\na.*.z=generated\n"), ("scheme.txt", scheme));

        withTemplate.Result.ExitCode.ShouldBe(1);
        Codes(withTemplate.Result).ShouldContain("TYPE001");
    }

    /// <summary>
    /// Section 8.6: a permanent mask is "active throughout wildcard fixed-point evaluation" and is
    /// applied "to every candidate when it appears", so it suppresses both a candidate a rule would
    /// have matched and a name a rule generates.
    /// </summary>
    [Test]
    public void AMaskSuppressesBothTheCandidateAndTheGeneratedName()
    {
        Render("a.x=1\na.y=2\n!a.y\na.*.z=3\n!a.x.z\n").ShouldBe("x=1\n");
    }

    /// <summary>
    /// Section 12.4: masks are "predicates rather than one-shot worklist items", so one mask
    /// suppresses every match of every rule rather than being consumed by the first.
    /// </summary>
    [Test]
    public void OneMaskSuppressesEveryMatchOfEveryRule()
    {
        Render("a.p.q=1\na.p.r=2\n!a.p.*.z\na.*.*.z=x\na.*.*.z=y\n")
            .ShouldBe("p.q=1\np.r=2\n");
    }

    // ---- 12.4 limits --------------------------------------------------------------------------

    /// <summary>
    /// "Enforce configurable generated-entry and iteration limits" and "report the rules
    /// responsible for the limit".
    /// </summary>
    [Test]
    public void CrossingTheGeneratedLimitNamesTheResponsibleRule()
    {
        var (result, sink) = Transformation(
            ["--max-generated", "1"],
            ("in.txt", "a.x=1\na.y=2\na.*.z=3\n"),
            ("scheme.txt", "a.output=namespace\n"));

        result.ExitCode.ShouldBe(1);
        Codes(result).ShouldBe(["WILDCARD002"]);
        result.Diagnostics.Single().Rule.ShouldBe(["a.*.z"]);
        sink.Written.ShouldBeEmpty();
    }

    /// <summary>
    /// Section 23 counts a <c>(rule,item)</c> candidate check "once for a (rule,item) pair", and
    /// Section 12.4 charges it before the match is attempted rather than after it succeeds.
    /// </summary>
    /// <remarks>
    /// The rule matches nothing here — the two captures are one identifier, and <c>p</c> is not
    /// <c>one</c> — so a limit that counted matches would never be reached, and a rule set that
    /// spends its whole budget failing to match would be unbounded.
    /// </remarks>
    [Test]
    public void ANonmatchingPairStillConsumesTheCandidateLimit()
    {
        var (result, _) = Transformation(
            ["--max-wildcard-candidates", "1"],
            ("in.txt", "a.p.one=1\na.p.two=2\na.*[0].*[0].z=never\n"),
            ("scheme.txt", "a.output=namespace\n"));

        result.ExitCode.ShouldBe(1);
        Codes(result).ShouldBe(["WILDCARD002"]);
    }

    /// <summary>
    /// Section 12.4 makes a pair a candidate only when "every literal name part before that point
    /// equals the corresponding item part", so an item under a different literal prefix is not
    /// charged at all.
    /// </summary>
    /// <remarks>
    /// Two documents differing only in items the rule cannot reach must cost the same, or a limit
    /// tuned for one part of a tree would fire because of an unrelated part of it. The limit here
    /// admits the one eligible item and would not admit the three depth-two paths that exist.
    /// </remarks>
    [Test]
    public void AnItemUnderADifferentLiteralPrefixIsNotACandidate()
    {
        var (result, sink) = Transformation(
            ["--max-wildcard-candidates", "2"],
            ("in.txt", "a.one=1\nb.two=2\nb.three=3\na.*.z=v\n"),
            ("scheme.txt", "a.output=namespace\nb.output=namespace\n"));

        result.ExitCode.ShouldBe(0);
        sink.Written["a.properties"].ShouldBe("one=1\none.z=v\n");
    }

    /// <summary>
    /// Section 12.5: "If one rule produces the same name more than once, the later deterministic
    /// match ordinal wins" — which requires each match of one rule to carry its own key.
    /// </summary>
    /// <remarks>
    /// Section 4.7 makes two contributions with one key one contribution, so a rule whose matches
    /// shared a key would be indistinguishable from a rule that contributed once. The rule position
    /// alone is shared by every match, and the match ordinal is the only component that separates
    /// them.
    /// </remarks>
    [Test]
    public void EachMatchOfOneRuleCarriesADistinctOrderingKey()
    {
        var start = StableOrderingKey.FromSource(0, 1);
        var root = OverlayNode.Intermediate(start).WithChild(
            Ordinary("a"),
            OverlayNode.Intermediate(start)
                .WithChild(Ordinary("x"), OverlayNode.OfPayload(ScalarPayload.OfString("1"), start))
                .WithChild(Ordinary("y"), OverlayNode.OfPayload(ScalarPayload.OfString("2"), start)));

        var container = Evaluate(root, "a.*.z=3\n").Children[Ordinary("a")];

        var first = container.Children[Ordinary("x")].Children[Ordinary("z")].Marks.Position;
        var second = container.Children[Ordinary("y")].Children[Ordinary("z")].Marks.Position;

        first.ShouldNotBe(second);
    }

    /// <summary>
    /// "Every wildcard rule category consumes the shared candidate-check limit once per eligible
    /// pair", which Section 12.4 lists as generative templates, "permanent wildcard ignore masks",
    /// and wildcard scheme selectors.
    /// </summary>
    /// <remarks>
    /// The mask generates nothing, so the only way it can cross a limit is by being charged for the
    /// checks it performs. Without the mask the same input and the same limit succeed.
    /// </remarks>
    [Test]
    public void AWildcardMaskConsumesTheSharedCandidateLimit()
    {
        var arguments = new[] { "--max-wildcard-candidates", "2" };
        var scheme = ("scheme.txt", "a.output=namespace\n");

        Transformation(arguments, ("in.txt", "a.x=1\na.y=2\na.*.z=3\n"), scheme)
            .Result.ExitCode.ShouldBe(0);

        var withMask = Transformation(
            arguments, ("in.txt", "a.x=1\na.y=2\n!a.*.q\na.*.z=3\n"), scheme);

        withMask.Result.ExitCode.ShouldBe(1);
        Codes(withMask.Result).ShouldBe(["WILDCARD002"]);
    }

    /// <summary>
    /// Section 12.4 charges a candidate check on eligibility -- "full capture matching may then
    /// succeed or fail without another candidate charge" -- so a mask is charged for the items it
    /// suppresses as well as for the ones it leaves alone.
    /// </summary>
    /// <remarks>
    /// Section 8.6 discards a masked contribution "before literal-path merge validation", so these
    /// two items are absent from the model the fixed point runs over and an implementation that
    /// counts only what survives counts nothing here. Both bounds are asserted because only the
    /// pair pins the count: the loose one fails if anything is charged twice, the tight one if the
    /// suppressed items are charged not at all.
    /// </remarks>
    [Test]
    public void AMaskIsChargedForTheItemsItSuppresses()
    {
        var scheme = ("scheme.txt", "b.output=namespace\n");
        var input = ("in.txt", "a.x=1\na.y=2\nb.keep=3\n!a.*\n");

        Transformation(["--max-wildcard-candidates", "2"], input, scheme)
            .Result.ExitCode.ShouldBe(0);

        var tight = Transformation(["--max-wildcard-candidates", "1"], input, scheme);

        tight.Result.ExitCode.ShouldBe(1);
        Codes(tight.Result).ShouldBe(["WILDCARD002"]);
        tight.Result.Diagnostics.Single().Rule.ShouldBe(["a.*"]);
    }

    /// <summary>
    /// The rule limit is charged for the whole worklist before any evaluation, so it is crossed by
    /// declaring the rules rather than by matching anything.
    /// </summary>
    [Test]
    public void CrossingTheRuleLimitNamesEveryRule()
    {
        var (result, _) = Transformation(
            ["--max-wildcard-rules", "1"],
            ("in.txt", "a.x=1\na.*.z=one\na.*.y=two\n"),
            ("scheme.txt", "a.output=namespace\n"));

        result.ExitCode.ShouldBe(1);
        var rule = result.Diagnostics.Single().Rule;
        rule.ShouldContain("a.*.z");
        rule.ShouldContain("a.*.y");
    }

    /// <summary>
    /// "Breadth-wave iteration counts apply only to generative templates", so the limit bounds the
    /// waves that generate and not the pass that establishes the fixed point.
    /// </summary>
    /// <remarks>
    /// A rule set whose whole expansion happens in one wave must therefore succeed at a limit of
    /// one. Charging the confirming pass as well would make <c>--max-wildcard-iterations=1</c>
    /// unsatisfiable for every rule set that matches anything at all, which is not a limit anyone
    /// could configure meaningfully.
    /// </remarks>
    [Test]
    public void OneGeneratingWaveFitsAnIterationLimitOfOne()
    {
        var (result, sink) = Transformation(
            ["--max-wildcard-iterations", "1"],
            ("in.txt", "a.x=1\na.y=2\na.*.z=3\n"),
            ("scheme.txt", "a.output=namespace\n"));

        result.ExitCode.ShouldBe(0);
        sink.Written["a.properties"].ShouldBe("x=1\nx.z=3\ny=2\ny.z=3\n");
    }

    /// <summary>
    /// A cascade needs one wave per level, so it crosses the same limit, and the report names every
    /// rule that has generated.
    /// </summary>
    [Test]
    public void ACascadeCrossesTheIterationLimit()
    {
        var (result, _) = Transformation(
            ["--max-wildcard-iterations", "2"],
            ("in.txt", "a.x=1\na.*.b=2\na.*.*.c=3\na.*.*.*.d=4\n"),
            ("scheme.txt", "a.output=namespace\n"));

        result.ExitCode.ShouldBe(1);
        Codes(result).ShouldBe(["WILDCARD002"]);
        var rule = result.Diagnostics.Single().Rule;
        rule.ShouldContain("a.*.b");
        rule.ShouldContain("a.*.*.c");
    }

    /// <summary>The same cascade completes when the limit admits every generating wave.</summary>
    [Test]
    public void ACascadeCompletesWithinItsIterationLimit()
    {
        var (result, sink) = Transformation(
            ["--max-wildcard-iterations", "3"],
            ("in.txt", "a.x=1\na.*.b=2\na.*.*.c=3\na.*.*.*.d=4\n"),
            ("scheme.txt", "a.output=namespace\n"));

        result.ExitCode.ShouldBe(0);
        sink.Written["a.properties"].ShouldBe("x=1\nx.b=2\nx.b.c=3\nx.b.c.d=4\n");
    }

    // ---- 12.3 shape preservation --------------------------------------------------------------

    /// <summary>
    /// A generated descendant reached through a sequence item must not change which container
    /// Section 4.4 selects at the item's parent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// After Section 15.1 step 9 a numeric mapping child and the sequence item at its ordering
    /// value are one node held by both facets of the parent. Refreshing both shape-marks with the
    /// generated contribution gives them the same Section 4.7 key, and
    /// <see cref="NodeMarks.ContainerIsMapping"/> resolves an equal pair to the mapping — so
    /// deep-merging into a sequence item would silently turn the sequence into a mapping, which is
    /// the opposite of Section 12.3's "deep-merged into that item's overlay node".
    /// </para>
    /// <para>
    /// The overlay is built here rather than read from sources because the contest has to be set up
    /// with the sequence winning by a specific margin, and an input that produces exactly that
    /// arrangement also produces further grafts that mask the defect.
    /// </para>
    /// </remarks>
    [Test]
    public void DeepMergingIntoASequenceItemLeavesTheContainerASequence()
    {
        var early = StableOrderingKey.FromSource(0, 1);
        var late = StableOrderingKey.FromSource(0, 2);
        var item = OverlayNode.OfPayload(ScalarPayload.OfString("x"), early);

        var container = OverlayNode.Intermediate(early)
            .WithChild(Ordinary("0"), item)
            .WithSequenceItem(0, SequenceItem.Native(item))
            .WithExplicitSequence(late);

        container.Marks.ContainerIsSequence.ShouldBeTrue("the arrangement under test");

        var root = OverlayNode.Intermediate(early).WithChild(Ordinary("a"), container);
        var evaluated = Evaluate(root, "a.*.z=t\n");

        evaluated.Children[Ordinary("a")].Marks.ContainerIsSequence.ShouldBeTrue();
    }

    /// <summary>
    /// The generated contribution still reaches the item, so preserving the shape is not achieved
    /// by declining to record the contribution at all.
    /// </summary>
    [Test]
    public void DeepMergingIntoASequenceItemStillPlacesTheContribution()
    {
        var early = StableOrderingKey.FromSource(0, 1);
        var item = OverlayNode.OfPayload(ScalarPayload.OfString("x"), early);

        var root = OverlayNode.Intermediate(early).WithChild(
            Ordinary("a"),
            OverlayNode.Intermediate(early)
                .WithChild(Ordinary("0"), item)
                .WithSequenceItem(0, SequenceItem.Native(item))
                .WithExplicitSequence(StableOrderingKey.FromSource(0, 2)));

        var evaluated = Evaluate(root, "a.*.z=t\n");
        var container = evaluated.Children[Ordinary("a")];

        container.Sequence[0].Node.Children[Ordinary("z")].Payload
            .ShouldNotBeNull().Text.ShouldBe("t");
        container.Children[Ordinary("0")].Children.ShouldContainKey(Ordinary("z"));
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static OrdinaryPart Ordinary(string text) => new([new LiteralToken(text)]);

    private static ImmutableArray<string> Codes(TransformationResult result) =>
        [.. result.Diagnostics.Select(d => d.Code)];

    /// <summary>
    /// Runs step 10 alone over a hand-built overlay, with the rules read from a namespace document.
    /// </summary>
    private static OverlayNode Evaluate(OverlayNode root, string document)
    {
        var diagnostics = new DiagnosticBuffer();
        var source = ProfileSource.OfFile("rules.txt");
        var records = document
            .Split('\n')
            .Select((line, index) => NamespaceRecordClassifier.Classify(line, index + 1))
            .ToImmutableArray();

        var contribution = NamespaceProfileReader.Read(records, 1, source, SubstituteModeMap.Default, diagnostics);

        var rules = contribution.Templates
            .Select(template => new WildcardRule(
                template.Name,
                template.Value,
                template.Order,
                template.Comments,
                source.File,
                source.Identity,
                template.Line))
            .ToImmutableArray();

        var evaluated = new WildcardEvaluator(
            WildcardEvaluator.Validate(rules, diagnostics),
            ExclusionMask.None,
            new OverlayMerger(MergeStrategyMap.Default, diagnostics),
            new GlobalBudget(ResourceLimits.Defaults),
            diagnostics).Evaluate(root);

        diagnostics.Drain().ShouldBeEmpty();

        return evaluated;
    }

    private static string Render(string document) =>
        Render2(("in.txt", document));

    private static string Render2(params (string Path, string Text)[] inputs)
    {
        var (result, sink) = Transformation(
            [.. inputs, ("scheme.txt", "a.output=namespace\n")]);

        result.ExitCode.ShouldBe(
            0,
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code} {d.Message}")));

        return sink.Written["a.properties"];
    }

    private static (TransformationResult Result, TransformationTests.Sink Sink) Transform(
        string document) =>
        Transformation(("in.txt", document), ("scheme.txt", "a.output=namespace\n"));

    private static (TransformationResult Result, TransformationTests.Sink Sink) Transformation(
        params (string Path, string Text)[] sources) =>
        Transformation([], sources);

    private static (TransformationResult Result, TransformationTests.Sink Sink) Transformation(
        string[] options, params (string Path, string Text)[] sources)
    {
        var sink = new TransformationTests.Sink();
        var arguments = ImmutableArray.CreateBuilder<string>();

        arguments.AddRange(options);

        foreach (var (path, _) in sources)
        {
            arguments.Add(path.StartsWith("scheme", StringComparison.Ordinal) ? "-s" : "-i");
            arguments.Add(path);
        }

        return (
            TransformationTests.Run(sink, new TransformationTests.Sources(sources), [.. arguments]),
            sink);
    }
}
