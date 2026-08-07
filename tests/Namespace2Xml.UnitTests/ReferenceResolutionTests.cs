using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 13: what a reference resolves to, what kind the result has, and which defects Section
/// 14.4 lets an unreachable entry get away with.
/// </summary>
/// <remarks>
/// <para>
/// Every expectation is read from Section 13. The two clauses the section states as worked
/// examples — type forwarding through <c>port=${database.port}</c> and concatenation through
/// <c>endpoint=https://${host}:${port}</c> — are asserted as written.
/// </para>
/// <para>
/// The kind tests go through <see cref="ReferenceResolver"/> directly rather than through a
/// rendered file. Section 13.2 is entirely about the <em>kind</em> a resolved payload carries, and
/// the two flat formats this build serializes render a forwarded decimal and a concatenated string
/// as the same bytes. Asserting the rendering would pass whether or not the kind was forwarded,
/// which is the definition of a test that proves nothing.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ReferenceResolutionTests
{
    // ---- 13.1 Resolution ----------------------------------------------------------------------

    /// <summary>
    /// Section 13.1: "descendants of the referenced path are never copied". The referring entry
    /// takes the scalar and nothing below it, and the referent keeps its own descendants.
    /// </summary>
    [Test]
    public void DescendantsOfTheReferencedPathAreNeverCopied() =>
        Render("a.k=${a.t}\na.t=v\na.t.deep=w\n").ShouldBe("k=v\nt=v\nt.deep=w\n");

    /// <summary>
    /// Section 13.1: "referencing a path that has descendants but no scalar/null payload is a
    /// missing-reference error". Having children is not the same as having a value.
    /// </summary>
    [Test]
    public void APathWithDescendantsButNoPayloadIsMissing() =>
        Failure("a.k=${a.t}\na.t.deep=w\n").ShouldBe("REFERENCE002");

    /// <summary>Section 13.1: "no match is a missing-reference error".</summary>
    [Test]
    public void APathThatIsNotThereIsMissing() =>
        Failure("a.k=${nowhere}\n").ShouldBe("REFERENCE002");

    /// <summary>
    /// Section 13.1: "References may be recursive." A chain of ordinary references resolves
    /// through to the scalar at its end.
    /// </summary>
    [Test]
    public void AChainOfReferencesResolvesThrough() =>
        Render("a.k=${a.m}\na.m=${a.n}\na.n=v\n").ShouldBe("k=v\nm=v\nn=v\n");

    /// <summary>
    /// Section 13.1 resolves against the whole model, not against the selected subtree, so an
    /// entry outside every selector is still a legitimate reference target.
    /// </summary>
    /// <remarks>
    /// Section 14.4 says as much in the other direction: "All entries reached transitively through
    /// references from selected entries are retained for evaluation". Retaining them would be
    /// pointless if they could not be referred to.
    /// </remarks>
    [Test]
    public void AReferenceMayLeaveTheSelectedSubtree() =>
        Render("a.k=${shared.v}\nshared.v=x\n").ShouldBe("k=x\n");

    /// <summary>
    /// Section 13.1: a reference "resolves only the scalar or null payload stored at that exact
    /// canonical path", which for a self-reference is the payload being resolved.
    /// </summary>
    [Test]
    public void AValueThatReferencesItselfIsACycle() =>
        Failure("a.k=${a.k}\n").ShouldBe("REFERENCE003");

    /// <summary>
    /// Section 13.1: "Missing references and cycles are blocking errors", so a two-step cycle is
    /// refused rather than unrolled.
    /// </summary>
    [Test]
    public void ACycleThroughTwoValuesIsRefused() =>
        Failure("a.k=${a.m}\na.m=${a.k}\n").ShouldBe("REFERENCE003");

    /// <summary>
    /// Section 22 counts <c>REFERENCE003</c> "once per canonically distinct reachable cycle", and
    /// a cycle of three members is reached from all three.
    /// </summary>
    [Test]
    public void OneCycleIsReportedOnce() =>
        Codes("a.k=${a.m}\na.m=${a.n}\na.n=${a.k}\n").ShouldBe(["REFERENCE003"]);

    /// <summary>
    /// Section 24 forbids output that depends on the order sources were given. The cycle report is
    /// output, so permuting the entries that form a cycle must not move it.
    /// </summary>
    /// <remarks>
    /// This is the assertion the rotation in <see cref="ReferenceResolver"/> exists for. Each
    /// member of a cycle spells the chain starting at itself, so a report keyed or located by
    /// whichever member was reached first says something different for every permutation of one
    /// unchanged input.
    /// </remarks>
    [Test]
    public void ACycleReportsIdenticallyUnderPermutation()
    {
        var forward = Diagnostics("a.k=${a.m}\na.m=${a.n}\na.n=${a.k}\n");
        var permuted = Diagnostics("a.n=${a.k}\na.m=${a.n}\na.k=${a.m}\n");

        forward.Select(d => $"{d.Code} {d.Message}")
            .ShouldBe(permuted.Select(d => $"{d.Code} {d.Message}"), Case.Sensitive);
    }

    // ---- 13.1 Alias index ---------------------------------------------------------------------

    /// <summary>
    /// Section 13.1: "an XML simple alias ... replaces every <c>@local</c> part with
    /// <c>local</c>", so a format-agnostic reference reaches an attribute without naming it as one.
    /// </summary>
    [Test]
    public void AFormatAgnosticReferenceReachesAnAttributeThroughItsSimpleAlias() =>
        Render("a.k=${a.t.x}\na.t.@x=v\n").ShouldBe("k=v\nt.@x=v\n");

    /// <summary>
    /// Section 13.1's worked ambiguity: "an XML attribute and unqualified child element both named
    /// <c>x</c> make <c>${a.x}</c> ambiguous".
    /// </summary>
    [Test]
    public void AnAttributeAndAnElementOfTheSameNameMakeTheAliasAmbiguous() =>
        Failure("a.k=${a.t.x}\na.t.@x=v\na.t.Q{u}x=w\n").ShouldBe("REFERENCE004");

    /// <summary>
    /// The same ambiguity with a plain unqualified element rather than a qualified one, which is
    /// the pairing Section 13.1 actually names. The element's own simple alias is its own path, so
    /// it competes with the attribute for the unmarked spelling.
    /// </summary>
    [Test]
    public void AnAttributeAndAnUnqualifiedElementOfTheSameNameMakeTheAliasAmbiguous() =>
        Failure("a.k=${a.t.x}\na.t.@x=v\na.t.x=w\n").ShouldBe("REFERENCE004");

    /// <summary>
    /// Section 11.4 offers <c>Q{}x</c> as the way to say "the element, not the attribute", but an
    /// empty URI yields an ordinary component, so the escape is unavailable and the reference stays
    /// ambiguous. Recorded in <c>KNOWN-LIMITS.md</c> section 1.6.
    /// </summary>
    [Test]
    public void AnEmptyQualifierDoesNotEscapeTheAmbiguity() =>
        Failure("a.k=${a.t.Q{}x}\na.t.@x=v\na.t.x=w\n").ShouldBe("REFERENCE004");

    /// <summary>
    /// Section 13.1, continuing the same example: "<c>${a.@x}</c> selects the attribute and
    /// <c>${a.Q{}x}</c> selects the child element". A canonical reference "resolves one exact
    /// canonical path" and never consults the alias index, so the ambiguity does not reach it.
    /// </summary>
    [Test]
    public void ACanonicalReferenceSelectsOneOfTheAmbiguousCandidates() =>
        Render("a.k=${a.t.@x}\na.t.@x=v\na.t.Q{u}x=w\n")
            .ShouldBe("k=v\nt.@x=v\nt.Q{u}x=w\n");

    /// <summary>
    /// Section 13.1: "more than one canonical scalar having the same simple alias is a blocking
    /// ambiguous-reference error" — but only for the value that refers to it. An ambiguous alias
    /// nothing refers to is not a defect at all.
    /// </summary>
    [Test]
    public void AnAmbiguousAliasNothingRefersToIsNotAnError() =>
        Render("a.t.@x=v\na.t.Q{u}x=w\n").ShouldBe("t.@x=v\nt.Q{u}x=w\n");

    // ---- 13.2 Type forwarding -----------------------------------------------------------------

    /// <summary>
    /// Section 13.2's worked example: <c>port=${database.port}</c> "remains numeric when
    /// <c>database.port</c> is numeric".
    /// </summary>
    [Test]
    public void AValueThatIsExactlyOneReferenceInheritsTheReferentsKind() =>
        ResolveKind("${b}", ScalarPayload.OfInteger(5432)).ShouldBe(ScalarKind.Integer);

    /// <summary>
    /// Section 13.2: "After recursive resolution, a single-reference payload adopts the referent's
    /// kind transitively."
    /// </summary>
    /// <remarks>
    /// Transitivity is not free. It holds because the referent is resolved to its own settled
    /// payload before it is adopted, so what is adopted is never itself a reference. A resolver
    /// that copied the referent's text instead would forward the kind one hop and lose it after.
    /// </remarks>
    [Test]
    public void KindForwardingIsTransitive()
    {
        var model = Model(
            ("a", Unresolved("${b}")),
            ("b", Unresolved("${c}")),
            ("c", ScalarPayload.OfBoolean(true)));

        Resolve(model, "a").Kind.ShouldBe(ScalarKind.Boolean);
    }

    /// <summary>
    /// Section 13.2's second worked example: <c>endpoint=https://${host}:${port}</c>. "If a value
    /// contains any concatenation, its result is a string."
    /// </summary>
    [Test]
    public void AnyConcatenationProducesAString()
    {
        var model = Model(
            ("endpoint", Unresolved("https://${host}:${port}")),
            ("host", ScalarPayload.OfString("example")),
            ("port", ScalarPayload.OfInteger(443)));

        var resolved = Resolve(model, "endpoint");

        resolved.Kind.ShouldBe(ScalarKind.String);
        resolved.ToCanonicalText().ShouldBe("https://example:443");
    }

    /// <summary>
    /// Section 13.2: the result of a concatenation is a string, not an untyped payload that
    /// Section 18 would classify again.
    /// </summary>
    /// <remarks>
    /// Step 12 has already run by the time references resolve, so nothing would re-infer this
    /// today. The kind is settled anyway, because the alternative depends on a step ordering
    /// stated somewhere else to stay correct: concatenating <c>1</c> and <c>2</c> must be the
    /// string <c>12</c>, and an untyped payload spelling <c>12</c> is one inference away from
    /// being a number.
    /// </remarks>
    [Test]
    public void AConcatenatedResultIsSettledRatherThanUntyped()
    {
        var model = Model(
            ("a", Unresolved("${b}${c}")),
            ("b", ScalarPayload.OfInteger(1)),
            ("c", ScalarPayload.OfInteger(2)));

        var resolved = Resolve(model, "a");

        resolved.IsUntyped.ShouldBeFalse();
        resolved.Kind.ShouldBe(ScalarKind.String);
        resolved.ToCanonicalText().ShouldBe("12");
    }

    /// <summary>
    /// Section 13.2's canonical interpolation text: null interpolates as the four letters
    /// <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Section 19 lets each output format spell null its own way, which is why
    /// <see cref="ScalarPayload.ToCanonicalText"/> refuses it. Interpolation is not an output
    /// format: by the time the text is inside a larger string no format has a say in it.
    /// </remarks>
    [Test]
    public void NullInterpolatesAsTheWordNull()
    {
        var model = Model(("a", Unresolved("x=${b}")), ("b", ScalarPayload.Null));

        Resolve(model, "a").ToCanonicalText().ShouldBe("x=null");
    }

    /// <summary>
    /// Section 13.2's canonical interpolation text for Boolean is "lowercase <c>true</c> or
    /// <c>false</c>", which is the one entry in that list a careless conversion gets wrong.
    /// </summary>
    [Test]
    public void BooleanInterpolatesInLowercase()
    {
        var model = Model(("a", Unresolved("<${b}>")), ("b", ScalarPayload.OfBoolean(true)));

        Resolve(model, "a").ToCanonicalText().ShouldBe("<true>");
    }

    /// <summary>
    /// Section 13.2 interpolates a decimal as "exactly the canonical decimal algorithm in Section
    /// 18", not as the text the referent was written with.
    /// </summary>
    [Test]
    public void ADecimalInterpolatesInItsCanonicalSpelling() =>
        Render("a.k=<${a.m}>\na.m=1.50\n").ShouldBe("k=<1.5>\nm=1.5\n");

    // ---- 13.3 Non-scalar and free-wildcard references -----------------------------------------

    /// <summary>
    /// Section 13.3: "Free wildcard references such as <c>${a.*}</c> are blocking errors."
    /// </summary>
    /// <remarks>
    /// Written with an explicit capture, because Appendix A.4 does not admit a bare <c>*</c> in a
    /// reference name at all and step 6 refuses it as syntax. A <c>*[n]</c> that the owning name
    /// never defines is the form that survives to step 15 with a wildcard still in it, and is
    /// therefore the one Section 14.4 can call reachable or not.
    /// </remarks>
    [Test]
    public void AFreeWildcardReferenceIsRefused() =>
        Failure("a.k=${a.*[n]}\na.t=v\n").ShouldBe("REFERENCE001");

    /// <summary>
    /// Section 13.3: "A reference inside a wildcard template may contain only explicit captures
    /// already bound by that same template. After capture substitution, the resulting reference
    /// must contain no wildcard."
    /// </summary>
    [Test]
    public void AReferenceInsideATemplateResolvesAfterCaptureSubstitution() =>
        Render("a.x.v=1\na.y.v=2\na.*[n].k=${a.*[n].v}\n")
            .ShouldBe("x.v=1\nx.k=1\ny.v=2\ny.k=2\n");

    // ---- 14.4 Reachability --------------------------------------------------------------------

    /// <summary>
    /// Section 14.4: "Missing, cyclic, ambiguous, free-wildcard, and non-scalar references in
    /// entries unreachable from every concrete output instance do not fail the run."
    /// </summary>
    [TestCase("unused.k=${nowhere}\n", TestName = "AnUnreachableMissingReferenceIsTolerated")]
    [TestCase("unused.k=${unused.k}\n", TestName = "AnUnreachableCycleIsTolerated")]
    [TestCase("unused.k=${unused.*[n]}\n", TestName = "AnUnreachableFreeWildcardIsTolerated")]
    [TestCase(
        "unused.k=${unused.t.x}\nunused.t.@x=v\nunused.t.Q{u}x=w\n",
        TestName = "AnUnreachableAmbiguousAliasIsTolerated")]
    public void AnUnreachableDefectDoesNotFailTheRun(string extra) =>
        Render("a.k=v\n" + extra).ShouldBe("k=v\n");

    /// <summary>
    /// Section 14.4: "Selected entries and their transitive reference closure are resolved
    /// strictly." Reaching a broken entry through a reference makes its defect this run's problem.
    /// </summary>
    [Test]
    public void ADefectReachedThroughAReferenceIsNotTolerated() =>
        Failure("a.k=${unused.k}\nunused.k=${nowhere}\n").ShouldBe("REFERENCE002");

    /// <summary>
    /// Section 22 counts <c>REFERENCE002</c> "once per reachable owning value", so two entries
    /// referring to the same missing target report twice and one entry reached twice reports once.
    /// </summary>
    [Test]
    public void TheMissingReferenceIsCountedPerOwningValue()
    {
        Codes("a.k=${nowhere}\na.m=${nowhere}\n")
            .ShouldBe(["REFERENCE002", "REFERENCE002"]);

        Codes("a.k=${a.shared}\na.m=${a.shared}\na.shared=${nowhere}\n")
            .ShouldBe(["REFERENCE002"]);
    }

    // ---- Harness ------------------------------------------------------------------------------

    private static OrdinaryPart Ordinary(string text) => new([new LiteralToken(text)]);

    private static ScalarPayload Unresolved(string text)
    {
        var lexed = ValueLexer.Lex(text, ValueSyntax.Profile(WildcardSyntax.None));

        lexed.Fault.ShouldBeNull();

        return ScalarPayload.Unresolved(
            lexed.Value.ShouldNotBeNull(), new ValueOrigin("m", 1, 1));
    }

    private static OverlayNode Model(params (string Name, ScalarPayload Payload)[] entries)
    {
        var root = OverlayNode.Intermediate(StableOrderingKey.First);

        for (var i = 0; i < entries.Length; i++)
        {
            root = root.WithChild(
                Ordinary(entries[i].Name),
                OverlayNode.OfPayload(entries[i].Payload, StableOrderingKey.First));
        }

        return root;
    }

    private static ScalarPayload Resolve(OverlayNode model, string root)
    {
        var diagnostics = new DiagnosticBuffer();
        var resolved = ReferenceResolver.Resolve(
            model, [ImmutableArray.Create<NamePart>(Ordinary(root))], diagnostics);

        diagnostics.Drain().Select(d => d.Code).ShouldBeEmpty();

        return resolved.Children[Ordinary(root)].Payload.ShouldNotBeNull();
    }

    private static ScalarKind ResolveKind(string value, ScalarPayload referent) =>
        Resolve(Model(("a", Unresolved(value)), ("b", referent)), "a").Kind;

    private static string Render(string document)
    {
        var (result, sink) = Transform(document);

        result.ExitCode.ShouldBe(
            0,
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code} {d.Message}")));

        return sink.Written["a.properties"];
    }

    private static ImmutableArray<Diagnostic> Diagnostics(string document)
    {
        var (result, sink) = Transform(document);

        sink.Written.ShouldBeEmpty();

        return result.Diagnostics;
    }

    private static ImmutableArray<string> Codes(string document) =>
        [.. Diagnostics(document).Select(d => d.Code)];

    private static string Failure(string document) =>
        Codes(document).ShouldHaveSingleItem();

    private static (TransformationResult Result, TransformationTests.Sink Sink) Transform(
        string document)
    {
        var sink = new TransformationTests.Sink();
        var sources = new TransformationTests.Sources(
            ("in.txt", document), ("scheme.txt", "a.output=namespace\n"));

        return (
            TransformationTests.Run(sink, sources, "-i", "in.txt", "-s", "scheme.txt"),
            sink);
    }
}
