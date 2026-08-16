using System.Collections.Immutable;
using Namespace2Xml.Budgets;
using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using Namespace2Xml.Scheme;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Pipeline step 1's second half: Section 15.1's "resolve references among scheme entries".
/// </summary>
/// <remarks>
/// Every expectation here is authored from the specification clause named in the test, never from
/// what the resolver currently produces.
/// </remarks>
[TestFixture]
public class SchemeReferenceResolverTests
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

    private ImmutableArray<SchemeEntry> Resolve(string document, int depth = 64)
    {
        var read = SchemeReader.Read(Records(document), 2, "s.properties", diagnostics);
        var budget = new GlobalBudget(
            ResourceLimits.Defaults with { MaxReferenceDepth = depth });

        return SchemeReferenceResolver.Resolve(read.Entries, budget, diagnostics);
    }

    private static string TextOf(ImmutableArray<SchemeEntry> entries, SchemeDirective directive) =>
        entries.Single(entry => entry.Directive == directive).Value.LiteralText!;

    private Diagnostic Only(string document, int depth = 64)
    {
        Resolve(document, depth);

        return diagnostics.Drain().ShouldHaveSingleItem();
    }

    // ---- Section 15.1 step 1: resolution ----------------------------------------------------------

    /// <summary>
    /// Section 15.1 step 1: "Parse scheme syntax using secure format defaults and resolve
    /// references among scheme entries."
    /// </summary>
    [Test]
    public void AReferenceToAnotherDirectiveResolvesToThatDirectivesValue()
    {
        var entries = Resolve("a.root=cfg\na.filename=${a.root}.conf");

        TextOf(entries, SchemeDirective.Filename).ShouldBe("cfg.conf");
        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 13.1: "References may be recursive." A referent that is itself a reference is
    /// resolved before it is adopted, so one lookup crosses the whole chain.
    /// </summary>
    [Test]
    public void AChainOfReferencesResolvesTransitively()
    {
        var entries = Resolve("a.key=k\na.root=${a.key}\na.filename=${a.root}.conf");

        TextOf(entries, SchemeDirective.Filename).ShouldBe("k.conf");
        TextOf(entries, SchemeDirective.Root).ShouldBe("k");
    }

    /// <summary>
    /// Nothing in Section 15.1 makes step 1 a single pass over the file, and Section 13.1 resolves
    /// a reference by what it names rather than by where it was written, so a directive may be
    /// referenced before it is declared.
    /// </summary>
    [Test]
    public void AReferenceMayNameADirectiveDeclaredLater()
    {
        var entries = Resolve("a.filename=${a.root}.conf\na.root=cfg");

        TextOf(entries, SchemeDirective.Filename).ShouldBe("cfg.conf");
    }

    /// <summary>
    /// Section 13.2: "If a value contains any concatenation, its result is a string." A scheme
    /// value is text in every case, so concatenation is the ordinary shape rather than the
    /// exception.
    /// </summary>
    [Test]
    public void SeveralReferencesInOneValueConcatenate()
    {
        var entries = Resolve("a.root=r\na.key=k\na.filename=${a.root}-${a.key}.conf");

        TextOf(entries, SchemeDirective.Filename).ShouldBe("r-k.conf");
    }

    /// <summary>
    /// Section 15.2: "A later matching directive overrides an earlier matching directive for the
    /// same effective setting." A reference names a setting, so it reads the value that setting
    /// ends up with rather than the one nearest above it.
    /// </summary>
    [Test]
    public void AReferenceReadsTheDirectiveThatWinsTheOverrideStream()
    {
        var entries = Resolve("a.root=first\na.filename=${a.root}.conf\na.root=second");

        TextOf(entries, SchemeDirective.Filename).ShouldBe("second.conf");
    }

    /// <summary>
    /// Section 15 gives directive names their own vocabulary and matches them ASCII
    /// case-insensitively, so the case a reference is written in cannot change what it names.
    /// </summary>
    [Test]
    public void ADirectiveNameInAReferenceIsMatchedCaseInsensitively()
    {
        var entries = Resolve("a.root=cfg\na.filename=${a.ROOT}.conf");

        TextOf(entries, SchemeDirective.Filename).ShouldBe("cfg.conf");
    }

    /// <summary>
    /// A value with no reference is returned unchanged, and Section 12.1's capture substitution
    /// happens where the directive is read rather than here, so a <c>*</c> survives step 1.
    /// </summary>
    [Test]
    public void AValueCarryingBothAReferenceAndACaptureKeepsTheCapture()
    {
        var entries = Resolve("a.delimiter=-\na.*.filename=${a.delimiter}*.conf");
        var filename = entries.Single(entry => entry.Directive == SchemeDirective.Filename).Value;

        filename.ContainsReference.ShouldBeFalse();
        filename.ContainsWildcard.ShouldBeTrue();
        filename.Tokens.Length.ShouldBe(3);
        filename.Tokens[0].ShouldBe(new ResolvedReferenceToken("-"));
        filename.Tokens[2].ShouldBe(new LiteralValueToken(".conf"));
    }

    /// <summary>
    /// Section 16.2: a reference's "resulting text is opaque segment data", so what a reference
    /// contributes stays distinguishable from text the scheme author wrote all the way to Section
    /// 16.2 composition. Folding it into the surrounding literals here would lose the distinction
    /// irrecoverably, because the two are the same characters.
    /// </summary>
    [Test]
    public void ReferencedTextStaysDistinguishableFromWrittenText()
    {
        var entries = Resolve("a.root=dir/name\na.filename=${a.root}.conf");
        var filename = entries.Single(entry => entry.Directive == SchemeDirective.Filename).Value;

        filename.Tokens.ShouldBe(
            [new ResolvedReferenceToken("dir/name"), new LiteralValueToken(".conf")]);
    }

    /// <summary>
    /// Every directive except <c>filename</c> wants the settled text and nothing else, so a
    /// resolved reference reads as ordinary text through
    /// <see cref="InterpretedValue.LiteralText"/> — which is also what keeps a resolved value out
    /// of the paths that reject an unresolved one.
    /// </summary>
    [Test]
    public void AResolvedReferenceReadsAsOrdinaryTextEverywhereElse()
    {
        var entries = Resolve("a.root=x\na.delimiter=${a.root}-${a.root}");
        var delimiter = entries.Single(entry => entry.Directive == SchemeDirective.Delimiter).Value;

        delimiter.LiteralText.ShouldBe("x-x");
        delimiter.ContainsReference.ShouldBeFalse();
        delimiter.ContainsWildcard.ShouldBeFalse();
    }

    /// <summary>
    /// A referent can never be empty, because Section 15's "every recognized directive requires a
    /// nonempty scalar value after format parsing" is enforced when the scheme is read — several
    /// steps before resolution. So a reference always contributes text, and there is no such thing
    /// as a resolved reference that contributes nothing.
    /// </summary>
    [Test]
    public void AnEmptyDirectiveIsRejectedBeforeItCanBeAReferent()
    {
        var entries = Resolve("a.root=\na.filename=pre-${a.root}post.conf");

        entries.ShouldNotContain(entry => entry.Directive == SchemeDirective.Root);
        diagnostics.Drain().ShouldContain(diagnostic => diagnostic.Code == "SCHEME001");
    }

    // ---- Section 13: what a scheme reference cannot do --------------------------------------------

    /// <summary>
    /// Section 13.1: "Missing references [...] are blocking errors." Section 15's "the final
    /// qualified-name part identifies a directive" is what makes a name that ends in anything else
    /// missing rather than unresolvable.
    /// </summary>
    [Test]
    public void AReferenceWhoseLastPartIsNoDirectiveIsMissing()
    {
        var diagnostic = Only("a.filename=${a.name}.conf");

        diagnostic.Code.ShouldBe("REFERENCE002");
        diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
        diagnostic.Phase.ShouldBe(DiagnosticPhase.Scheme);
    }

    /// <summary>
    /// Section 15.1 step 1: "Scheme references cannot target input data." A path that names a
    /// directive nobody declared therefore resolves to nothing rather than falling back to the
    /// overlay.
    /// </summary>
    [Test]
    public void AReferenceToAnUndeclaredDirectiveIsMissing()
    {
        var diagnostic = Only("a.filename=${a.root}.conf");

        diagnostic.Code.ShouldBe("REFERENCE002");
        diagnostic.Path.ShouldBe("a.filename");
    }

    /// <summary>Section 13.1: "Missing references and cycles are blocking errors."</summary>
    [Test]
    public void ACycleAmongSchemeDirectivesIsABlockingError()
    {
        var diagnostic = Only("a.root=${a.filename}\na.filename=${a.root}.conf");

        diagnostic.Code.ShouldBe("REFERENCE003");
        diagnostic.Phase.ShouldBe(DiagnosticPhase.Scheme);
    }

    /// <summary>
    /// Section 22 counts <c>REFERENCE003</c> "once per canonically distinct reachable cycle", and a
    /// ring of two directives is entered from both of them.
    /// </summary>
    [Test]
    public void OneRingIsReportedOnceHoweverManyOfItsMembersAreResolved()
    {
        Resolve("a.root=${a.filename}\na.filename=${a.root}");

        diagnostics.Drain().Count(diagnostic => diagnostic.Code == "REFERENCE003").ShouldBe(1);
    }

    /// <summary>
    /// Section 22 canonicalizes a ring by rotating its smallest member first, so which member the
    /// resolver happened to reach first cannot change what is reported.
    /// </summary>
    [Test]
    public void ACycleIsReportedAtItsCanonicallyFirstMember()
    {
        var forwards = Only("a.root=${a.filename}\na.filename=${a.root}");

        diagnostics.Drain();

        var backwards = Only("a.filename=${a.root}\na.root=${a.filename}");

        forwards.Path.ShouldBe("a.filename");
        backwards.Path.ShouldBe("a.filename");
        forwards.Message.ShouldBe(backwards.Message);
    }

    /// <summary>
    /// Section 13.3: "Free wildcard references such as <c>${a.*}</c> are blocking errors." An
    /// explicit capture is free here for a reason of phase rather than of syntax: step 1 runs
    /// before any selector is expanded, so no capture is bound yet.
    /// </summary>
    [Test]
    public void AReferenceNamingAWildcardSelectorIsABlockingError()
    {
        var diagnostic = Only("a.*.filename=${a.*[q].root}.conf");

        diagnostic.Code.ShouldBe("REFERENCE001");
        diagnostic.Phase.ShouldBe(DiagnosticPhase.Scheme);
    }

    /// <summary>
    /// Section 6.2 bounds reference recursion depth and Section 23 requires the bound be consumed
    /// in normative pipeline order, which puts step 1 before step 15.
    /// </summary>
    /// <remarks>
    /// The chain is declared outermost first on purpose. The level charged is the number of nested
    /// unresolved values entered, and a value already resolved costs nothing to read, so the same
    /// directives written innermost first are resolved one level at a time and never nest. The last
    /// hop is free for the same reason: it reads a value that holds no reference.
    /// </remarks>
    [Test]
    public void AChainDeeperThanTheReferenceBoundCrossesIt()
    {
        var diagnostic = Only(
            "a.filename=${b.filename}\nb.filename=${c.filename}\nc.filename=${d.filename}\n"
            + "d.filename=${e.filename}\ne.filename=x",
            depth: 2);

        diagnostic.Code.ShouldBe("LIMIT001");
        diagnostic.Phase.ShouldBe(DiagnosticPhase.Scheme);
    }

    /// <summary>
    /// Section 15.4: a phase "completes every independent check that does not depend on a failed
    /// result", so a defect written in a declaration a later one overrides is still reported.
    /// </summary>
    [Test]
    public void AShadowedDeclarationsBrokenReferenceIsStillReported()
    {
        var diagnostic = Only("a.filename=${a.root}.conf\na.filename=plain.conf");

        diagnostic.Code.ShouldBe("REFERENCE002");
    }

    /// <summary>
    /// A declaration that references its own setting is not a cycle when a later declaration wins
    /// it: Section 15.2 gives the reference the surviving value, and that value holds no reference.
    /// Both declarations survive step 1, so both are checked.
    /// </summary>
    [Test]
    public void ASelfReferenceIsResolvedByALaterOverride()
    {
        var entries = Resolve("a.root=${a.root}\na.root=cfg");

        entries.Where(entry => entry.Directive == SchemeDirective.Root)
            .Select(entry => entry.Value.LiteralText)
            .ShouldBe(["cfg", "cfg"]);
        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 15.3 keeps the deprecated aliases accepted, so one written inside a reference names
    /// the directive it is an alias for rather than nothing.
    /// </summary>
    [Test]
    public void ADeprecatedAliasInAReferenceNamesItsReplacement()
    {
        var entries = Resolve("a.delimiter=-\na.filename=${a.namespacedelimiter}.conf");

        TextOf(entries, SchemeDirective.Filename).ShouldBe("-.conf");
        diagnostics.Drain()
            .Count(diagnostic => diagnostic.Code == "WARN002")
            .ShouldBe(1);
    }

    /// <summary>
    /// A scheme with no reference in it is returned as it arrived, so the whole resolver is inert
    /// for the schemes that do not use the feature.
    /// </summary>
    [Test]
    public void ASchemeWithNoReferenceIsUnchanged()
    {
        var read = SchemeReader.Read(
            Records("a.output=namespace\na.filename=plain.conf"), 2, "s.properties", diagnostics);
        var resolved = SchemeReferenceResolver.Resolve(
            read.Entries, new GlobalBudget(ResourceLimits.Defaults), diagnostics);

        resolved.ShouldBe(read.Entries);
    }
}
