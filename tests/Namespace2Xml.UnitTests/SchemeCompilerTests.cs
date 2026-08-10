using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Output;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using Namespace2Xml.Scheme;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Pipeline steps 2 through 4: compiling Section 15 directives into effective configuration.
/// </summary>
/// <remarks>
/// Every expectation here is authored from the specification clause named in the test, never from
/// what the compiler currently produces.
/// </remarks>
[TestFixture]
public class SchemeCompilerTests
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

    private SchemeConfiguration Compile(string document)
    {
        var read = SchemeReader.Read(Records(document), 2, "s.properties", diagnostics);

        return SchemeCompiler.Compile(read.Entries, diagnostics);
    }

    private OutputInstance One(string document) => Compile(document).Outputs.ShouldHaveSingleItem();

    private Diagnostic Only(string document)
    {
        Compile(document);

        return diagnostics.Drain().ShouldHaveSingleItem();
    }

    // ---- Section 16.1: output ---------------------------------------------------------------------

    /// <summary>Section 16.1 lists six formats plus the negative declaration.</summary>
    [TestCase("namespace", OutputFormat.Namespace)]
    [TestCase("quotednamespace", OutputFormat.QuotedNamespace)]
    [TestCase("json", OutputFormat.Json)]
    [TestCase("yaml", OutputFormat.Yaml)]
    [TestCase("xml", OutputFormat.Xml)]
    [TestCase("ini", OutputFormat.Ini)]
    public void EveryFormatIsRecognized(string written, OutputFormat expected) =>
        One($"output={written}").Formats.ShouldBe([expected]);

    /// <summary>Section 16.1: "Names are case-insensitive."</summary>
    [Test]
    public void AFormatNameIsCaseInsensitive() =>
        One("output=QuotedNamespace").Formats.ShouldBe([OutputFormat.QuotedNamespace]);

    /// <summary>Section 16.1: "Whitespace around comma-separated values is ignored."</summary>
    [Test]
    public void WhitespaceAroundFormatsIsIgnored() =>
        One("output= namespace , ini ").Formats.ShouldBe([OutputFormat.Namespace, OutputFormat.Ini]);

    /// <summary>
    /// Section 17.5: "Duplicate formats within one <c>output</c> declaration, such as
    /// <c>output=json,json</c>, are blocking scheme errors rather than self-collisions."
    /// </summary>
    /// <remarks>
    /// Accepting it produces two contributions to one destination, which Section 17.5 would then
    /// fold and warn about — reporting a collision between a declaration and itself, and quietly
    /// applying <c>filemerge</c> to a file merged with its own content.
    /// </remarks>
    [Test]
    public void ADuplicateFormatInOneDeclarationIsRejected()
    {
        Only("output=json,json").Code.ShouldBe("SCHEME001");

        Compile("output=json,json").Outputs.ShouldBeEmpty();
    }

    /// <summary>Case-insensitive names make the same format, so the duplicate is still a duplicate.</summary>
    [Test]
    public void ADuplicateFormatSpelledDifferentlyIsStillRejected() =>
        Only("output=json,JSON").Code.ShouldBe("SCHEME001");

    /// <summary>
    /// Section 16.1: "Formats in one comma-separated declaration have a left-to-right declaration
    /// ordinal." Section 17.5 folds by that ordinal, so the order has to survive compilation.
    /// </summary>
    [Test]
    public void FormatsKeepTheirDeclarationOrder() =>
        One("output=ini,namespace").Formats.ShouldBe([OutputFormat.Ini, OutputFormat.Namespace]);

    /// <summary>Section 16.1: an unrecognized format name is a scheme error.</summary>
    [Test]
    public void AnUnknownFormatIsRejected() => Only("output=properties").Code.ShouldBe("SCHEME001");

    /// <summary>
    /// Section 16.1: "'ignore' is a negative output declaration. When it is the winning declaration,
    /// no output is produced for that concrete selector."
    /// </summary>
    [Test]
    public void IgnoreProducesAnInstanceWithNoFormats() =>
        One("a.output=ignore").Formats.ShouldBeEmpty();

    /// <summary>Section 16.1: "'ignore' must appear alone in its declaration."</summary>
    [TestCase("output=ignore,namespace")]
    [TestCase("output=namespace,ignore")]
    [TestCase("output=ignore,ignore")]
    public void IgnoreMustAppearAlone(string document) => Only(document).Code.ShouldBe("SCHEME001");

    /// <summary>
    /// Section 16.1: "A later 'output' declaration replaces the complete earlier output-format set."
    /// Accumulating would make a restored output carry the formats it was suppressed with.
    /// </summary>
    [Test]
    public void ALaterOutputReplacesTheEarlierSet() =>
        One("a.output=json,xml\na.output=ini").Formats.ShouldBe([OutputFormat.Ini]);

    /// <summary>
    /// Section 16.1: "a later non-ignore declaration can restore output, and a later 'ignore'
    /// declaration can suppress it again."
    /// </summary>
    [Test]
    public void IgnoreParticipatesInOrdinaryOverride()
    {
        One("a.output=ignore\na.output=namespace").Formats.ShouldBe([OutputFormat.Namespace]);
        SetUp();
        One("a.output=namespace\na.output=ignore").Formats.ShouldBeEmpty();
    }

    // ---- Section 15.2: selector scope --------------------------------------------------------------

    /// <summary>
    /// Section 15.2: "a directive for selector 'a' does not implicitly configure an independently
    /// created 'a.x' output instance".
    /// </summary>
    [Test]
    public void ConfigurationDoesNotDescendToANestedSelector()
    {
        var outputs = Compile("a.output=namespace\na.delimiter=_\na.x.output=namespace").Outputs;

        outputs.Length.ShouldBe(2);
        outputs.Single(output => output.Selector.ToString() == "a").Delimiter.ShouldBe("_");
        outputs.Single(output => output.Selector.ToString() == "a.x").Delimiter.ShouldBeNull();
    }

    /// <summary>
    /// Section 15.2's "binds to no concrete output instance" warning is emitted after wildcard
    /// expansion, not at compile time, so an exact-selector directive at a selector no
    /// <c>output</c> declaration created does not warn here.
    /// </summary>
    /// <remarks>
    /// The compile step keeps the directive as a per-selector winner and defers the binding
    /// decision. A wildcard <c>output</c> declaration and an exact-selector directive can meet
    /// on a concrete instance whose selector neither wrote, and only pipeline step 13 can see it.
    /// The corresponding planning-phase behavior is verified in
    /// <see cref="OutputInstanceExpansionTests"/>.
    /// </remarks>
    [Test]
    public void ADirectiveThatCompilesToANoInstanceSelectorDoesNotWarnAtCompileTime()
    {
        Compile("a.filename=x.txt");

        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// The warning is about binding, not about suppression: an <c>ignore</c> declaration still
    /// creates the concrete instance the directive binds to, and Section 16.1 lets a later
    /// declaration restore it.
    /// </summary>
    [Test]
    public void ADirectiveOnAnIgnoredInstanceDoesNotWarn()
    {
        Compile("a.output=ignore\na.filename=x.txt");

        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 15.2: "All scheme directives follow source order only." The later declaration wins
    /// wherever it is written relative to the 'output' that creates the instance.
    /// </summary>
    [Test]
    public void TheLaterDirectiveWinsRegardlessOfPosition() =>
        One("a.filename=first\na.output=namespace\na.filename=second").Filename.ShouldBe("second");

    // ---- Section 16.2: filename --------------------------------------------------------------------

    /// <summary>
    /// Section 16.2: "An explicit 'filename' value is the complete relative destination path and is
    /// used verbatim after the portable path processing below."
    /// </summary>
    [Test]
    public void AFilenameIsCarriedVerbatim() =>
        One("output=namespace\nfilename=conf/app.properties").Filename
            .ShouldBe("conf/app.properties");

    /// <summary>
    /// Section 21.1 validates a path "after filename expansion" at pipeline step 14, so a written
    /// path is not rejected during compilation: the string checked has to be the one that reaches
    /// the filesystem, and captures have not been substituted yet.
    /// </summary>
    [Test]
    public void AWrittenPathIsNotValidatedDuringCompilation()
    {
        One("output=namespace\nfilename=/etc/passwd").Filename.ShouldBe("/etc/passwd");

        diagnostics.Drain().ShouldBeEmpty();
    }

    // ---- Section 16.3: root ------------------------------------------------------------------------

    /// <summary>Section 16.3: <c>root=x.y</c> wraps content in two named levels.</summary>
    [Test]
    public void ARootValueIsANamePath() =>
        One("output=namespace\nroot=x.y").Root!.Parts.Length.ShouldBe(2);

    /// <summary>
    /// Section 16.3: "'\.' represents a literal dot inside one root name part." Appendix A.3 leaves
    /// the sequence intact because it is not a value escape, so the name grammar still sees it and
    /// one part is produced rather than two.
    /// </summary>
    [Test]
    public void AnEscapedDotStaysInsideOneRootPart()
    {
        var root = One("output=namespace\nroot=x\\.y").Root!;

        root.Parts.Length.ShouldBe(1);
        ((OrdinaryPart)root.Parts[0]).LiteralText.ShouldBe("x.y");
    }

    /// <summary>
    /// Section 12.1: "in a scheme whose selector contains no wildcard, <c>*</c> in a
    /// <c>filename</c>, <c>root</c>, or <c>delimiter</c> value is literal text". Section 16.3 gives
    /// the value the name grammar, in which a bare <c>*</c> lexes as a wildcard token, so the
    /// decision Section 12.1 already made has to be carried into the lexed name rather than left
    /// for a later matcher to reinterpret.
    /// </summary>
    [Test]
    public void AnAsteriskInARootValueIsALiteralNamePart()
    {
        var root = One("output=namespace\nroot=x.*").Root.ShouldNotBeNull();

        root.Parts.Length.ShouldBe(2);
        ((OrdinaryPart)root.Parts[1]).LiteralText.ShouldBe("*");
    }

    // ---- Section 16.4: delimiter -------------------------------------------------------------------

    /// <summary>Section 16.4: an explicit delimiter joins path parts for the flat formats.</summary>
    [Test]
    public void ADelimiterIsCarried() =>
        One("output=namespace\ndelimiter=__").Delimiter.ShouldBe("__");

    /// <summary>
    /// Section 16.4: "For namespace output, a delimiter must not contain '=', backslash, or any
    /// scalar in the Section 19.1 forbidden set."
    /// </summary>
    [TestCase("=")]
    [TestCase("a=b")]
    [TestCase("\\")]
    public void AnIllegalNamespaceDelimiterIsRejected(string delimiter) =>
        Only($"output=namespace\ndelimiter={delimiter}").Code.ShouldBe("SCHEME001");

    /// <summary>
    /// Section 16.4 excludes a delimiter "built only from 'u', braces, and upper-case hexadecimal
    /// digits", because a consumer splitting on it would split inside a '\u{HEX}' escape.
    /// </summary>
    [Test]
    public void ADelimiterThatCouldSplitAnEscapeIsRejected() =>
        Only("output=namespace\ndelimiter=2E").Code.ShouldBe("SCHEME001");

    /// <summary>
    /// Section 16.4 scopes those restrictions to namespace output: quoted-namespace and INI output
    /// "instead apply their own validation and escaping after joining", under Sections 19.2 and
    /// 19.6. Rejecting here would refuse a configuration those formats can render.
    /// </summary>
    [Test]
    public void TheNamespaceDelimiterRuleDoesNotConstrainOtherFormats()
    {
        One("output=ini\ndelimiter=2E").Delimiter.ShouldBe("2E");

        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// The rule applies to an instance that renders namespace output among others, because that one
    /// destination still has to be spelled unambiguously.
    /// </summary>
    [Test]
    public void TheNamespaceDelimiterRuleAppliesWhenNamespaceIsOneOfSeveralFormats() =>
        Only("output=ini,namespace\ndelimiter=2E").Code.ShouldBe("SCHEME001");

    // ---- Section 16.9: INI options -----------------------------------------------------------------

    /// <summary>Section 16.9: the INI default is <c>RejectMultiline</c>.</summary>
    [Test]
    public void TheIniOptionsDefaultToRejectingMultiline() =>
        One("output=ini").IniOptions.ShouldBe(IniOutputOptions.RejectMultiline);

    /// <summary>Section 16.9 lists five portable INI options, matched case-insensitively.</summary>
    [Test]
    public void IniOptionsAreReadCaseInsensitively() =>
        One("output=ini\ninioutputoptions=hashcomments,escapemultiline").IniOptions
            .ShouldBe(IniOutputOptions.HashComments | IniOutputOptions.EscapeMultiline);

    /// <summary>
    /// Section 16.9: "When a replacement omits every flag from a mutually exclusive mode group,
    /// that group's documented default is reapplied." Leaving the multiline group unset would let
    /// a serializer choose.
    /// </summary>
    [Test]
    public void AnOmittedModeGroupReappliesItsDefault() =>
        One("output=ini\ninioutputoptions=SemicolonComments").IniOptions
            .ShouldBe(IniOutputOptions.SemicolonComments | IniOutputOptions.RejectMultiline);

    /// <summary>Section 16.9: the two comment markers are mutually exclusive.</summary>
    [Test]
    public void ContradictoryIniOptionsAreRejected() =>
        Only("output=ini\ninioutputoptions=SemicolonComments,HashComments").Code
            .ShouldBe("SCHEME001");

    /// <summary>Section 16.9: the two multiline strategies are mutually exclusive.</summary>
    [Test]
    public void ContradictoryMultilineStrategiesAreRejected() =>
        Only("output=ini\ninioutputoptions=RejectMultiline,EscapeMultiline").Code
            .ShouldBe("SCHEME001");

    /// <summary>Section 16.9 names its flags; it does not number them.</summary>
    [TestCase("4")]
    [TestCase("0")]
    public void ANumericIniOptionIsRejected(string written) =>
        Only($"output=ini\ninioutputoptions={written}").Code.ShouldBe("SCHEME001");

    /// <summary>
    /// Section 16.9: "the later complete directive replaces the earlier complete flag set. Flags
    /// from separate declarations do not accumulate."
    /// </summary>
    [Test]
    public void ALaterOptionsDeclarationReplacesTheEarlierSet() =>
        One("output=ini\ninioutputoptions=HashComments\ninioutputoptions=QuoteValues").IniOptions
            .ShouldBe(IniOutputOptions.QuoteValues | IniOutputOptions.RejectMultiline);

    // ---- Sections 16.10 and 16.11: merge strategies ------------------------------------------------

    /// <summary>Section 16.11: <c>filemerge</c> defaults to <c>deep</c>.</summary>
    [Test]
    public void FileMergeDefaultsToDeep() => One("output=namespace").FileMerge.ShouldBe(MergeStrategy.Deep);

    /// <summary>Section 16.11 lists four strategies, matched case-insensitively.</summary>
    [TestCase("deep", MergeStrategy.Deep)]
    [TestCase("Replace", MergeStrategy.Replace)]
    [TestCase("APPEND", MergeStrategy.Append)]
    [TestCase("error", MergeStrategy.Error)]
    public void EveryFileMergeStrategyIsRecognized(string written, MergeStrategy expected) =>
        One($"output=namespace\nfilemerge={written}").FileMerge.ShouldBe(expected);

    /// <summary>
    /// Section 16.11: "'filemerge=error' is not sticky. A later matching declaration may replace it
    /// with another complete 'filemerge' value under universal source-order precedence."
    /// </summary>
    [Test]
    public void FileMergeErrorIsNotSticky() =>
        One("output=namespace\nfilemerge=error\nfilemerge=deep").FileMerge
            .ShouldBe(MergeStrategy.Deep);

    /// <summary>Section 16.10: an unrecognized strategy is a scheme error.</summary>
    [Test]
    public void AnUnknownStrategyIsRejected() =>
        Only("output=namespace\nfilemerge=merge").Code.ShouldBe("SCHEME001");

    /// <summary>
    /// Section 16.10: <c>merge</c> "applies only to common-model input", so it is not part of an
    /// output instance and is compiled separately at pipeline step 4.
    /// </summary>
    [Test]
    public void AnInputMergeIsCompiledSeparately()
    {
        var configuration = Compile("a.b.merge=append");

        configuration.Outputs.ShouldBeEmpty();

        var merge = configuration.InputMerges.ShouldHaveSingleItem();

        merge.Strategy.ShouldBe(MergeStrategy.Append);
        merge.Path!.Parts.Length.ShouldBe(2);
        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 16.10: a <c>merge</c> written with no path governs the root, where every contribution
    /// is at some path beneath it.
    /// </summary>
    [Test]
    public void ARootLevelInputMergeHasNoPath() =>
        Compile("merge=replace").InputMerges.ShouldHaveSingleItem().Path.ShouldBeNull();

    /// <summary>
    /// Section 16.10: "Input 'merge' directives required at pipeline step 4 must use literal paths
    /// and must not contain wildcards or references." Step 4 runs before any name graph exists to
    /// expand a wildcard against.
    /// </summary>
    [Test]
    public void AWildcardInputMergePathIsRejected()
    {
        Only("a.*.merge=append").Code.ShouldBe("SCHEME001");
    }

    /// <summary>
    /// Every <c>merge</c> is kept, not just the last: Section 16.10 scopes each to the node it
    /// matches, and two different paths are two different settings.
    /// </summary>
    [Test]
    public void EveryInputMergePathIsKept() =>
        Compile("a.merge=append\nb.merge=replace").InputMerges.Length.ShouldBe(2);

    /// <summary>
    /// Section 15.2: "A later matching directive overrides an earlier matching directive for the
    /// same effective setting." One path has one effective input merge strategy.
    /// </summary>
    /// <remarks>
    /// Keeping both would not merely be untidy. The overlay's strategy map is a dictionary keyed by
    /// path, so a repeated path reaches it as a duplicate key — a user-caused condition escaping as
    /// an unhandled exception, which Section 6.3 forbids.
    /// </remarks>
    [Test]
    public void ARepeatedInputMergePathKeepsOnlyTheLaterStrategy()
    {
        var merges = Compile("a.merge=append\na.merge=replace").InputMerges;

        merges.ShouldHaveSingleItem().Strategy.ShouldBe(MergeStrategy.Replace);
        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>A repeated root <c>merge</c> overrides in the same way.</summary>
    [Test]
    public void ARepeatedRootInputMergeKeepsOnlyTheLaterStrategy() =>
        Compile("merge=append\nmerge=error").InputMerges.ShouldHaveSingleItem()
            .Strategy.ShouldBe(MergeStrategy.Error);

    // ---- Section 15.1 step 1: unresolved values ----------------------------------------------------

    /// <summary>
    /// Section 15.1 step 1 resolves scheme-internal references before any directive value is read.
    /// A value carrying one is therefore not compiled here; compiling the text as written would
    /// treat the reference's spelling as a literal file name.
    /// </summary>
    [Test]
    public void AReferenceBearingValueIsNotCompiled()
    {
        var configuration = Compile("output=namespace\nfilename=${a.b}");

        configuration.Outputs.ShouldHaveSingleItem().Filename.ShouldBeNull();
        configuration.Deferred.ShouldHaveSingleItem().Directive.ShouldBe(SchemeDirective.Filename);
    }

    /// <summary>
    /// A directive value this build does not compile is carried rather than dropped, so the driver
    /// can refuse the run. Silently ignoring configuration the user wrote is the failure mode this
    /// list exists to prevent.
    /// </summary>
    /// <remarks>
    /// Section 12.1's substitution is performed for every directive whose captures are bound where
    /// its value is read, so the residue is <c>type</c> alone — Section 16.6 closes its value to a
    /// keyword set and Section 22 fixes the phase its rejection is reported in.
    /// </remarks>
    [Test]
    public void AnUncompiledDirectiveIsCarried() =>
        Compile("a.*.type=arr*y").Deferred.ShouldHaveSingleItem()
            .Directive.ShouldBe(SchemeDirective.Type);

    /// <summary>
    /// Every member of <see cref="SchemeDirective"/> has a compiler arm, so no well-formed
    /// directive reaches the deferral list and refuses a run that Section 16 defines completely.
    /// A directive added to the enum without an arm makes this red.
    /// </summary>
    [TestCase("a.output=namespace")]
    [TestCase("a.filename=f")]
    [TestCase("a.root=r")]
    [TestCase("a.delimiter=:")]
    [TestCase("a.key=name")]
    [TestCase("a.type=array")]
    [TestCase("a.substitute=None")]
    [TestCase("a.xmlinputoptions=attributes")]
    [TestCase("a.xmloutputoptions=indent")]
    [TestCase("a.jsoninputoptions=arrayasobject")]
    [TestCase("a.jsonoutputoptions=indent")]
    [TestCase("a.yamlinputoptions=arrayasobject")]
    [TestCase("a.yamloutputoptions=indent")]
    [TestCase("a.inioutputoptions=preservesections")]
    [TestCase("a.merge=append")]
    [TestCase("a.filemerge=append")]
    public void EveryDirectiveHasACompilerArm(string declaration) =>
        Compile(declaration).Deferred.ShouldBeEmpty();

    // ---- Sections 16.5 and 16.6: path-scoped transformations ---------------------------------------

    /// <summary>
    /// Section 15.2 evaluates <c>type</c> "against absolute stable pre-transformation paths in
    /// every output instance containing the path", so a rule is compiled into its own source-ordered
    /// list rather than into the per-selector winner table the instance settings use.
    /// </summary>
    [Test]
    public void ATypeDirectiveCompilesToASourceOrderedRule()
    {
        var configuration = Compile("a.type=array");

        configuration.Deferred.ShouldBeEmpty();

        var rule = configuration.Transforms.ShouldHaveSingleItem();

        rule.KeyField.ShouldBeNull();
        rule.Types.ShouldNotBeNull().ToString().ShouldBe("array");
        CanonicalPath.Of(rule.Path.ShouldNotBeNull().Parts).ShouldBe("a");
    }

    /// <summary>Section 16.5: a <c>key</c> directive names the generated field.</summary>
    [Test]
    public void AKeyDirectiveCompilesToASourceOrderedRule()
    {
        var rule = Compile("a.key=name").Transforms.ShouldHaveSingleItem();

        rule.Types.ShouldBeNull();
        rule.KeyField.ShouldNotBeNull().LiteralText.ShouldBe("name");
    }

    /// <summary>
    /// Section 16.6 makes the set the unit of override, so two directives are two rules and the
    /// later one is not merged into the earlier at compile time. Which one applies is a per-path
    /// question that only step 16 can answer.
    /// </summary>
    [Test]
    public void TwoTypeDirectivesForOnePathStayTwoRules() =>
        Compile("a.type=array\na.type=mapping").Transforms
            .Select(rule => rule.Types!.Value.ToString())
            .ShouldBe(["array", "mapping"]);

    /// <summary>Section 16.6: an illegal combination is a blocking scheme error.</summary>
    [Test]
    public void AnIllegalTypeCombinationIsRejected() =>
        Only("a.type=array,mapping").Code.ShouldBe("SCHEME001");

    /// <summary>Section 16.6: an unrecognized type value is a blocking scheme error.</summary>
    [Test]
    public void AnUnknownTypeValueIsRejected() =>
        Only("a.type=nonsense").Code.ShouldBe("SCHEME001");

    /// <summary>Section 16.5: a <c>key</c> directive that names no field is rejected.</summary>
    [Test]
    public void AKeyDirectiveWithNoFieldIsRejected() =>
        Only("a.key= ").Code.ShouldBe("SCHEME001");

    /// <summary>
    /// Section 15.3 keeps the legacy <c>xmlns</c> and <c>xmlnssuffix</c> type values accepted "as
    /// no-ops with one warning per scheme". A directive whose every value is a no-op compiles to no
    /// rule at all, because a rule carrying an empty set would win Section 16.6's set replacement
    /// and clear an earlier directive — which is not what doing nothing means.
    /// </summary>
    [Test]
    public void ALegacyTypeValueWarnsAndIsIgnored()
    {
        Compile("a.type=xmlns").Transforms.ShouldBeEmpty();

        diagnostics.Drain().ShouldHaveSingleItem().Code.ShouldBe("WARN002");
    }

    /// <summary>Section 15.3: a no-op value written beside a real one does not consume it.</summary>
    [Test]
    public void ALegacyTypeValueDoesNotSuppressItsNeighbour()
    {
        Compile("a.type=xmlnssuffix,array").Transforms.ShouldHaveSingleItem()
            .Types.ShouldNotBeNull().ToString().ShouldBe("array");

        diagnostics.Drain().ShouldHaveSingleItem().Code.ShouldBe("WARN002");
    }

    // ---- Section 17.5: declaration order -----------------------------------------------------------

    /// <summary>
    /// Section 17.5 folds destinations by "output-declaration source order", so each instance
    /// carries the order of the declaration that created it, and instances are produced in that
    /// order.
    /// </summary>
    [Test]
    public void InstancesCarryAndKeepDeclarationOrder()
    {
        var outputs = Compile("b.output=namespace\na.output=ini").Outputs;

        outputs.Select(output => output.Selector.ToString()).ShouldBe(["b", "a"]);
        outputs[0].DeclarationOrder.ShouldBeLessThan(outputs[1].DeclarationOrder);
    }

    /// <summary>
    /// A replaced declaration takes the later declaration's order, because Section 16.1 makes the
    /// later one the declaration that produces the instance.
    /// </summary>
    [Test]
    public void AReplacedDeclarationTakesTheLaterOrder()
    {
        var outputs = Compile("a.output=namespace\nb.output=ini\na.output=json").Outputs;

        outputs.Select(output => output.Selector.ToString()).ShouldBe(["b", "a"]);
    }
    /// <summary>
    /// Section 17.5 makes the encoded selector the fold order's "final deterministic tie-breaker",
    /// which requires the spelling to be injective. Joining part texts with the delimiter is not:
    /// the one-part selector whose literal text is <c>a.b</c> and the two-part selector <c>a</c>,
    /// <c>b</c> would both join to <c>a.b</c>, and two distinct contributions would fold in
    /// arrival order.
    /// </summary>
    [Test]
    public void TwoDistinctSelectorsNeverEncodeAlike()
    {
        var joined = new SelectorKey(
            new QualifiedName([new OrdinaryPart([new LiteralToken("a.b")])]));

        var separate = new SelectorKey(new QualifiedName(
            [new OrdinaryPart([new LiteralToken("a")]), new OrdinaryPart([new LiteralToken("b")])]));

        joined.ToString().ShouldNotBe(separate.ToString());
    }

    /// <summary>Section 15.2's root selector spells the empty path.</summary>
    [Test]
    public void TheRootSelectorEncodesAsEmpty() =>
        SelectorKey.Root.ToString().ShouldBe(string.Empty);
}
