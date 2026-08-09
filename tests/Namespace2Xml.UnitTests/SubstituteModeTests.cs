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
using SchemeAlias = Namespace2Xml.Scheme.SchemeAlias;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 16.7: the <c>substitute</c> directive, and the two columns of its table.
/// </summary>
/// <remarks>
/// Every expectation here is authored from the specification clause named in the test, never from
/// what the reader or the compiler currently produces. The directive <em>disables</em>
/// interpretation rather than performing any, so each test asks what a name or a value became, not
/// what some substitution engine did.
/// </remarks>
[TestFixture]
public class SubstituteModeTests
{
    private DiagnosticBuffer diagnostics = null!;

    [SetUp]
    public void SetUp() => diagnostics = new DiagnosticBuffer();

    private static ImmutableArray<NamespaceRecord> Records(string document) =>
    [
        .. document
            .Split('\n')
            .Select((line, index) => NamespaceRecordClassifier.Classify(line, index + 1)),
    ];

    private static QualifiedName Name(string text) =>
        QualifiedNameLexer.Lex(text).Name.ShouldNotBeNull();

    private static SubstituteModeMap Map(params (string? Pattern, SubstituteMode Mode)[] declared) =>
        SubstituteModeMap.Create(
            declared.Select(d => (d.Pattern is null ? null : Name(d.Pattern), d.Mode)));

    private SubstituteModeMap CompileScheme(string document) =>
        SchemeCompiler.CompileSubstitutes(
            SchemeReader.Read(Records(document), 2, "s.properties", diagnostics).Entries,
            diagnostics);

    private ProfileContribution Read(string document, SubstituteModeMap substitutes) =>
        NamespaceProfileReader.Read(
            Records(document), 3, ProfileSource.OfFile("p.txt"), substitutes, diagnostics);

    private static OrdinaryPart Ordinary(string text) => new([new LiteralToken(text)]);

    private static OverlayNode Descend(OverlayNode node, params string[] path) =>
        path.Aggregate(node, (current, step) => current.Children[Ordinary(step)]);

    // ---- Section 16.7: the four mode names -------------------------------------------------------

    /// <summary>Section 16.7 offers exactly four mode names.</summary>
    [TestCase("All", SubstituteMode.All)]
    [TestCase("Key", SubstituteMode.Key)]
    [TestCase("Value", SubstituteMode.Value)]
    [TestCase("None", SubstituteMode.None)]
    public void EveryModeIsRecognized(string written, SubstituteMode expected)
    {
        SubstituteModes.TryRecognize(written, out var mode, out var alias).ShouldBeTrue();
        mode.ShouldBe(expected);
        alias.ShouldBe(SchemeAlias.None);
    }

    /// <summary>
    /// Section 15 matches "every other name and value in the scheme language" case-insensitively,
    /// and a mode name is a scheme value.
    /// </summary>
    [TestCase("all")]
    [TestCase("NONE")]
    [TestCase("kEy")]
    public void AModeNameIsCaseInsensitive(string written) =>
        SubstituteModes.TryRecognize(written, out _, out _).ShouldBeTrue();

    /// <summary>Section 15.3: <c>keyOnly</c> is a deprecated alias for mode <c>Key</c>.</summary>
    [Test]
    public void TheDeprecatedAliasNamesTheKeyMode()
    {
        SubstituteModes.TryRecognize("keyOnly", out var mode, out var alias).ShouldBeTrue();
        mode.ShouldBe(SubstituteMode.Key);
        alias.ShouldBe(SchemeAlias.KeyOnly);
    }

    /// <summary>
    /// Section 16.7 names four modes and no combination of them. A combined spelling is the one an
    /// enumeration shaped as flags would silently accept, so it is asserted rather than assumed.
    /// </summary>
    [TestCase("Key,Value")]
    [TestCase("keys")]
    [TestCase("")]
    [TestCase("0")]
    public void ASpellingSection167DoesNotHaveIsNotRecognized(string written) =>
        SubstituteModes.TryRecognize(written, out _, out _).ShouldBeFalse();

    // ---- Section 16.7: the table -----------------------------------------------------------------

    /// <summary>
    /// Section 16.7's table, read as its two independent questions. Section 13.4 gives the two
    /// off-diagonal rows their names: under <c>Key</c> "names retain wildcard interpretation" while
    /// values are interpreted in neither references nor wildcards.
    /// </summary>
    [TestCase(SubstituteMode.All, true, true)]
    [TestCase(SubstituteMode.Key, true, false)]
    [TestCase(SubstituteMode.Value, false, true)]
    [TestCase(SubstituteMode.None, false, false)]
    public void TheTableDecidesNamesAndValuesIndependently(
        SubstituteMode mode, bool names, bool values)
    {
        mode.InterpretsNames().ShouldBe(names);
        mode.InterpretsValues().ShouldBe(values);
    }

    // ---- Section 15.2: precedence among declarations ---------------------------------------------

    /// <summary>A scheme that declares no <c>substitute</c> leaves every path interpreted.</summary>
    [Test]
    public void TheDefaultIsAll()
    {
        SubstituteModeMap.Default.IsEmpty.ShouldBeTrue();
        SubstituteModeMap.Default.For(Name("a.b")).ShouldBe(SubstituteMode.All);
    }

    /// <summary>
    /// Section 15.2: "a later matching directive overrides an earlier matching directive".
    /// </summary>
    [Test]
    public void ALaterMatchingDirectiveWins() =>
        Map(("a.b", SubstituteMode.None), ("a.b", SubstituteMode.Value))
            .For(Name("a.b"))
            .ShouldBe(SubstituteMode.Value);

    /// <summary>
    /// Section 15.2: "pattern specificity does not alter precedence". The more specific pattern is
    /// written first here, so a specificity-ordered implementation would return <c>None</c>.
    /// </summary>
    [Test]
    public void SpecificityDoesNotAlterPrecedence() =>
        Map(("a.b", SubstituteMode.None), ("a.*", SubstituteMode.Value))
            .For(Name("a.b"))
            .ShouldBe(SubstituteMode.Value);

    /// <summary>
    /// A directive governs only the node its pattern matches, so an ancestor's declaration does not
    /// reach a descendant. Sections 8.4, 10.1 and 10.2 each speak of a <em>matching</em> directive,
    /// and Section 16.10 is node-scoped explicitly for the sibling input-phase directive.
    /// </summary>
    [Test]
    public void AnAncestorDeclarationDoesNotReachADescendant()
    {
        var map = Map(("a", SubstituteMode.None));

        map.For(Name("a")).ShouldBe(SubstituteMode.None);
        map.For(Name("a.b")).ShouldBe(SubstituteMode.All);
        map.For(Name("a.b.c")).ShouldBe(SubstituteMode.All);
    }

    /// <summary>
    /// Section 12.1: <c>*</c> "matches zero or more characters within one qualified-name part", so
    /// a one-part pattern spans one level and no more.
    /// </summary>
    [Test]
    public void AWildcardPatternSpansExactlyOneLevel()
    {
        var map = Map(("a.*", SubstituteMode.None));

        map.For(Name("a.top")).ShouldBe(SubstituteMode.None);
        map.For(Name("a.b.deep")).ShouldBe(SubstituteMode.All);
        map.For(Name("a")).ShouldBe(SubstituteMode.All);
    }

    /// <summary>
    /// Section 16.7 writes the path as <c>[path.]substitute</c>. Under node scoping the pathless
    /// form can only mean the run-wide default: Appendix A.3 gives every value a name of at least
    /// one component, so a directive governing the overlay root alone would govern nothing.
    /// Section 16.8 sets the same precedent one subsection earlier.
    /// </summary>
    [Test]
    public void ThePathlessFormGovernsEveryPath()
    {
        var map = Map((null, SubstituteMode.None));

        map.For(Name("a")).ShouldBe(SubstituteMode.None);
        map.For(Name("a.b.c.d")).ShouldBe(SubstituteMode.None);
    }

    /// <summary>A pathless declaration is overridden by a later path-bearing one, and the reverse.</summary>
    [Test]
    public void ThePathlessFormTakesPartInSourceOrderLikeAnyOther()
    {
        Map((null, SubstituteMode.None), ("a.b", SubstituteMode.All))
            .For(Name("a.b"))
            .ShouldBe(SubstituteMode.All);

        Map(("a.b", SubstituteMode.All), (null, SubstituteMode.None))
            .For(Name("a.b"))
            .ShouldBe(SubstituteMode.None);
    }

    /// <summary>
    /// A value with no path of its own — a native document whose root is a scalar — can only be
    /// named by the pathless form, because there is nothing for a pattern to match component for
    /// component.
    /// </summary>
    [Test]
    public void APathlessValueIsNamedOnlyByThePathlessForm()
    {
        Map(("*", SubstituteMode.None)).For(null).ShouldBe(SubstituteMode.All);
        Map((null, SubstituteMode.None)).For(null).ShouldBe(SubstituteMode.None);
    }

    /// <summary>
    /// A declared path that is itself a template is matched by its spelling, so a pattern naming a
    /// path the template only reaches after expansion does not govern it. Section 15.1 step 6 says
    /// "an entry's declared pre-expansion path", and the 2.x implementation matched a component's
    /// rendered text for the same reason.
    /// </summary>
    [Test]
    public void ATemplateIsMatchedByItsSpelling()
    {
        var map = Map(("a.*.b", SubstituteMode.None));

        map.For(Name("a.*.b")).ShouldBe(SubstituteMode.None);
        map.For(Name("a.x.b")).ShouldBe(SubstituteMode.None);

        Map(("a.x.b", SubstituteMode.None)).For(Name("a.*.b")).ShouldBe(SubstituteMode.All);
    }

    // ---- Section 15.1 step 3: compiling the directive ---------------------------------------------

    /// <summary>Step 3 compiles the directive before step 6 lexes the values it governs.</summary>
    [Test]
    public void AWellFormedDirectiveCompiles()
    {
        var map = CompileScheme("a.b.substitute=None");

        diagnostics.Drain().ShouldBeEmpty();
        map.For(Name("a.b")).ShouldBe(SubstituteMode.None);
    }

    /// <summary>Section 16.7 admits four names, so anything else is a scheme error.</summary>
    [Test]
    public void AnUnrecognizedModeIsScheme001()
    {
        CompileScheme("a.substitute=sometimes");

        var diagnostic = diagnostics.Drain().ShouldHaveSingleItem();

        diagnostic.Code.ShouldBe("SCHEME001");
        diagnostic.Spec.ShouldBe("\u00A716.7");
    }

    /// <summary>
    /// Section 15.3 accepts the deprecated alias and warns "once per alias category and scheme", so
    /// two occurrences in one scheme are one warning.
    /// </summary>
    [Test]
    public void TheDeprecatedAliasWarnsOncePerScheme()
    {
        var map = CompileScheme("a.substitute=keyOnly\nb.substitute=keyOnly");

        map.For(Name("a")).ShouldBe(SubstituteMode.Key);
        map.For(Name("b")).ShouldBe(SubstituteMode.Key);

        var diagnostic = diagnostics.Drain().ShouldHaveSingleItem();

        diagnostic.Code.ShouldBe("WARN002");
        diagnostic.Spec.ShouldBe("\u00A715.3");
    }

    /// <summary>
    /// Step 3 compiles the directive, so step 16 must not find it uncompiled and refuse the run.
    /// </summary>
    [Test]
    public void ACompiledDirectiveIsNotDeferred() =>
        SchemeCompiler.Compile(
            SchemeReader.Read(Records("a.substitute=None"), 2, "s.properties", diagnostics).Entries,
            diagnostics)
            .Deferred.ShouldBeEmpty();

    // ---- Section 16.7 applied to a namespace profile ---------------------------------------------

    /// <summary>
    /// Section 13.4: under <c>None</c> a value is interpreted in neither references nor wildcards,
    /// so <c>${b}</c> is the six characters that spell it.
    /// </summary>
    [Test]
    public void UnderNoneAReferenceIsOrdinaryText()
    {
        var contribution = Read("a=${b}", Map(("a", SubstituteMode.None)));

        var payload = Descend(contribution.Overlay, "a").Payload.ShouldNotBeNull();

        payload.IsUnresolved.ShouldBeFalse();
        payload.ToCanonicalText().ShouldBe("${b}");
    }

    /// <summary>
    /// Under <c>Value</c> the value is still interpreted, so the same entry is unresolved. Written
    /// alongside the previous test because a mode that disabled everything would pass that one.
    /// </summary>
    [Test]
    public void UnderValueAReferenceIsStillARerefence()
    {
        var contribution = Read("a=${b}", Map(("a", SubstituteMode.Value)));

        Descend(contribution.Overlay, "a").Payload.ShouldNotBeNull().IsUnresolved.ShouldBeTrue();
    }

    /// <summary>
    /// Under a mode that does not interpret names, an asterisk in a name is a literal part rather
    /// than a Section 12 template, so the entry lands in the concrete overlay at the path as
    /// written.
    /// </summary>
    [TestCase(SubstituteMode.Value)]
    [TestCase(SubstituteMode.None)]
    public void UnderAModeThatDoesNotInterpretNamesAnAsteriskIsLiteralText(SubstituteMode mode)
    {
        var contribution = Read("a.*.b=1", Map(("a.*.b", mode)));

        contribution.Templates.ShouldBeEmpty();
        Descend(contribution.Overlay, "a", "*", "b").Payload
            .ShouldNotBeNull().ToCanonicalText().ShouldBe("1");
    }

    /// <summary>
    /// Section 13.4: under <c>Key</c> "names retain wildcard interpretation", so the same entry is
    /// a template.
    /// </summary>
    [TestCase(SubstituteMode.All)]
    [TestCase(SubstituteMode.Key)]
    public void UnderAModeThatInterpretsNamesAnAsteriskIsATemplate(SubstituteMode mode)
    {
        var contribution = Read("a.*.b=1", Map(("a.*.b", mode)));

        contribution.Templates.ShouldHaveSingleItem();
        contribution.Overlay.Children.ShouldBeEmpty();
    }

    /// <summary>
    /// Section 12.1 recognizes a value wildcard "from the owning name's captures", so a name whose
    /// captures were not interpreted leaves an asterisk in the value as text too. The two halves of
    /// Section 16.7's table cannot disagree.
    /// </summary>
    [Test]
    public void UnderValueAnUninterpretedNameLeavesNoCapturesToSubstitute()
    {
        var contribution = Read("a.*.b=*", Map(("a.*.b", SubstituteMode.Value)));

        Descend(contribution.Overlay, "a", "*", "b").Payload
            .ShouldNotBeNull().ToCanonicalText().ShouldBe("*");
    }

    /// <summary>
    /// The explicit capture form keeps its brackets when it is literalized: Section 12.1 spells an
    /// uninterpreted capture in a value the same way, and dropping them would silently rename the
    /// node.
    /// </summary>
    [Test]
    public void AnExplicitCaptureIsLiteralizedWithItsBrackets()
    {
        var contribution = Read("a.*[i].b=1", Map(("a.*[i].b", SubstituteMode.None)));

        Descend(contribution.Overlay, "a", "*[i]", "b").Payload
            .ShouldNotBeNull().ToCanonicalText().ShouldBe("1");
    }

    /// <summary>
    /// Section 15.1 step 6 resolves the mode against "an entry's declared pre-expansion path", so a
    /// directive naming a path an entry only reaches after expansion does not govern it.
    /// </summary>
    [Test]
    public void TheModeIsResolvedAgainstTheDeclaredPath()
    {
        var contribution = Read("a.*.b=${c}", Map(("a.x.b", SubstituteMode.None)));

        contribution.Templates.ShouldHaveSingleItem()
            .Value.ContainsReference.ShouldBeTrue();
    }

    // ---- Section 13.4 applied to a native document -----------------------------------------------

    private OverlayNode Project(string document, SubstituteModeMap substitutes)
    {
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
            substitutes,
            diagnostics,
            out var unsupported);

        unsupported.ShouldBeNull();

        return contribution.Overlay;
    }

    /// <summary>
    /// Section 13.4: "Native JSON, YAML, and XML strings matched by <c>Key</c> or <c>None</c> are
    /// preserved exactly after native format decoding; no transformer escape decoding is applied."
    /// The backslash survives, so the value is not merely lexed with interpretation switched off.
    /// </summary>
    [TestCase(SubstituteMode.Key)]
    [TestCase(SubstituteMode.None)]
    public void ANativeStringIsPreservedExactlyUnderKeyAndNone(SubstituteMode mode) =>
        Descend(Project("{\"a\":\"\\\\*\"}", Map(("a", mode))), "a").Payload
            .ShouldNotBeNull().ToCanonicalText().ShouldBe("\\*");

    /// <summary>
    /// Under a mode that interprets values the same document decodes the Appendix A.5 escape, which
    /// is what the previous test asserts does not happen.
    /// </summary>
    [TestCase(SubstituteMode.All)]
    [TestCase(SubstituteMode.Value)]
    public void ANativeStringIsDecodedUnderAllAndValue(SubstituteMode mode) =>
        Descend(Project("{\"a\":\"\\\\*\"}", Map(("a", mode))), "a").Payload
            .ShouldNotBeNull().ToCanonicalText().ShouldBe("*");

    /// <summary>
    /// A native document whose root is a scalar has no path, so only the pathless form can name it.
    /// </summary>
    [Test]
    public void ANativeRootScalarIsGovernedByThePathlessForm() =>
        Project("\"\\\\*\"", Map((null, SubstituteMode.None))).Payload
            .ShouldNotBeNull().ToCanonicalText().ShouldBe("\\*");
}
