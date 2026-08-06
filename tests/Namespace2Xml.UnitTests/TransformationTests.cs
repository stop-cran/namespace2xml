using System.Collections.Immutable;
using System.Text;
using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Output;
using Namespace2Xml.Pipeline;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// The Section 15.1 pipeline driver, exercised end to end through an in-memory source reader and
/// publication sink. Expectations are authored from the specification clauses each step enforces.
/// </summary>
/// <remarks>
/// These are driver tests, not unit tests of the components the driver composes: they assert what
/// an invocation produces, because that is the only place step order, refusal, and the wiring
/// between phases become observable. A component test cannot show that step 12 ran before
/// serialization, or that a refusal in step 14 suppressed publication.
/// </remarks>
[TestFixture]
public sealed class TransformationTests
{
    /// <summary>An in-memory corpus of sources, keyed by the path as written on the command line.</summary>
    private sealed class Sources : ISourceReader
    {
        private readonly Dictionary<string, byte[]> files;

        public Sources(params (string Path, string Text)[] files)
            : this([.. files.Select(f => (f.Path, new UTF8Encoding(false).GetBytes(f.Text)))])
        {
        }

        /// <summary>
        /// Supplies bytes rather than text, so a test can present a source that does not decode.
        /// Section 7.4 failures are byte-level and cannot be expressed as a .NET string.
        /// </summary>
        public Sources(params (string Path, byte[] Bytes)[] files) =>
            this.files = files.ToDictionary(f => f.Path, f => f.Bytes, StringComparer.Ordinal);

        public SourceRead Read(string path) =>
            files.TryGetValue(path, out var bytes)
                ? new SourceRead(SourceReadStatus.Read, bytes, null, path)
                : new SourceRead(SourceReadStatus.Missing, null, "not found", path);
    }

    /// <summary>Records what would have been written, so a test never touches a filesystem.</summary>
    private sealed class Sink : IPublicationSink
    {
        public Dictionary<string, string> Written { get; } = new(StringComparer.Ordinal);

        public List<string> Directories { get; } = [];

        public void CreateDirectory(string root, string relative) => Directories.Add(relative);

        public void Write(string root, string relative, OutputBuffer buffer) =>
            Written[relative] = new UTF8Encoding(false).GetString(buffer.ToArray());
    }

    private static TransformationResult Run(Sink sink, Sources sources, params string[] arguments)
    {
        var parsed = CommandLineParser.Parse(arguments);
        parsed.Diagnostic.ShouldBeNull();
        return Transformation.Run(parsed.CommandLine.ShouldNotBeNull(), sources, sink);
    }

    private static (TransformationResult Result, Sink Sink) Transform(string input, string scheme)
    {
        var sink = new Sink();
        var sources = new Sources(("in.txt", input), ("scheme.txt", scheme));
        return (Run(sink, sources, "-i", "in.txt", "-s", "scheme.txt"), sink);
    }

    private static ImmutableArray<string> Codes(TransformationResult result) =>
        [.. result.Diagnostics.Select(d => d.Code)];

    // ---- The complete path -------------------------------------------------------------------

    [Test]
    public void AWholeInvocationProducesTheSelectedSubtree()
    {
        // Section 14.1: "the concrete selector prefix is removed", so `app.name` is emitted as
        // `name`. Section 16.2 derives the filename from the selector.
        var (result, sink) = Transform("app.name=example\napp.port=8080\n", "app.output=namespace\n");

        result.ExitCode.ShouldBe(0);
        result.Published.ShouldBe(1);
        sink.Written.Keys.ShouldBe(["app.properties"]);
        sink.Written["app.properties"].ShouldBe("name=example\nport=8080\n");
    }

    [Test]
    public void OnlyTheSelectedSubtreeIsEmitted()
    {
        var (_, sink) = Transform("app.name=example\nother.name=hidden\n", "app.output=namespace\n");

        sink.Written["app.properties"].ShouldBe("name=example\n");
    }

    [Test]
    public void ScalarKindsAreInferredBeforeSerialization()
    {
        // Section 18 canonical text is what reaches the file. `1.50` surviving as `1.50` would show
        // step 12 never ran; `1.5` shows it ran and that its product was the one serialized.
        var (_, sink) = Transform(
            "app.a=1.50\napp.b=TRUE\napp.c=007\napp.d=hello\n",
            "app.output=namespace\n");

        sink.Written["app.properties"].ShouldBe("a=1.5\nb=true\nc=7\nd=hello\n");
    }

    [Test]
    public void EachRequestedFormatBecomesItsOwnDestination()
    {
        var (result, sink) = Transform("app.name=example\n", "app.output=namespace,ini\n");

        result.Published.ShouldBe(2);
        sink.Written.Keys.Order(StringComparer.Ordinal).ShouldBe(["app.ini", "app.properties"]);
    }

    [Test]
    public void SeparateSelectorsBecomeSeparateDestinations()
    {
        var sink = new Sink();
        var result = Run(
            sink,
            new Sources(
                ("in.txt", "a.x=1\nb.y=2\n"),
                ("scheme.txt", "a.output=namespace\nb.output=namespace\n")),
            "-i", "in.txt", "-s", "scheme.txt");

        result.ExitCode.ShouldBe(0);
        sink.Written["a.properties"].ShouldBe("x=1\n");
        sink.Written["b.properties"].ShouldBe("y=2\n");
    }

    [Test]
    public void OverridingAMappingKeyMovesItToTheWinningPosition()
    {
        // Section 5.2: "Overriding a mapping key moves that exact key... to the winning
        // contribution's position mark." So re-declaring `name` in a later source does not merely
        // change its value, it relocates it after `only`, which was never re-declared.
        var sink = new Sink();
        var result = Run(
            sink,
            new Sources(
                ("first.txt", "app.name=first\napp.only=kept\n"),
                ("second.txt", "app.name=second\n"),
                ("scheme.txt", "app.output=namespace\n")),
            "-i", "first.txt", "-i", "second.txt", "-s", "scheme.txt");

        result.ExitCode.ShouldBe(0);
        sink.Written["app.properties"].ShouldBe("only=kept\nname=second\n");
    }

    [Test]
    public void AddingAChildDoesNotMoveItsParent()
    {
        // Section 5.2: "A contribution to a strictly deeper descendant... does not change an
        // ancestor position mark... Adding a new child therefore never moves its parent."
        var sink = new Sink();
        var result = Run(
            sink,
            new Sources(
                ("first.txt", "app.db.host=h\napp.zzz=z\n"),
                ("second.txt", "app.db.port=5432\n"),
                ("scheme.txt", "app.output=namespace\n")),
            "-i", "first.txt", "-i", "second.txt", "-s", "scheme.txt");

        result.ExitCode.ShouldBe(0);
        sink.Written["app.properties"].ShouldBe("db.host=h\ndb.port=5432\nzzz=z\n");
    }

    [Test]
    public void ACommandLineVariableParticipatesInTheOverlay()
    {
        var sink = new Sink();
        var result = Run(
            sink,
            new Sources(("in.txt", "app.name=file\n"), ("scheme.txt", "app.output=namespace\n")),
            "-i", "in.txt", "-s", "scheme.txt", "-v", "app.name=cli");

        result.ExitCode.ShouldBe(0);
        sink.Written["app.properties"].ShouldBe("name=cli\n");
    }

    // ---- Section 7.2 missing sources ---------------------------------------------------------

    [Test]
    public void AMissingInputWarnsAndTheRunStillSucceeds()
    {
        // Section 7.2: a missing file "emits a warning", "contributes no data", and "does not by
        // itself cause failure".
        var sink = new Sink();
        var result = Run(
            sink,
            new Sources(("in.txt", "app.name=example\n"), ("scheme.txt", "app.output=namespace\n")),
            "-i", "in.txt", "-i", "absent.txt", "-s", "scheme.txt");

        Codes(result).ShouldBe(["WARN001"]);
        result.ExitCode.ShouldBe(0);
        sink.Written["app.properties"].ShouldBe("name=example\n");
    }

    [Test]
    public void AMissingSourceIsNamedByThePathAsWritten()
    {
        var sink = new Sink();
        var result = Run(
            sink,
            new Sources(("scheme.txt", "app.output=namespace\n")),
            "-i", "absent.txt", "-s", "scheme.txt");

        result.Diagnostics.Single().Source.ShouldBe("absent.txt");
    }

    [Test]
    public void MissingSourcesAreReportedInCommandLineOrder()
    {
        // Section 24 orders diagnostics by the source ordering key within a phase, which for a
        // whole-source diagnostic is the CLI source ordinal.
        var sink = new Sink();
        var result = Run(
            sink,
            new Sources(("scheme.txt", "app.output=namespace\n")),
            "-i", "b.txt", "-i", "a.txt", "-s", "scheme.txt");

        result.Diagnostics.Select(d => d.Source).ShouldBe(["b.txt", "a.txt"]);
    }

    // ---- Section 16.2 default destinations ---------------------------------------------------

    [Test]
    public void ANestedSelectorBecomesOneFilenameSegment()
    {
        // Section 16.2 joins the concrete selector parts with `.` into a single filename segment,
        // so a nested selector must not become a directory.
        var (_, sink) = Transform("app.db.host=localhost\n", "app.db.output=namespace\n");

        sink.Written.Keys.ShouldBe(["app.db.properties"]);
    }

    // ---- Section 19 formats ------------------------------------------------------------------

    [Test]
    public void IniOutputGroupsLeavesUnderTheirSection()
    {
        var (_, sink) = Transform("app.db.host=localhost\napp.db.port=5432\n", "app.output=ini\n");

        sink.Written["app.ini"].ShouldBe("[db]\nhost=localhost\nport=5432\n");
    }

    [Test]
    public void QuotedNamespaceUsesSingleQuoteShellEscaping()
    {
        // Section 19.2: "Values use single-quote shell escaping", with the `'\''` idiom for an
        // embedded quote, and the default extension is `.sh`.
        var (_, sink) = Transform("app.name=example\napp.it=can't\n", "app.output=quotednamespace\n");

        sink.Written["app.sh"].ShouldBe("name='example'\nit='can'\\''t'\n");
    }

    // ---- Section 14.1 empty views ------------------------------------------------------------

    [Test]
    public void AnOutputWhoseSelectorMatchesNothingProducesAnEmptyFile()
    {
        // Section 14.1 plans an instance "even when no data path currently matches its literal
        // prefix", and Section 24 makes a text output with no content zero bytes rather than one LF.
        var (result, sink) = Transform("other.name=example\n", "app.output=namespace\n");

        result.ExitCode.ShouldBe(0);
        sink.Written["app.properties"].ShouldBe(string.Empty);
    }

    // ---- Section 15.1 refusal ----------------------------------------------------------------

    [Test]
    public void AFormatOutsideThisVersionDeclinesRatherThanGuessing()
    {
        var (result, sink) = Transform("app.name=example\n", "app.output=xml\n");

        result.State.ShouldBe(PipelineRunState.Unsupported);
        result.ExitCode.ShouldBeNull();
        result.Unsupported.ShouldNotBeNull();
        sink.Written.ShouldBeEmpty();
    }

    [Test]
    public void ARefusalPublishesNothingAtAllEvenForFormatsThatAreSupported()
    {
        // A partial run is the failure this gate exists to prevent: publishing `app.properties`
        // and silently dropping `app.xml` would leave a plausible tree that asserts less than the
        // scheme asked for.
        var (result, sink) = Transform("app.name=example\n", "app.output=namespace,xml\n");

        result.State.ShouldBe(PipelineRunState.Unsupported);
        sink.Written.ShouldBeEmpty();
    }

    [TestCase("app.name=${other.name}\napp.other.name=x\n", "app.output=namespace\n",
        TestName = "AReferenceDeclines")]
    [TestCase("app.0=x\napp.1=y\n", "app.output=namespace\n",
        TestName = "ASequenceDeclines")]
    public void AnUnimplementedFeatureDeclines(string input, string scheme)
    {
        var (result, sink) = Transform(input, scheme);

        result.State.ShouldBe(PipelineRunState.Unsupported);
        sink.Written.ShouldBeEmpty();
    }

    [Test]
    public void AGapAndANonzeroBaseStillMakeASequence()
    {
        // Section 8.7 classifies a mapping as sequence-inferable when *all* surviving child names
        // are canonical ordering values, and allows "gaps and nonzero bases". `app.5` alone is
        // therefore a sequence, and Section 19.1 would emit it under a generated zero-based part —
        // a visibly different file from `5=y`. Guessing is exactly what the gate prevents.
        var (result, sink) = Transform("app.5=y\n", "app.output=namespace\n");

        result.State.ShouldBe(PipelineRunState.Unsupported);
        sink.Written.ShouldBeEmpty();
    }

    [Test]
    public void AMappingWithANonNumericSiblingIsNotASequence()
    {
        // "All its surviving concrete child names" — one non-numeric sibling makes the whole
        // mapping ordinary, so the numeric key keeps its own spelling.
        var (result, sink) = Transform("app.0=x\napp.name=y\n", "app.output=namespace\n");

        result.ExitCode.ShouldBe(0);
        sink.Written["app.properties"].ShouldBe("0=x\nname=y\n");
    }

    [Test]
    public void ALeadingZeroSpellingIsAnOrdinaryKey()
    {
        // Section 8.7: "leading-zero spellings such as `00` and `01` are ordinary mapping keys and
        // prevent sequence interpretation."
        var (result, sink) = Transform("app.00=x\n", "app.output=namespace\n");

        result.ExitCode.ShouldBe(0);
        sink.Written["app.properties"].ShouldBe("00=x\n");
    }

    [Test]
    public void ADecimalAboveTheSupportedMaximumIsAnOrdinaryKey()
    {
        // Section 8.7: a canonically spelled decimal above 9,223,372,036,854,775,807 "is an
        // ordinary mapping key and prevents sequence interpretation".
        var (result, sink) = Transform("app.9223372036854775808=x\n", "app.output=namespace\n");

        result.ExitCode.ShouldBe(0);
        sink.Written["app.properties"].ShouldBe("9223372036854775808=x\n");
    }

    // ---- Section 15.1 step ordering ----------------------------------------------------------

    [Test]
    public void AnInvocationRunsEveryStepInSectionOrder()
    {
        // PipelineRun rejects a step out of order, so a successful run is itself the assertion that
        // the driver expressed the Section 15.1 order. Reaching the last step proves all twenty ran.
        var (result, _) = Transform("app.name=example\n", "app.output=namespace\n");

        result.State.ShouldBe(PipelineRunState.Finished);
    }

    [Test]
    public void AnUnknownSchemeDirectiveIsABlockingError()
    {
        // The Section 22 registry gives SCHEME001 to an "unknown directive", at error severity, so
        // a misspelt directive fails the run rather than being ignored.
        var sink = new Sink();
        var result = Run(
            sink,
            new Sources(("in.txt", "app.name=example\n"), ("scheme.txt", "app.unknown=x\napp.output=namespace\n")),
            "-i", "in.txt", "-s", "scheme.txt");

        Codes(result).ShouldBe(["SCHEME001"]);
        result.ExitCode.ShouldBe(1);
        sink.Written.ShouldBeEmpty();
    }

    [Test]
    public void ADirectiveBindingToNoEffectiveOutputWarns()
    {
        // WARN009 covers a scheme directive that "binds to no effective output/path": `filename`
        // under a selector that declares no output has nothing to name.
        var sink = new Sink();
        var result = Run(
            sink,
            new Sources(("in.txt", "app.name=example\n"), ("scheme.txt", "other.filename=x.txt\napp.output=namespace\n")),
            "-i", "in.txt", "-s", "scheme.txt");

        Codes(result).ShouldContain("WARN009");
        result.ExitCode.ShouldBe(0);
    }

    [Test]
    public void IgnoreSuppressesTheDestinationEntirely()
    {
        var (result, sink) = Transform("app.name=example\n", "app.output=ignore\n");

        result.ExitCode.ShouldBe(0);
        result.Published.ShouldBe(0);
        sink.Written.ShouldBeEmpty();
    }
    [Test]
    public void TwoDestinationsDiagnoseInPublicationOrderNotPathOrder()
    {
        // Section 24 orders diagnostics "carrying a destination but no source ordering key... in
        // the Section 21.3 destination order", and Section 21.3 orders by publication key first,
        // which Section 16.1 derives from declaration order. `z` is declared first, so its SHELL001
        // precedes `a`'s even though 'a.sh' sorts before 'z.sh' as bytes.
        //
        // A stream ordered by path would look identical for either one alone. Two destinations that
        // both diagnose is the only shape that can tell the two orders apart, which is why this
        // case exists.
        var (result, _) = Transform(
            "z.a-b=1\na.a-b=1\n",
            "z.output=quotednamespace\nz.filename=z.sh\na.output=quotednamespace\na.filename=a.sh\n");

        Codes(result).ShouldBe(["SHELL001", "SHELL001"]);
        result.Diagnostics.Select(d => d.Destination).ShouldBe(["z.sh", "a.sh"]);
    }

    [Test]
    public void ADiagnosticNamingADestinationMustCarryItsOrder()
    {
        // Section 24 gives a diagnostic three possible groups, and a diagnostic that names a
        // destination without its Section 21.3 index belongs to none of them: it would silently
        // sort with the diagnostics that name nothing, by code bytes then path bytes. Refusing it
        // at construction is what keeps every emission site honest.
        var occurrence = DiagnosticCodes.Path002(
            DiagnosticPhase.Publication,
            "\u00A721.3",
            "the destination could not be written.",
            cardinalityKey: "out.txt",
            destination: "out.txt");

        Should.Throw<InvalidOperationException>(() => new BufferedDiagnostic(occurrence))
            .Message.ShouldContain("out.txt");

        Should.NotThrow(() => new BufferedDiagnostic(occurrence, DestinationOrder: 0));
    }
    [Test]
    public void AMergeConflictIsOrderedAtTheContributionThatCausedIt()
    {
        // Section 24: "A diagnostic's ordering key is the Section 4.7 stable ordering key of the
        // item it concerns", and Section 4.7 makes the CLI source ordinal that key's first
        // component. Section 24 keys a conflict between two contributions at
        // "the later of them", so this one carries the third source's key and orders after a
        // warning about the second.
        //
        // Carrying no key would put it in Section 24's third group, after every keyed diagnostic of
        // the same phase. Carrying the zero key would put it first. Keying it at the *earlier*
        // contribution would put it before the missing-file warning for a source that really is
        // earlier than the mistake. The warning sits between the two contributions so that all
        // three wrong answers move it.
        var sink = new Sink();
        var sources = new Sources(
            ("one.txt", "app.name=first\n"),
            ("two.txt", "app.name=second\n"),
            ("scheme.txt", "app.merge=error\n"));

        var result = Run(
            sink, sources, "-i", "one.txt", "-i", "gone.txt", "-i", "two.txt", "-s", "scheme.txt");

        Codes(result).ShouldBe(["WARN001", "TYPE001"]);
    }
    /// <summary>
    /// Section 6.4.3's `phase` member names where a condition arose, and Section 24 orders
    /// scheme-loading diagnostics before input-parsing ones. The same decoder reads both kinds of
    /// source, so a scheme that fails Section 7.4 must still report the scheme phase.
    /// </summary>
    [Test]
    public void ASchemeThatFailsToDecodeReportsTheSchemePhase()
    {
        // FF FE 00 00 is the Section 7.4 prohibited UTF-32 little-endian mark.
        var sink = new Sink();
        var sources = new Sources(
            ("in.txt", new UTF8Encoding(false).GetBytes("app.name=example\n")),
            ("scheme.txt", new byte[] { 0xFF, 0xFE, 0x00, 0x00 }));

        var result = Run(sink, sources, "-i", "in.txt", "-s", "scheme.txt");

        var occurrence = result.Diagnostics.Single(d => d.Code == "PARSE002");
        occurrence.Phase.ShouldBe(DiagnosticPhase.Scheme);
        occurrence.Source.ShouldBe("scheme.txt");
    }
}
