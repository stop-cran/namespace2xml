using System.Collections.Immutable;
using Namespace2Xml.Budgets;
using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Inputs;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using Namespace2Xml.Scheme;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 15's structured scheme projection, over the Sections 9.1 and 10.4 key rules.
/// </summary>
[TestFixture]
public class StructuredSchemeReaderTests
{
    private DiagnosticBuffer diagnostics = null!;

    /// <summary>Gives each case its own buffer.</summary>
    [SetUp]
    public void SetUp() => diagnostics = new DiagnosticBuffer();

    /// <summary>
    /// Section 9.1: "Each object-property name becomes one literal qualified-name part." Nesting is
    /// therefore the path, and Section 15's "final qualified-name part identifies a directive"
    /// picks the leaf.
    /// </summary>
    [Test]
    public void NestingIsThePathAndTheLeafIsTheDirective()
    {
        var entries = Read(
            """
            {"app":{"db":{"output":"json","filename":"db.json"}}}
            """).Entries;

        entries.Length.ShouldBe(2);
        entries[0].Directive.ShouldBe(SchemeDirective.Output);
        entries[0].Declaration.ShouldBe("app.db.output");
        CanonicalPath.Of(entries[0].Selector).ShouldBe("app.db");
        entries[1].Directive.ShouldBe(SchemeDirective.Filename);
        entries[1].Value.LiteralText.ShouldBe("db.json");
    }

    /// <summary>
    /// A directive written at the document root selects nothing, exactly as a namespace-profile
    /// scheme's unqualified <c>output=json</c> does.
    /// </summary>
    [Test]
    public void ARootDirectiveHasNoSelector()
    {
        var entries = Read("""{"output":"json"}""").Entries;

        entries.ShouldHaveSingleItem().Selector.ShouldBeNull();
    }

    /// <summary>
    /// Section 15.2 orders directives by source order. A JSON object may put every property on one
    /// line, so the ordering key cannot come from the line: two directives sharing one would make
    /// their override stream depend on which the implementation happened to hold.
    /// </summary>
    [Test]
    public void TwoDirectivesOnOneLineGetDistinctOrderingKeys()
    {
        var entries = Read("""{"a":{"output":"json"},"b":{"output":"yaml"}}""").Entries;

        entries.Length.ShouldBe(2);
        entries[0].Line.ShouldBe(entries[1].Line);
        entries[0].Order.ShouldNotBe(entries[1].Order);
        entries[0].Order.CompareTo(entries[1].Order).ShouldBeLessThan(0);
    }

    /// <summary>
    /// Section 15: "An empty value, null, container value, unknown directive value, or illegal
    /// option/type combination is <c>SCHEME001</c>." Each of the three shapes this reader decides
    /// earns one, and Section 22 counts them once per declaration.
    /// </summary>
    /// <param name="document">The scheme document.</param>
    [TestCase("""{"app":{"output":[1,2]}}""")]
    [TestCase("""{"app":{"output":{}}}""")]
    [TestCase("""{"app":{"output":null}}""")]
    [TestCase("""{"app":{"output":""}}""")]
    public void AValueThatIsNotANonemptyScalarIsScheme001(string document)
    {
        Read(document).Entries.ShouldBeEmpty();

        diagnostics.Drain().ShouldHaveSingleItem().Code.ShouldBe("SCHEME001");
    }

    /// <summary>
    /// Section 15.4 completes "every independent check that does not depend on a failed result", so
    /// one run reports every bad declaration rather than stopping at the first.
    /// </summary>
    [Test]
    public void EveryBadDeclarationIsReported()
    {
        Read("""{"app":{"output":[1],"filename":{},"root":null}}""").Entries.ShouldBeEmpty();

        diagnostics.Drain().Select(d => d.Code)
            .ShouldBe(["SCHEME001", "SCHEME001", "SCHEME001"]);
    }

    /// <summary>
    /// Section 15: "Unknown directives are blocking errors", and a wildcard component spells no
    /// directive name at all.
    /// </summary>
    /// <param name="document">The scheme document.</param>
    [TestCase("""{"app":{"nosuch":"x"}}""")]
    [TestCase("""{"app":{"*":"x"}}""")]
    public void ALeafThatNamesNoDirectiveIsScheme001(string document)
    {
        Read(document).Entries.ShouldBeEmpty();

        diagnostics.Drain().ShouldHaveSingleItem().Code.ShouldBe("SCHEME001");
    }

    /// <summary>
    /// Section 15 requires content that projects to "qualified directive paths". A root scalar or a
    /// root sequence has no path, so it spells no declaration and supplies none to report.
    /// </summary>
    /// <param name="document">The scheme document.</param>
    [TestCase(""" "x" """)]
    [TestCase("""[1,2]""")]
    public void ARootThatIsNotAMappingIsScheme001WithNoDeclaration(string document)
    {
        Read(document).Entries.ShouldBeEmpty();

        var occurrence = diagnostics.Drain().ShouldHaveSingleItem();

        occurrence.Code.ShouldBe("SCHEME001");
        occurrence.Declaration.ShouldBeNull();
        occurrence.Path.ShouldBeNull();
    }

    /// <summary>
    /// Section 15 asks for a scalar value "after format parsing", and Section 18 keeps a typed JSON
    /// scalar in its source kind, so the value the directive receives is that scalar's Section 18
    /// canonical text.
    /// </summary>
    [Test]
    public void ATypedScalarContributesItsCanonicalText()
    {
        var entries = Read("""{"app":{"filename":1,"delimiter":true}}""").Entries;

        entries.Select(entry => entry.Value.LiteralText).ShouldBe(["1", "true"]);
    }

    /// <summary>
    /// Section 9.1: "String scalar values use the same strict reference and value-escape lexer as
    /// namespace values." A native string is therefore lexed, and a reference in it is a reference
    /// rather than the seven literal characters it looks like.
    /// </summary>
    [Test]
    public void ANativeStringValueIsLexed()
    {
        var entry = Read("""{"app":{"filename":"${x}.conf"}}""").Entries.ShouldHaveSingleItem();

        entry.Value.ContainsReference.ShouldBeTrue();
        entry.Value.LiteralText.ShouldBeNull();
    }

    /// <summary>
    /// Section 12.1 decides a directive value's capture form from the selector. A selector with no
    /// wildcard leaves <c>*</c> literal, which is what makes a glob usable as a file name.
    /// </summary>
    [Test]
    public void ASelectorWithoutACaptureLeavesAnAsteriskLiteral()
    {
        var entry = Read("""{"app":{"filename":"x-*.json"}}""").Entries.ShouldHaveSingleItem();

        entry.Value.ContainsWildcard.ShouldBeFalse();
        entry.Value.LiteralText.ShouldBe("x-*.json");
    }

    /// <summary>
    /// Section 9.1 keeps the wildcard meaning inside one key part, so the same value under a
    /// wildcard selector is a capture substitution rather than literal text. This is the pair that
    /// proves the capture form follows the selector and not the value's spelling.
    /// </summary>
    [Test]
    public void ASelectorWithACaptureMakesTheSameAsteriskASubstitution()
    {
        var entry = Read("""{"app":{"*":{"filename":"x-*.json"}}}""").Entries
            .Single(candidate => candidate.Directive == SchemeDirective.Filename);

        entry.Value.ContainsWildcard.ShouldBeTrue();
    }

    /// <summary>
    /// Section 15.3 keeps the deprecated aliases accepted "with one warning per scheme", and the
    /// format the scheme was written in does not change that.
    /// </summary>
    [Test]
    public void ADeprecatedAliasWarnsAndStillBinds()
    {
        var entry = Read("""{"app":{"namespacedelimiter":"/"}}""").Entries.ShouldHaveSingleItem();

        entry.Directive.ShouldBe(SchemeDirective.Delimiter);
        diagnostics.Drain().ShouldHaveSingleItem().Code.ShouldBe("WARN002");
    }

    /// <summary>
    /// Section 15: "A scheme builds no overlay." A mapping that carries properties is a path and
    /// nothing else, so an intermediate level contributes no entry of its own.
    /// </summary>
    [Test]
    public void AnIntermediateMappingContributesNoEntry()
    {
        Read("""{"a":{"b":{"c":{"output":"json"}}}}""").Entries
            .ShouldHaveSingleItem()
            .Declaration.ShouldBe("a.b.c.output");
    }

    private SchemeContribution Read(string document)
    {
        var origin = ProfileSource.OfFile("scheme.json");
        var node = JsonInputReader.Read(
            document,
            ResourceLimits.Defaults,
            new SourceBudget(ResourceLimits.Defaults, 0),
            origin,
            DiagnosticPhase.Scheme,
            diagnostics,
            StableOrderingKey.FromSource(0, 0));

        node.ShouldNotBeNull();

        return StructuredSchemeReader.Read(node, 0, "scheme.json", diagnostics);
    }
}
