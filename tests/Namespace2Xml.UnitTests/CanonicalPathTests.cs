using System.Collections.Immutable;
using Namespace2Xml.Profiles;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 6.4.3's diagnostic <c>path</c> member and Section 17.5's fold tie-breaker, both of which
/// spell a name outside a physical record.
/// </summary>
[TestFixture]
public class CanonicalPathTests
{
    private static OrdinaryPart Ordinary(string text) => new([new LiteralToken(text)]);

    private static QualifiedName Name(params NamePart[] parts) =>
        new([.. parts]);

    [Test]
    public void AnEmptyPathHasNoSpelling()
    {
        CanonicalPath.Of((QualifiedName?)null).ShouldBeNull();
        CanonicalPath.Of(ImmutableArray<NamePart>.Empty).ShouldBeNull();
    }

    [Test]
    public void AnOrdinaryPathSpellsAsItsRecordForm() =>
        CanonicalPath.Of(Name(Ordinary("a"), Ordinary("b"))).ShouldBe("a.b");

    /// <summary>
    /// Section 11.4 addresses a mixed-content node as <c>#n</c>, and a view rooted at its parent
    /// makes that the leading part. Nothing here emits a record, so Section 19.1's comment-record
    /// concern does not arise and the path must still be nameable — a diagnostic about
    /// <c>#0</c> that cannot say <c>#0</c> reports nothing usable.
    /// </summary>
    [Test]
    public void ALeadingContentTokenIsStillSpelled() =>
        CanonicalPath.Of(Name(new ContentPart(0), Ordinary("a"))).ShouldBe("#0.a");

    /// <summary>
    /// Section 19.1 gives an unpaired surrogate no escape, so this name genuinely has no canonical
    /// spelling and the approximate fallback is taken. It must still name the parts it can: the
    /// record's own <c>ToString</c> prints the backing array's type name, which locates nothing and
    /// tells a reader only that the tool failed to say what it meant.
    /// </summary>
    [Test]
    public void AnUnspellableNameFallsBackToPartsRatherThanARecordDump()
    {
        var text = CanonicalPath.Of(
            Name(Ordinary("a"), new QualifiedElementPart("u\uDC00", [new LiteralToken("x")])));

        text.ShouldNotBeNull();
        text.ShouldStartWith("a.");
        text.ShouldNotContain("ImmutableArray", Case.Sensitive);
        text.ShouldNotContain("QualifiedName {", Case.Sensitive);
    }

    /// <summary>
    /// Two names that differ only inside an unspellable URI must not spell the same. Section 6.4.3
    /// takes this text as a diagnostic's <c>path</c>, and the buffer takes it as the cardinality
    /// slot for a code scoped to a path, so a fallback that collapses them does not merely say
    /// less: it drops the second path's diagnostic as a duplicate of the first.
    /// </summary>
    /// <param name="first">One URI.</param>
    /// <param name="second">Another URI differing only within the part that has no spelling.</param>
    /// <remarks>
    /// U+00AD is <c>Cf</c>, which Section 19.1 forbids, and Section 11.4 admits only <c>\}</c> and
    /// <c>\\</c> inside <c>Q{...}</c> — so neither name can be spelled and both take the fallback.
    /// </remarks>
    [TestCase("urn:x\u00ad1", "urn:x\u00ad2")]
    [TestCase("urn:\u00ada", "urn:\u00adb")]
    [TestCase("urn:a\u00ad", "urn:a\u0000")]
    public void TwoUnspellableNamesDoNotSpellTheSame(string first, string second)
    {
        var one = CanonicalPath.Of(Name(new QualifiedElementPart(first, [new LiteralToken("x")])));
        var other = CanonicalPath.Of(Name(new QualifiedElementPart(second, [new LiteralToken("x")])));

        one.ShouldNotBeNull();
        one.ShouldNotBe(other);
    }

    /// <summary>
    /// The same requirement for the two lone surrogates, which are the other half of Section 19.1's
    /// unspellable set.
    /// </summary>
    /// <remarks>
    /// These cannot be <c>[TestCase]</c> arguments: an attribute argument is stored in metadata as
    /// UTF-8, which cannot encode a lone surrogate, so the compiler substitutes U+FFFD and both
    /// arguments arrive equal. The strings are therefore built here, where the literals survive.
    /// </remarks>
    [Test]
    public void ALoneHighSurrogateDoesNotSpellAsALoneLowOne()
    {
        var high = CanonicalPath.Of(Name(new QualifiedElementPart("u\ud800", [new LiteralToken("x")])));
        var low = CanonicalPath.Of(Name(new QualifiedElementPart("u\udc00", [new LiteralToken("x")])));

        high.ShouldBe("Q{u\\u{D800}}x");
        low.ShouldBe("Q{u\\u{DC00}}x");
    }

    /// <summary>
    /// An approximation must not collide with a name Section 19.1 can spell either, or a path that
    /// reports normally would share a cardinality slot with one that does not.
    /// </summary>
    /// <remarks>
    /// The spellable URI is the six literal characters <c>\u{AD}</c>. Section 19.1 escapes a
    /// backslash inside <c>Q{...}</c> as <c>\\</c> and a closing brace as <c>\}</c>, so it spells as
    /// <c>\\u{AD\}</c> — differing from the approximation of a real U+00AD in both positions.
    /// </remarks>
    [Test]
    public void AnApproximationDoesNotCollideWithASpellableName()
    {
        var approximated = CanonicalPath.Of(
            Name(new QualifiedElementPart("urn:x\u00ad", [new LiteralToken("b")])));
        var spellable = CanonicalPath.Of(
            Name(new QualifiedElementPart("urn:x\\u{AD}", [new LiteralToken("b")])));

        approximated.ShouldBe("Q{urn:x\\u{AD}}b");
        spellable.ShouldBe("Q{urn:x\\\\u{AD\\}}b");
    }

    /// <summary>
    /// The parts of an unspellable name that do have a spelling keep it, so the diagnostic still
    /// points somewhere a reader can act on.
    /// </summary>
    [Test]
    public void AnApproximationKeepsTheSpellingOfEveryOtherPart() =>
        CanonicalPath.Of(
                Name(
                    Ordinary("a.b"),
                    new QualifiedElementPart("urn:\u0085", [new LiteralToken("c")]),
                    Ordinary("d")))
            .ShouldBe("a\\u{2E}b.Q{urn:\\u{85}}c.d");
}
