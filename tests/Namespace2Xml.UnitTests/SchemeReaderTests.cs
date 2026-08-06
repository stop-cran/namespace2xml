using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using Namespace2Xml.Scheme;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Pipeline step 1: Section 15 reading of a namespace-profile scheme into directives.
/// </summary>
/// <remarks>
/// Every expectation here is authored from the specification clause named in the test, never from
/// what the reader currently produces.
/// </remarks>
[TestFixture]
public class SchemeReaderTests
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

    private SchemeContribution Read(string document) =>
        SchemeReader.Read(Records(document), 2, "s.properties", diagnostics);

    private ImmutableArray<SchemeEntry> Entries(string document) => Read(document).Entries;

    private Diagnostic Only(string document)
    {
        Read(document);

        return diagnostics.Drain().ShouldHaveSingleItem();
    }

    // ---- Section 15: recognizing a directive ------------------------------------------------------

    /// <summary>Section 15: "The final qualified-name part identifies a directive."</summary>
    [Test]
    public void TheFinalNamePartIdentifiesTheDirective()
    {
        var entry = Entries("a.b.output=namespace").ShouldHaveSingleItem();

        entry.Directive.ShouldBe(SchemeDirective.Output);
        entry.Selector!.Parts.Length.ShouldBe(2);
    }

    /// <summary>
    /// Section 16.1 spells a directive with an optional selector, so a directive written with none
    /// binds to the root output instance rather than to a selector named after the directive.
    /// </summary>
    [Test]
    public void ARootLevelDirectiveHasNoSelector() =>
        Entries("output=namespace").ShouldHaveSingleItem().Selector.ShouldBeNull();

    /// <summary>
    /// Section 15 matches directive names under ASCII case-insensitive comparison, as it does every
    /// other name and value in the scheme language.
    /// </summary>
    [TestCase("output=namespace")]
    [TestCase("OUTPUT=namespace")]
    [TestCase("OutPut=namespace")]
    public void ADirectiveNameIsMatchedCaseInsensitively(string document) =>
        Entries(document).ShouldHaveSingleItem().Directive.ShouldBe(SchemeDirective.Output);

    /// <summary>
    /// Section 15 lists eighteen recognized spellings. Recognizing a subset would silently discard
    /// configuration, which Section 15 makes an error precisely so that it cannot happen.
    /// </summary>
    [TestCase("output", SchemeDirective.Output)]
    [TestCase("filename", SchemeDirective.Filename)]
    [TestCase("root", SchemeDirective.Root)]
    [TestCase("delimiter", SchemeDirective.Delimiter)]
    [TestCase("namespacedelimiter", SchemeDirective.Delimiter)]
    [TestCase("key", SchemeDirective.Key)]
    [TestCase("type", SchemeDirective.Type)]
    [TestCase("substitute", SchemeDirective.Substitute)]
    [TestCase("xmloptions", SchemeDirective.XmlOutputOptions)]
    [TestCase("xmlinputoptions", SchemeDirective.XmlInputOptions)]
    [TestCase("xmloutputoptions", SchemeDirective.XmlOutputOptions)]
    [TestCase("jsoninputoptions", SchemeDirective.JsonInputOptions)]
    [TestCase("jsonoutputoptions", SchemeDirective.JsonOutputOptions)]
    [TestCase("yamlinputoptions", SchemeDirective.YamlInputOptions)]
    [TestCase("yamloutputoptions", SchemeDirective.YamlOutputOptions)]
    [TestCase("inioutputoptions", SchemeDirective.IniOutputOptions)]
    [TestCase("merge", SchemeDirective.Merge)]
    [TestCase("filemerge", SchemeDirective.FileMerge)]
    public void EveryRecognizedDirectiveIsRecognized(string name, SchemeDirective expected) =>
        Entries($"{name}=x").ShouldHaveSingleItem().Directive.ShouldBe(expected);

    /// <summary>Section 15: "Unknown directives are blocking errors."</summary>
    [Test]
    public void AnUnknownDirectiveIsRejected()
    {
        var diagnostic = Only("a.outputs=namespace");

        diagnostic.Code.ShouldBe("SCHEME001");
        diagnostic.Message.ShouldContain("outputs");
    }

    /// <summary>
    /// Section 15: "The final qualified-name part identifies a directive." A wildcard token spells
    /// no directive name, so a name ending in one identifies nothing.
    /// </summary>
    [Test]
    public void AWildcardFinalPartIdentifiesNoDirective() =>
        Only("a.*=namespace").Code.ShouldBe("SCHEME001");

    /// <summary>
    /// Section 15.2 admits a wildcard selector, which is expanded at pipeline step 13. Only the
    /// final part has to name a directive.
    /// </summary>
    [Test]
    public void AWildcardSelectorIsRead() =>
        Entries("a.*.output=namespace").ShouldHaveSingleItem().Directive.ShouldBe(SchemeDirective.Output);

    // ---- Section 15: what is not a directive ------------------------------------------------------

    /// <summary>
    /// Section 15: a scheme's content projects to "qualified directive paths and scalar directive
    /// values", and a mask projects to neither.
    /// </summary>
    [Test]
    public void AMaskIsNotADirective()
    {
        var diagnostic = Only("!a.b");

        diagnostic.Code.ShouldBe("SCHEME001");
        diagnostic.Message.ShouldContain("mask");
    }

    /// <summary>Section 8.1 rule 2: a comment is not an entry, and carries no directive.</summary>
    [Test]
    public void ACommentContributesNoDirective()
    {
        Entries("# output=namespace").ShouldBeEmpty();
        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>Section 8.1 rule 5: a record with no separating '=' is a parse error.</summary>
    [Test]
    public void AMalformedRecordIsAParseError() => Only("output").Code.ShouldBe("PARSE001");

    /// <summary>
    /// Section 15: "Every recognized directive requires a nonempty scalar value after format
    /// parsing. An empty value... is SCHEME001."
    /// </summary>
    [Test]
    public void AnEmptyValueIsRejected()
    {
        var diagnostic = Only("output=");

        diagnostic.Code.ShouldBe("SCHEME001");
        diagnostic.Message.ShouldContain("empty");
    }

    /// <summary>
    /// A value that carries a reference is not empty. Section 15.1 step 1 resolves it, and emptiness
    /// is judged from the resolved text rather than from the unresolved spelling.
    /// </summary>
    [Test]
    public void AReferenceValueIsNotEmpty()
    {
        Entries("filename=${a.b}").ShouldHaveSingleItem().Value.ContainsReference.ShouldBeTrue();
        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Diagnostics from a scheme belong to the Section 15.1 steps 1 through 4 phase, whatever the
    /// name or value lexer they came from: the same malformed name in an input file is an input
    /// diagnostic, and the phase is what tells the two apart.
    /// </summary>
    [Test]
    public void EverySchemeDiagnosticIsInTheSchemePhase() =>
        Only("output=").Phase.ShouldBe(DiagnosticPhase.Scheme);

    // ---- Section 15.3: deprecated aliases ---------------------------------------------------------

    /// <summary>
    /// Section 15.3: <c>namespacedelimiter</c> "remains accepted with one warning per scheme", and
    /// resolves to the directive it aliases so nothing downstream sees the spelling.
    /// </summary>
    [Test]
    public void ADeprecatedAliasIsAcceptedAndWarned()
    {
        var entry = Entries("a.namespacedelimiter=_").ShouldHaveSingleItem();

        entry.Directive.ShouldBe(SchemeDirective.Delimiter);

        var diagnostic = diagnostics.Drain().ShouldHaveSingleItem();

        diagnostic.Code.ShouldBe("WARN002");
        diagnostic.Message.ShouldContain("delimiter");
    }

    /// <summary>Section 15.3: <c>xmloptions</c> aliases <c>xmloutputoptions</c>.</summary>
    [Test]
    public void TheXmlOptionsAliasIsAcceptedAndWarned()
    {
        Entries("a.xmloptions=Indent").ShouldHaveSingleItem()
            .Directive.ShouldBe(SchemeDirective.XmlOutputOptions);

        diagnostics.Drain().ShouldHaveSingleItem().Code.ShouldBe("WARN002");
    }

    /// <summary>
    /// The registry gives WARN002 the cardinality "once per alias category and scheme", so a second
    /// use of the same alias adds no warning.
    /// </summary>
    [Test]
    public void OneAliasWarnsOncePerScheme()
    {
        Entries("a.namespacedelimiter=_\nb.namespacedelimiter=:");

        diagnostics.Drain().ShouldHaveSingleItem().Code.ShouldBe("WARN002");
    }

    /// <summary>
    /// "Once per alias category" makes the category the key, so two different aliases warn twice.
    /// One warning for both would leave a reader unable to tell which spelling to change.
    /// </summary>
    [Test]
    public void TwoAliasCategoriesWarnSeparately()
    {
        Entries("a.namespacedelimiter=_\na.xmloptions=Indent");

        diagnostics.Drain().Length.ShouldBe(2);
    }

    /// <summary>
    /// The canonical spelling is not an alias, so it warns about nothing.
    /// </summary>
    [Test]
    public void TheCanonicalSpellingDoesNotWarn()
    {
        Entries("a.delimiter=_");

        diagnostics.Drain().ShouldBeEmpty();
    }

    // ---- Section 15.2: source order ---------------------------------------------------------------

    /// <summary>
    /// Section 15.2 resolves precedence by source order, so the reader must preserve the order it
    /// read directives in and must not group them by selector or by directive.
    /// </summary>
    [Test]
    public void DirectivesAreReadInSourceOrder()
    {
        var entries = Entries("b.output=namespace\na.output=json\nb.filename=x");

        entries.Select(entry => entry.Line).ShouldBe([1, 2, 3]);
    }

    /// <summary>
    /// A scheme builds no overlay: Section 15 projects its content to directive paths, so
    /// <c>a.output</c> and <c>a.output.filename</c> are two independent directives — an
    /// <c>output</c> on selector <c>a</c> and a <c>filename</c> on selector <c>a.output</c> —
    /// rather than a node and its child. Grafting them into a tree would make the second the child
    /// of the first, and the first is not a path at all.
    /// </summary>
    [Test]
    public void ADirectivePathIsNotATreePath()
    {
        var entries = Entries("a.output=namespace\na.output.filename=x");

        entries.Select(entry => entry.Directive)
            .ShouldBe([SchemeDirective.Output, SchemeDirective.Filename]);
        entries[1].Selector!.Parts.Length.ShouldBe(2);
        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 22 requires a source, a line, and a column on a scheme diagnostic, and the column is
    /// measured from where the record's content begins.
    /// </summary>
    [Test]
    public void ASchemeDiagnosticNamesItsPosition()
    {
        Read("output=namespace\nnope=1");

        var diagnostic = diagnostics.Drain().ShouldHaveSingleItem();

        diagnostic.Source.ShouldBe("s.properties");
        diagnostic.Line.ShouldBe(2);
        diagnostic.Declaration.ShouldBe("nope");
    }
    /// <summary>
    /// Section 4.7 keys every diagnostic by its source position, so two faults in one scheme reach
    /// the stream in the order they were written rather than in the order they were detected.
    /// </summary>
    [Test]
    public void SchemeDiagnosticsAreOrderedByLine()
    {
        Read("nosuchdirective=1\nalsomissing=2\nthirdmissing=3");

        diagnostics.Drain().Select(diagnostic => diagnostic.Line).ShouldBe([1, 2, 3]);
    }

    /// <summary>
    /// The registry raises WARN002 "once per alias category and scheme", so a second occurrence of
    /// the same alias is silent and a different alias still warns.
    /// </summary>
    [Test]
    public void EachAliasCategoryWarnsOncePerScheme()
    {
        Read("a.namespacedelimiter=_\nb.namespacedelimiter=-\na.xmloptions=None");

        var warnings = diagnostics.Drain();

        warnings.Length.ShouldBe(2);
        warnings.Select(warning => warning.Line).ShouldBe([1, 3]);
    }
    /// <summary>
    /// Reads one document through a buffer of its own and returns the single diagnostic it earned.
    /// </summary>
    /// <remarks>
    /// <c>Only</c> shares one buffer across every call in a test, and these codes carry a
    /// cardinality key scoped to the source, so two calls comparing two documents would find the
    /// second deduplicated away and compare a diagnostic with itself.
    /// </remarks>
    private static Diagnostic Isolated(string document)
    {
        var buffer = new DiagnosticBuffer();
        SchemeReader.Read(Records(document), 2, "s.properties", buffer);

        return buffer.Drain().ShouldHaveSingleItem();
    }

    /// <summary>
    /// Section 22: "a character outside the Basic Multilingual Plane occupies one column". The
    /// codes a malformed name or value earns do not change because it was written in a scheme, and
    /// neither does the unit its column is counted in.
    /// </summary>
    /// <remarks>
    /// Asserted as an equality between two declarations differing only in one scalar, because
    /// Section 22 fixes the unit a column is counted in and not where within the malformed text the
    /// lexer points. U+1D11E is two UTF-16 code units and 'x' is one, so a column counted in code
    /// units differs by one.
    /// </remarks>
    [Test]
    public void AnAstralScalarInASchemeSelectorOccupiesOneColumn()
    {
        var astral = Isolated("a\U0001D11E.*[].output=v");
        var basicPlane = Isolated("ax.*[].output=v");

        astral.Column.ShouldBe(basicPlane.Column);
    }

    /// <summary>Section 22, for a scalar within a selector that lexes.</summary>
    [Test]
    public void AnAstralScalarInASchemeSelectorDoesNotWidenTheValueColumn()
    {
        var astral = Isolated("a\U0001D11Eb.output=${c");
        var basicPlane = Isolated("axb.output=${c");

        astral.Code.ShouldBe("REFERENCE001");
        astral.Column.ShouldBe(basicPlane.Column);
    }

    /// <summary>Section 22, for a scalar preceding the fault within a scheme directive's value.</summary>
    [Test]
    public void AnAstralScalarBeforeASchemeValueFaultOccupiesOneColumn()
    {
        var astral = Isolated("a.output=\U0001D11E${c");
        var basicPlane = Isolated("a.output=x${c");

        astral.Code.ShouldBe("REFERENCE001");
        astral.Column.ShouldBe(basicPlane.Column);
    }
}
