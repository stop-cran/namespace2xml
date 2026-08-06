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
}
