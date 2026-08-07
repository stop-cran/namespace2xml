using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Output;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Spelling a flat path as key text under Sections 19.1, 19.2, and 19.6, and the Section 16.4
/// post-projection collision rule.
/// </summary>
/// <remarks>
/// Every expectation here is authored from the specification clause named in the test, never from
/// what the projector currently produces.
/// </remarks>
[TestFixture]
public class FlatKeyProjectorTests
{
    private DiagnosticBuffer diagnostics = null!;

    [SetUp]
    public void SetUp() => diagnostics = new DiagnosticBuffer();

    private static OrdinaryPart Ordinary(string text) => new([new LiteralToken(text)]);

    private static FlatEntry Entry(params string[] parts)
    {
        ImmutableArray<NamePart> path = [.. parts.Select(Ordinary)];

        return new FlatEntry(path, path, ScalarPayload.Untyped("v"), []);
    }

    private static FlatEntry Typed(params NamePart[] parts)
    {
        var path = ImmutableArray.Create(parts);

        return new FlatEntry(path, path, ScalarPayload.Untyped("v"), []);
    }

    private ImmutableArray<FlatKeyedEntry> Project(
        FlatFormat format, params FlatEntry[] entries) =>
        Project(format, FlatKeyProjector.DefaultDelimiter(format), entries);

    private ImmutableArray<FlatKeyedEntry> Project(
        FlatFormat format, string delimiter, params FlatEntry[] entries) =>
        new FlatKeyProjector(format, delimiter, diagnostics, new DestinationRef("out.txt", 0)).Project(entries);

    private string SoleCode() => diagnostics.Drain().ShouldHaveSingleItem().Code;

    // Section 16.4: the default delimiters.

    /// <summary>
    /// Section 16.4 fixes one default per family: namespace <c>.</c>, quoted namespace <c>_</c>,
    /// INI nested section path <c>:</c>.
    /// </summary>
    [Test]
    public void EachFormatHasItsOwnDefaultDelimiter()
    {
        FlatKeyProjector.DefaultDelimiter(FlatFormat.Namespace).ShouldBe(".");
        FlatKeyProjector.DefaultDelimiter(FlatFormat.QuotedNamespace).ShouldBe("_");
        FlatKeyProjector.DefaultDelimiter(FlatFormat.Ini).ShouldBe(":");
    }

    // Section 19.1: namespace keys.

    /// <summary>Section 19.1 emits <c>qualified.name=value</c>, joined at the delimiter.</summary>
    [Test]
    public void ANamespaceKeyJoinsPartsAtTheDelimiter()
    {
        Project(FlatFormat.Namespace, Entry("a", "b"))
            .ShouldHaveSingleItem().Key.ShouldBe("a.b");
    }

    /// <summary>
    /// Section 16.4: a delimiter occurrence inside a part is escaped as <c>\u{HEX}</c>, so "the
    /// default delimiter therefore emits <c>\u{2E}</c>, not <c>\.</c>". Without this a part
    /// containing a dot would read back as two parts.
    /// </summary>
    [Test]
    public void ANamespaceKeyEscapesADelimiterInsideAPart()
    {
        Project(FlatFormat.Namespace, Entry("a.b", "c"))
            .ShouldHaveSingleItem().Key.ShouldBe("a\\u{2E}b.c");
    }

    /// <summary>Section 16.4: the configured delimiter joins the parts and is what gets escaped.</summary>
    [Test]
    public void ANamespaceKeyHonoursAConfiguredDelimiter()
    {
        Project(FlatFormat.Namespace, "/", Entry("a/b", "c"))
            .ShouldHaveSingleItem().Key.ShouldBe("a\\u{2F}b/c");
    }

    /// <summary>
    /// Section 19.1: a namespace name has no spelling when a forbidden scalar sits inside a
    /// <c>Q{...}</c> URI, where "Section 11.4 admits only <c>\}</c> and <c>\\</c>". Serializing it
    /// is blocking <c>SERIALIZE001</c>.
    /// </summary>
    [Test]
    public void AnUnspellableNamespaceNameIsBlocking()
    {
        Project(
            FlatFormat.Namespace,
            Typed(new QualifiedElementPart("u\u0001ri", [new LiteralToken("x")])));

        SoleCode().ShouldBe("SERIALIZE001");
    }

    /// <summary>
    /// Sections 19.1, 19.2, and 19.6 each name a bare scalar by "the final concrete selector part",
    /// and Section 16.3 <c>root</c> may supply one. A scalar reaching serialization with no part at
    /// all has no key, which is a fault rather than an unnamed line.
    /// </summary>
    [Test]
    public void AScalarWithNoPathPartsHasNoKey()
    {
        Project(FlatFormat.Namespace, Entry()).ShouldBeEmpty();

        SoleCode().ShouldBe("SERIALIZE001");
    }

    // Section 19.2: quoted-namespace keys.

    /// <summary>
    /// Section 19.2 keys are shell identifiers "after applying root and delimiter", and
    /// Section 16.4 defaults that delimiter to <c>_</c>.
    /// </summary>
    [Test]
    public void AQuotedNamespaceKeyJoinsPartsWithUnderscore()
    {
        Project(FlatFormat.QuotedNamespace, Entry("a", "b"))
            .ShouldHaveSingleItem().Key.ShouldBe("a_b");
    }

    /// <summary>
    /// Section 19.2: keys must match <c>[A-Za-z_][A-Za-z0-9_]*</c>, so a leading digit is
    /// <c>SHELL001</c>. A shell cannot assign to such a name at all.
    /// </summary>
    [Test]
    public void AQuotedNamespaceKeyRejectsALeadingDigit()
    {
        Project(FlatFormat.QuotedNamespace, Entry("0", "b")).ShouldBeEmpty();

        SoleCode().ShouldBe("SHELL001");
    }

    /// <summary>
    /// Section 16.4: quoted-namespace output applies "its own validation" after joining and has no
    /// escape, so a hyphen inside a part survives into the key and is rejected there.
    /// </summary>
    [Test]
    public void AQuotedNamespaceKeyRejectsAHyphen()
    {
        Project(FlatFormat.QuotedNamespace, Entry("a-b")).ShouldBeEmpty();

        SoleCode().ShouldBe("SHELL001");
    }

    /// <summary>
    /// Section 16.4: "here, identifier normalization means only shell-identifier validation under
    /// Section 19.2 ... It performs no case folding or character replacement". A projector that
    /// replaced the hyphen would satisfy the previous test's key and violate this one.
    /// </summary>
    [Test]
    public void AQuotedNamespaceKeyIsNotRewrittenToFit()
    {
        Project(FlatFormat.QuotedNamespace, Entry("Mixed", "Case"))
            .ShouldHaveSingleItem().Key.ShouldBe("Mixed_Case");
    }

    /// <summary>
    /// Section 16.4: "two distinct logical paths must never silently become one namespace, shell,
    /// or INI key". Quoted namespace has no escape, so <c>a_b.c</c> and <c>a.b_c</c> both join to
    /// <c>a_b_c</c>; the second is blocking <c>FLAT001</c>.
    /// </summary>
    [Test]
    public void TwoPathsJoiningToOneShellIdentifierCollide()
    {
        var keyed = Project(FlatFormat.QuotedNamespace, Entry("a_b", "c"), Entry("a", "b_c"));

        keyed.ShouldHaveSingleItem().Key.ShouldBe("a_b_c");

        var diagnostic = diagnostics.Drain().ShouldHaveSingleItem();

        diagnostic.Code.ShouldBe("FLAT001");
        diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
        diagnostic.Destination.ShouldBe("out.txt");
    }

    /// <summary>
    /// Section 19.1 makes namespace name encoding "total and injective", so the pair that collides
    /// for a shell keeps two distinct keys here. Detection must therefore be per format rather than
    /// a shared pre-check on the paths.
    /// </summary>
    [Test]
    public void TheSamePairDoesNotCollideForNamespaceOutput()
    {
        Project(FlatFormat.Namespace, Entry("a.b", "c"), Entry("a", "b.c")).Length.ShouldBe(2);

        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 16.4 detects collisions "after root application", so the surviving entry is the
    /// first one, keeping the result a function of emission order rather than of dictionary
    /// iteration.
    /// </summary>
    [Test]
    public void ACollisionKeepsTheFirstEntry()
    {
        var keyed = Project(
            FlatFormat.QuotedNamespace,
            new FlatEntry(
                [Ordinary("a_b")], [Ordinary("a_b")], ScalarPayload.Untyped("first"), []),
            new FlatEntry(
                [Ordinary("a"), Ordinary("b")],
                [Ordinary("a"), Ordinary("b")],
                ScalarPayload.Untyped("second"),
                []));

        keyed.ShouldHaveSingleItem().Entry.Payload.ToCanonicalText().ShouldBe("first");
    }

    /// <summary>
    /// A typed XML component has no literal text and Section 16.4 gives quoted namespace no escape,
    /// so there is nothing to join. Emitting the structural form would invent a key.
    /// </summary>
    [Test]
    public void ATypedComponentHasNoShellSpelling()
    {
        Project(FlatFormat.QuotedNamespace, Typed(new AttributePart(Ordinary("a"))))
            .ShouldBeEmpty();

        SoleCode().ShouldBe("SHELL001");
    }

    /// <summary>
    /// <c>SHELL001</c> is scoped "once per projected key and output instance", so two paths that
    /// have no key spelling must each be reported. The buffer keys that scope on the diagnostic's
    /// <c>path</c> text, so a path spelling that collapses distinct paths together silently drops
    /// the second error — the reader fixes one namespace URI and the build fails again on a second
    /// one that was never named.
    /// </summary>
    /// <remarks>
    /// Section 19.1 has no spelling for either URI: U+00AD is <c>Cf</c>, and Section 11.4 admits
    /// only <c>\}</c> and <c>\\</c> inside <c>Q{...}</c>. Both paths therefore take the approximate
    /// spelling, which is exactly where a collapse would occur. The assertion is on the count and on
    /// the two paths differing, not on the approximation itself, which the specification does not
    /// fix.
    /// </remarks>
    [Test]
    public void TwoUnspellablePathsAreEachReported()
    {
        Project(
                FlatFormat.QuotedNamespace,
                Typed(new QualifiedElementPart("urn:x\u00ad1", [new LiteralToken("b")])),
                Typed(new QualifiedElementPart("urn:x\u00ad2", [new LiteralToken("b")])))
            .ShouldBeEmpty();

        var reported = diagnostics.Drain();

        reported.Select(d => d.Code).ShouldBe(["SHELL001", "SHELL001"]);
        reported.Select(d => d.Path).Distinct().Count().ShouldBe(2);
    }

    // Section 19.6: INI keys and sections.

    /// <summary>Section 19.6: "a surviving scalar path with one part becomes a global key".</summary>
    [Test]
    public void AnIniPathOfOnePartIsAGlobalKey()
    {
        var keyed = Project(FlatFormat.Ini, Entry("a")).ShouldHaveSingleItem();

        keyed.Section.ShouldBeEmpty();
        keyed.Key.ShouldBe("a");
    }

    /// <summary>
    /// Section 19.6: "for a path with two or more parts, the final part is the key and every
    /// preceding part is joined with the configured INI delimiter to form the section name".
    /// </summary>
    [Test]
    public void AnIniPathSplitsIntoSectionAndKey()
    {
        var keyed = Project(FlatFormat.Ini, Entry("a", "b", "c")).ShouldHaveSingleItem();

        keyed.Section.ShouldBe("a:b");
        keyed.Key.ShouldBe("c");
    }

    /// <summary>Section 16.4: an explicit delimiter joins the section path instead of <c>:</c>.</summary>
    [Test]
    public void AnIniSectionHonoursAConfiguredDelimiter()
    {
        Project(FlatFormat.Ini, ".", Entry("a", "b", "c"))
            .ShouldHaveSingleItem().Section.ShouldBe("a.b");
    }

    /// <summary>
    /// Section 16.3: "with <c>root=x.y</c>, former global keys are emitted inside section
    /// <c>[x:y]</c>; <c>root</c> parts are section-path parts rather than part of the key text".
    /// The root is already in the path when it arrives here, so the split alone must produce that.
    /// </summary>
    [Test]
    public void RootPartsBecomeSectionPartsRatherThanKeyText()
    {
        var keyed = Project(FlatFormat.Ini, Entry("x", "y", "a")).ShouldHaveSingleItem();

        keyed.Section.ShouldBe("x:y");
        keyed.Key.ShouldBe("a");
    }

    /// <summary>
    /// Section 19.6: names "must match <c>[A-Za-z0-9_.:-]+</c> after delimiter joining", and names
    /// containing whitespace "are errors".
    /// </summary>
    [Test]
    public void AnIniKeyRejectsWhitespace()
    {
        Project(FlatFormat.Ini, Entry("a", "b c")).ShouldBeEmpty();

        SoleCode().ShouldBe("INI001");
    }

    /// <summary>
    /// Section 19.6 applies the same rule to section names, where a bracket would end the section
    /// header early.
    /// </summary>
    [Test]
    public void AnIniSectionRejectsABracket()
    {
        Project(FlatFormat.Ini, Entry("a]b", "c")).ShouldBeEmpty();

        SoleCode().ShouldBe("INI001");
    }

    /// <summary>
    /// Section 19.6 admits <c>:</c> in the pattern, which is what lets the default nested-section
    /// delimiter appear in a joined section name at all.
    /// </summary>
    [Test]
    public void AnIniSectionAdmitsTheJoiningDelimiter()
    {
        Project(FlatFormat.Ini, Entry("a", "b", "c"))
            .ShouldHaveSingleItem().Section.ShouldBe("a:b");
    }

    /// <summary>
    /// Section 19.6: "a scalar at <c>a.x</c> and a descendant at <c>a.x.z</c> may emit key <c>x</c>
    /// in section <c>[a]</c> and key <c>z</c> in section <c>[a:x]</c>", because their projected
    /// identities are distinct. Comparing keys without their sections would reject this.
    /// </summary>
    [Test]
    public void OneKeyInTwoSectionsIsNotACollision()
    {
        var keyed = Project(FlatFormat.Ini, Entry("a", "x"), Entry("a", "x", "z"));

        keyed.Select(entry => (entry.Section, entry.Key)).ShouldBe([("a", "x"), ("a:x", "z")]);
        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 16.4: a genuine post-projection collision is blocking <c>FLAT001</c>. With the
    /// default delimiter a part spelled <c>a:b</c> joins into the same section as the parts
    /// <c>a</c> and <c>b</c>.
    /// </summary>
    [Test]
    public void TwoPathsProjectingToOneSectionAndKeyCollide()
    {
        var keyed = Project(FlatFormat.Ini, Entry("a:b", "c"), Entry("a", "b", "c"));

        keyed.ShouldHaveSingleItem().Section.ShouldBe("a:b");

        SoleCode().ShouldBe("FLAT001");
    }

    /// <summary>
    /// A typed XML component has no literal text, and Section 19.6's name pattern has no way to
    /// carry <c>@</c> either, so the path has no INI spelling.
    /// </summary>
    [Test]
    public void ATypedComponentHasNoIniSpelling()
    {
        Project(FlatFormat.Ini, Typed(new AttributePart(Ordinary("a"))))
            .ShouldBeEmpty();

        SoleCode().ShouldBe("INI001");
    }

    /// <summary>
    /// Section 19.6 orders INI output itself; the projector preserves the Section 19.1 emission
    /// order it is handed so that the serializer can group by section without losing it.
    /// </summary>
    [Test]
    public void ProjectionPreservesEmissionOrder()
    {
        Project(FlatFormat.Ini, Entry("z", "k"), Entry("a", "k"))
            .Select(entry => entry.Section)
            .ShouldBe(["z", "a"]);
    }
}
