using System.Collections.Immutable;
using Namespace2Xml.Overlay;
using Namespace2Xml.Profiles;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 15.2's alias for scheme paths: "an explicitly marked <c>Q{}</c>, <c>@</c>, or <c>#n</c>
/// component selects only that XML component. An unmarked component uses the simple alias index."
/// </summary>
/// <remarks>
/// <para>
/// The corpus pins what a directive binds to end to end. What it cannot pin cheaply is that the
/// grant is <em>per component</em> — the clause says "component", where Section 13.1 says "a
/// reference containing an unescaped … is canonical" and takes the whole path off the index — and a
/// per-path implementation matches every single-component case identically.
/// </para>
/// <para>
/// Every expectation here is authored from Section 15.2 and Section 13.1, never from output.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SchemeAliasTests
{
    private static ImmutableArray<NamePart> Path(string text) =>
        QualifiedNameLexer.Lex(text).Name!.Parts;

    private static string Align(string pattern, string concrete)
    {
        var parts = Path(pattern);

        return CanonicalPath.Of(SchemeAlias.Align(parts, parts.Length, Path(concrete)))
            ?? string.Empty;
    }

    /// <summary>An unmarked component reaches the attribute the alias index offers it.</summary>
    [Test]
    public void AnUnmarkedComponentFoldsAnAttributeToItsLocalName() =>
        Align("a.x", "a.@x").ShouldBe("a.x");

    /// <summary>
    /// Section 13.1's alias "replaces every <c>Q{uri}local</c> or <c>@Q{uri}local</c> part with
    /// <c>local</c>", so a namespaced element and a namespaced attribute fold the same way.
    /// </summary>
    [Test]
    public void AnUnmarkedComponentFoldsAQualifiedNameToItsLocalName()
    {
        Align("a.x", "a.Q{urn:p}x").ShouldBe("a.x");
        Align("a.x", "a.@Q{urn:p}x").ShouldBe("a.x");
    }

    /// <summary>An explicitly marked component "selects only that XML component".</summary>
    [Test]
    public void AMarkedComponentDoesNotFold()
    {
        Align("a.@x", "a.@x").ShouldBe("a.@x");
        Align("a.Q{}x", "a.@x").ShouldBe("a.@x");
        Align("a.Q{urn:p}x", "a.Q{urn:p}x").ShouldBe("a.Q{urn:p}x");
    }

    /// <summary>
    /// Section 15.2 grants the alias per component, so one marked component does not take the rest
    /// of the path off the index — which is where it parts company with Section 13.1.
    /// </summary>
    [Test]
    public void TheGrantIsPerComponentAndNotPerPath()
    {
        Align("a.x.@y", "a.@x.@y").ShouldBe("a.x.@y");
        Align("a.@x.y", "a.@x.@y").ShouldBe("a.@x.y");
    }

    /// <summary>
    /// Section 11.4 holds an element and its sole content token to be "two canonical addresses for
    /// one scalar identity, not two candidates in the simple-alias ambiguity index", and a content
    /// fold changes a path's length, so a scheme path keeps <c>#n</c> literal. See KNOWN-LIMITS.
    /// </summary>
    [Test]
    public void AContentTokenIsNotFolded()
    {
        Align("a.x", "a.#1").ShouldBe("a.#1");
        Align("a.#1", "a.#1").ShouldBe("a.#1");
    }

    /// <summary>
    /// A wildcard does not fold. The alias index maps a written name to the XML components that
    /// name could have meant; a wildcard writes no name, and Section 15.2's remedy for an ambiguous
    /// alias — mark the component and name one outright — is not available to a pattern written to
    /// match both.
    /// </summary>
    [Test]
    public void AWildcardComponentDoesNotFold()
    {
        Align("a.*", "a.@x").ShouldBe("a.@x");
        Align("a.x*", "a.@xy").ShouldBe("a.@xy");
    }

    /// <summary>
    /// A wildcard reaching an attribute and an element of one name is a wildcard matching two
    /// components, which is what it was written to do — not an ambiguous alias. Folding it made
    /// this input blocking.
    /// </summary>
    [Test]
    public void AWildcardOverAClashingNameIsNotAmbiguous()
    {
        var sink = new TransformationTests.Sink();
        var result = TransformationTests.Run(
            sink,
            new TransformationTests.Sources(
                ("in.xml", "<r><a x=\"1\"><x>2</x></a></r>\n"),
                ("scheme.txt", "r.output=xml\nr.root=r\nr.a.*.type=string\n")),
            "-i",
            "in.xml",
            "-s",
            "scheme.txt");

        result.Diagnostics.ShouldNotContain(diagnostic => diagnostic.Code == "SCHEME002");
        sink.Written.ShouldNotBeEmpty();
    }

    /// <summary>A path with nothing to fold is returned unchanged, not rebuilt.</summary>
    [Test]
    public void AnOrdinaryPathIsUntouched()
    {
        var concrete = Path("a.b.c");

        SchemeAlias.Align(Path("a.b.c"), 3, concrete).ShouldBe(concrete);
    }

    /// <summary>
    /// Section 8.6 matches a mask in full and Section 12.3 matches "through its last
    /// wildcard-containing name part", so a caller can align fewer components than the pattern has.
    /// </summary>
    [Test]
    public void OnlyTheCountedPrefixIsFolded()
    {
        var aligned = SchemeAlias.Align(Path("a.x.y"), 2, Path("a.@x.@y"));

        CanonicalPath.Of(aligned).ShouldBe("a.x.@y");
    }

    /// <summary>
    /// Section 15.2 has the diagnostic "list the canonical alternatives", and Section 24 forbids
    /// their order depending on which of them the tree walk reached first.
    /// </summary>
    /// <remarks>
    /// The corpus cannot assert this: Section 6.4.3 makes <c>message</c> "human-readable prose …
    /// never compared", and the alternatives appear nowhere else. A reader who cannot act on the
    /// error without the two addresses is exactly who this diagnostic is for.
    /// </remarks>
    [Test]
    public void TheAmbiguityListsItsAlternativesInOrdinalOrder()
    {
        var sink = new TransformationTests.Sink();
        var result = TransformationTests.Run(
            sink,
            new TransformationTests.Sources(
                ("in.xml", "<r><a x=\"1\"><x>2</x></a></r>\n"),
                ("scheme.txt", "r.output=xml\nr.root=r\nr.a.x.type=string\n")),
            "-i",
            "in.xml",
            "-s",
            "scheme.txt");

        var diagnostic = result.Diagnostics.ShouldHaveSingleItem();

        diagnostic.Code.ShouldBe("SCHEME002");
        diagnostic.Message.ShouldContain("'r.a.@x' and 'r.a.x'", Case.Sensitive);
        sink.Written.ShouldBeEmpty();
    }

    /// <summary>
    /// The alternatives are sorted, not walked. Section 19.1 spells a qualified element
    /// <c>Q{uri}b</c> and a typed attribute <c>@b</c>, and ordinal order puts <c>@</c> first, so a
    /// walk that reaches the qualified element first is where "sorted" and "as encountered" part
    /// company.
    /// </summary>
    /// <remarks>
    /// The input has to be a profile, and it has to declare the qualified element first. Section
    /// 5.2 compares the position mark before the component kind, and an XML element's attributes
    /// are read before its children — so in <c>&lt;a b="1"&gt;&lt;p:b&gt;2&lt;/p:b&gt;&lt;/a&gt;</c>
    /// the attribute already has the earlier mark and the walk agrees with ordinal order by
    /// accident. That version of this test passed with the sort deleted.
    /// </remarks>
    [Test]
    public void TheAlternativesAreSortedRatherThanWalked()
    {
        var sink = new TransformationTests.Sink();
        var result = TransformationTests.Run(
            sink,
            new TransformationTests.Sources(
                ("in.properties", "r.a.Q{urn:p}b=2\nr.a.@b=1\n"),
                ("scheme.txt", "r.output=xml\nr.root=r\nr.a.b.type=string\n")),
            "-i",
            "in.properties",
            "-s",
            "scheme.txt");

        result.Diagnostics.ShouldHaveSingleItem()
            .Message.ShouldContain("'r.a.@b' and 'r.a.Q{urn:p}b'", Case.Sensitive);
    }
}
