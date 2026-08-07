using System.Collections.Immutable;
using Namespace2Xml.Profiles;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// The Section 12.1 legacy captures, Section 12.2 explicit captures, and Section 12.3 matching
/// scope.
/// </summary>
[TestFixture]
public sealed class WildcardMatchTests
{
    private static QualifiedName Lex(string text)
    {
        var result = QualifiedNameLexer.Lex(text);
        result.Fault.ShouldBeNull();
        return result.Name!;
    }

    /// <summary>
    /// Renders a match outcome as one string so a test can state the whole expected partition:
    /// a nonmatch as <c>no</c>, a match binding nothing as <c>yes</c>, positional captures as
    /// bracketed texts in order, named captures as <c>id=[text]</c> pairs in identifier order.
    /// </summary>
    /// <remarks>
    /// The brackets are load-bearing. Rendering a capture as its bare text makes one capture of
    /// empty text indistinguishable from no captures at all, which is precisely the outcome
    /// Section 12.1's "zero or more" case has to be able to state.
    /// </remarks>
    private static string Match(string pattern, string concrete)
    {
        if (!WildcardMatch.TryMatch(Lex(pattern), Lex(concrete), out var captures))
        {
            return "no";
        }

        var positional = string.Concat(captures.Positional.Select(text => "[" + text + "]"));
        var named = string.Join(
            ",",
            captures.Named.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + "=[" + pair.Value + "]"));

        return (positional, named) switch
        {
            ("", "") => "yes",
            (_, "") => positional,
            ("", _) => named,
            _ => positional + " " + named,
        };
    }

    // Section 12.1: "An unescaped '*' matches zero or more characters within one qualified-name
    // part."

    [Test]
    public void AWildcardMatchesSeveralCharacters() =>
        Match("a.*", "a.xyz").ShouldBe("[xyz]");

    [Test]
    public void AWildcardMatchesOneCharacter() =>
        Match("a.*", "a.x").ShouldBe("[x]");

    /// <summary>
    /// "Zero or more" includes zero, so a pattern whose literals already consume the whole part
    /// still matches and binds empty text.
    /// </summary>
    [Test]
    public void AWildcardMatchesNoCharacters() =>
        Match("a.x*y", "a.xy").ShouldBe("[]");

    // Section 12.1: "matching is anchored to the complete part".

    [Test]
    public void ALeadingLiteralIsAnchored() =>
        Match("a.x*", "a.yx").ShouldBe("no");

    [Test]
    public void ATrailingLiteralIsAnchored() =>
        Match("a.*x", "a.xy").ShouldBe("no");

    [Test]
    public void AWildcardFreePatternMatchesOnlyItself() =>
        Match("a.b", "a.b").ShouldBe("yes");

    [Test]
    public void AWildcardFreePatternRejectsOtherText() =>
        Match("a.b", "a.c").ShouldBe("no");

    // Section 12.1: "Captures are assigned left to right, each taking the shortest text that still
    // permits the remaining pattern to match. Implementations must produce this partition
    // independently of regular-expression greediness."

    /// <summary>
    /// The clause's whole point, stated as a case where greedy and shortest disagree. A greedy
    /// engine partitions <c>a-b-c</c> as <c>a-b</c> then <c>c</c>; Section 12.1 requires <c>a</c>
    /// then <c>b-c</c>.
    /// </summary>
    [Test]
    public void TheFirstCaptureTakesTheShortestText() =>
        Match("p.*-*", "p.a-b-c").ShouldBe("[a][b-c]");

    /// <summary>
    /// The same disagreement with an overlapping literal: greedy binds <c>aa</c> then empty, and
    /// shortest binds empty then <c>aa</c>.
    /// </summary>
    [Test]
    public void AnOverlappingLiteralTakesItsEarliestOccurrence() =>
        Match("p.*a*", "p.aaa").ShouldBe("[][aa]");

    /// <summary>
    /// Three captures, so the rule has to hold for a capture that is neither first nor last.
    /// </summary>
    [Test]
    public void EveryCaptureTakesTheShortestText() =>
        Match("p.*-*-*", "p.a-b-c-d-e").ShouldBe("[a][b][c-d-e]");

    /// <summary>
    /// A trailing literal is end-anchored, so the last capture is what remains before it rather
    /// than what stops at the literal's earliest occurrence.
    /// </summary>
    [Test]
    public void ATrailingLiteralAnchorsTheLastCapture() =>
        Match("p.*-end", "p.a-b-end").ShouldBe("[a-b]");

    /// <summary>
    /// A pattern that needs two occurrences of a literal does not match text holding one, even
    /// though the earliest-occurrence walk consumes the only one it has.
    /// </summary>
    [Test]
    public void AnExhaustedAnchorIsANonmatch() =>
        Match("p.*a*a", "p.a").ShouldBe("no");

    // Section 12.1: "Legacy unnamed captures are substituted positionally."

    [Test]
    public void UnnamedCapturesAreOrderedLeftToRight() =>
        Match("*.*", "one.two").ShouldBe("[one][two]");

    // Section 12.2: "the same identifier reused in the name must match the same text".

    [Test]
    public void ARepeatedIdentifierBindsOnce() =>
        Match("*[x].*[x]", "a.a").ShouldBe("x=[a]");

    [Test]
    public void ARepeatedIdentifierMatchesWithinOnePart() =>
        Match("p.*[x]-*[x]", "p.ab-ab").ShouldBe("x=[ab]");

    // Section 12.2: "inconsistent repeated captures are nonmatches".

    /// <summary>
    /// A nonmatch, and deliberately not a diagnostic. Section 22 gives <c>WILDCARD001</c> the
    /// condition "invalid, undefined, mixed, or inconsistent capture", but Section 12.2 is the
    /// specific sentence and it says this case simply does not match.
    /// </summary>
    [Test]
    public void AnInconsistentRepeatedIdentifierIsANonmatch() =>
        Match("*[x].*[x]", "a.b").ShouldBe("no");

    [Test]
    public void AnInconsistentRepeatWithinOnePartIsANonmatch() =>
        Match("p.*[x]-*[x]", "p.aa-a").ShouldBe("no");

    [Test]
    public void DistinctIdentifiersBindIndependently() =>
        Match("*[a].*[b]", "one.two").ShouldBe("a=[one],b=[two]");

    // Section 12.3: "A wildcard matches only within one name part and never crosses a namespace
    // delimiter."

    [Test]
    public void AWildcardDoesNotCrossADelimiter() =>
        Match("a.*", "a.b.c").ShouldBe("no");

    [Test]
    public void APatternDoesNotMatchAShorterName() =>
        Match("a.*.c", "a.b").ShouldBe("no");

    // Section 12.3: "A template name matches existing concrete names through its last
    // wildcard-containing name part. Literal suffix parts are appended to generated names."

    [Test]
    public void TheLastWildcardPartIsTheMatchDepth() =>
        WildcardMatch.LastWildcardPart(Lex("a.*.z")).ShouldBe(1);

    [Test]
    public void ALaterWildcardMovesTheMatchDepth() =>
        WildcardMatch.LastWildcardPart(Lex("a.*.b.*")).ShouldBe(3);

    [Test]
    public void AWildcardFreeNameHasNoMatchDepth() =>
        WildcardMatch.LastWildcardPart(Lex("a.b.c")).ShouldBe(-1);

    /// <summary>
    /// The rule <c>a.*.z</c> matches through part 1, so <c>a.x</c> is a candidate however deep the
    /// concrete path continues. Section 12.4 counts candidates at that depth: "eligible items are
    /// the distinct depth-k prefixes of existing paths, not every deeper descendant."
    /// </summary>
    [Test]
    public void APrefixMatchIgnoresDeeperParts()
    {
        var pattern = Lex("a.*.z");

        WildcardMatch.TryMatchPrefix(
            pattern.Parts,
            WildcardMatch.LastWildcardPart(pattern) + 1,
            Lex("a.x.q.r").Parts,
            out var captures).ShouldBeTrue();

        captures.Positional.ShouldBe(ImmutableArray.Create("x"));
    }

    [Test]
    public void APrefixMatchStillRequiresTheLiteralPartsToAgree()
    {
        var pattern = Lex("a.*.z");

        WildcardMatch.TryMatchPrefix(pattern.Parts, 2, Lex("b.x.z").Parts, out _).ShouldBeFalse();
    }

    [Test]
    public void APrefixLongerThanTheNameIsANonmatch()
    {
        var pattern = Lex("a.*.z");

        WildcardMatch.TryMatchPrefix(pattern.Parts, 3, Lex("a.x").Parts, out _).ShouldBeFalse();
    }

    // Section 11.4 typed components, matched strictly. Section 15.2 grants unmarked components an
    // alias index, but grants it to scheme paths; Sections 8.6 and 12 pattern over input data.

    [Test]
    public void AQualifiedElementMatchesItsOwnUri() =>
        Match("Q{u}*", "Q{u}b").ShouldBe("[b]");

    [Test]
    public void AQualifiedElementDoesNotMatchAnotherUri() =>
        Match("Q{u}*", "Q{v}b").ShouldBe("no");

    [Test]
    public void AnOrdinaryPatternDoesNotMatchAQualifiedElement() =>
        Match("*", "Q{u}b").ShouldBe("no");

    [Test]
    public void AnAttributeMatchesAnAttribute() =>
        Match("a.@*", "a.@x").ShouldBe("[x]");

    [Test]
    public void AnAttributeDoesNotMatchAnElement() =>
        Match("a.@*", "a.x").ShouldBe("no");

    [Test]
    public void AnOrdinaryPatternDoesNotMatchAnAttribute() =>
        Match("a.*", "a.@x").ShouldBe("no");

    [Test]
    public void AContentTokenMatchesItsOwnOrdinal() =>
        Match("a.#1", "a.#1").ShouldBe("yes");

    [Test]
    public void AContentTokenDoesNotMatchAnotherOrdinal() =>
        Match("a.#1", "a.#2").ShouldBe("no");

    /// <summary>
    /// A content token carries no <c>ordinary-component</c>, so Appendix A.2 gives a wildcard no
    /// position inside one and an ordinary pattern part cannot stand in for it.
    /// </summary>
    [Test]
    public void AnOrdinaryPatternDoesNotMatchAContentToken() =>
        Match("a.*", "a.#1").ShouldBe("no");

    // Section 8.2: an escaped marker creates an ordinary component, and Section 12.1 makes an
    // escaped asterisk literal text rather than a capture.

    /// <summary>
    /// The concrete name is written with the same escape, because <c>a.*</c> unescaped lexes to a
    /// wildcard rather than to a name containing an asterisk. Were <c>\*</c> to lex as a wildcard,
    /// this would bind a capture of <c>*</c> instead of matching nothing.
    /// </summary>
    [Test]
    public void AnEscapedAsteriskIsLiteralText() =>
        Match("a.\\*", "a.\\*").ShouldBe("yes");

    [Test]
    public void AnEscapedAsteriskDoesNotMatchOtherText() =>
        Match("a.\\*", "a.xyz").ShouldBe("no");
}
