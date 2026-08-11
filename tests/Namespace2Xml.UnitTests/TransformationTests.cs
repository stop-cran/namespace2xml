using System.Collections.Immutable;
using System.Globalization;
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
    internal sealed class Sources : ISourceReader
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
    internal sealed class Sink : IPublicationSink
    {
        public Dictionary<string, string> Written { get; } = new(StringComparer.Ordinal);

        public List<string> Directories { get; } = [];

        /// <summary>An in-memory sink has no filesystem to be escaped from.</summary>
        public bool SupportsSecureContainment => true;

        public void CreateDirectory(string root, string relative) => Directories.Add(relative);

        public void Write(string root, string relative, OutputBuffer buffer) =>
            Written[relative] = new UTF8Encoding(false).GetString(buffer.ToArray());
    }

    internal static TransformationResult Run(Sink sink, Sources sources, params string[] arguments)
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
    public void ACapabilityOutsideThisVersionDeclinesRatherThanGuessing()
    {
        // Section 15 names XML among the formats a scheme file may use and never says what an XML
        // scheme projects to. This build declines rather than picking one of the three available
        // readings, and says so instead of handing the file to the Section 8.1 parser and
        // reporting the wrong contract against a file that is already correct.
        var sink = new Sink();
        var sources = new Sources(
            ("in.txt", "app.name=example\n"),
            ("scheme.xml", "<app><output>namespace</output></app>\n"));
        var result = Run(sink, sources, "-i", "in.txt", "-s", "scheme.xml");

        result.State.ShouldBe(PipelineRunState.Unsupported);
        result.ExitCode.ShouldBeNull();
        result.Unsupported.ShouldNotBeNull();
        sink.Written.ShouldBeEmpty();
    }

    [Test]
    public void ABlockingSchemeErrorPublishesNothingIncludingTheInstancesThatAreWellFormed()
    {
        // A partial run is the failure this gate exists to prevent: publishing `app.properties`
        // and silently dropping the rejected work would leave a plausible tree that asserts less
        // than the scheme asked for. `app` is well-formed and shares nothing with `other` but the
        // scheme file; Section 15.4 still lets the phase finish its independent checks, and
        // Section 6.2 then publishes nothing at all.
        var (result, sink) = Transform(
            "app.name=example\nother.name=example\n",
            "app.output=namespace\nother.output=namespace\nother.*.type=arr*y\n");

        result.ExitCode.ShouldBe(1);
        Codes(result).ShouldContain("SCHEME001");
        sink.Written.ShouldBeEmpty();
    }

    /// <summary>
    /// Section 12.1: "Legacy unnamed captures are substituted positionally", and a scheme
    /// directive's value "is decided the same way, from the captures its selector defines". One
    /// wildcard <c>key</c> rule therefore names a different field at every path it matches. The
    /// expected projection is Section 16.5's own worked example, whose <c>a.key=name</c> this
    /// reaches by substitution instead.
    /// </summary>
    [Test]
    public void ACaptureIsSubstitutedIntoAKeyValue()
    {
        var (result, sink) = Transform(
            "app.a.b.x=1\napp.a.c.x=2\n", "app.output=namespace\napp.*.key=n*me\n");

        result.State.ShouldBe(PipelineRunState.Finished);
        sink.Written["app.properties"]
            .ShouldBe("a.0.name=b\na.0.x=1\na.1.name=c\na.1.x=2\n");
    }

    /// <summary>
    /// Section 12.1 substitutes into every directive value whose captures are bound where the value
    /// is read, not into <c>filename</c> alone. <c>root</c> and <c>delimiter</c> are the two the
    /// clause names explicitly, and each concrete instance gets its own captures.
    /// </summary>
    [Test]
    public void ACaptureIsSubstitutedIntoRootAndDelimiter()
    {
        var (result, sink) = Transform(
            "app.db.x=1\napp.web.x=2\n",
            "app.*.output=namespace\napp.*.root=r*\napp.*.delimiter=-*-\n");

        result.State.ShouldBe(PipelineRunState.Finished);
        sink.Written["app.db.properties"].ShouldBe("rdb-db-x=1\n");
        sink.Written["app.web.properties"].ShouldBe("rweb-web-x=2\n");
    }

    /// <summary>
    /// Section 12.1: "in a scheme whose selector contains no wildcard, <c>*</c> in a
    /// <c>filename</c>, <c>root</c>, or <c>delimiter</c> value is literal text". The decision is
    /// made before the value is lexed, so nothing here is a capture at all — and Section 21 then
    /// escapes the literal asterisk it produced, unconditionally.
    /// </summary>
    [Test]
    public void AnAsteriskUnderALiteralSelectorIsLiteralText()
    {
        var (result, sink) = Transform(
            "app.x=1\n", "app.output=namespace\napp.root=r*\n");

        result.State.ShouldBe(PipelineRunState.Finished);
        sink.Written["app.properties"].ShouldBe("r\\*.x=1\n");
    }

    /// <summary>
    /// Section 12.1 excludes <c>type</c> from capture substitution, and says the exclusion belongs
    /// to the directive rather than to the selector, so an unescaped asterisk in a <c>type</c>
    /// value is literal text under a wildcard selector exactly as it is under a literal one. Section
    /// 16.6 closes the value to a keyword set, so the declaration is a blocking Section 22 scheme
    /// error rather than a request the tool declines. The same declaration with a keyword value
    /// still runs to completion.
    /// </summary>
    [Test]
    public void AnAsteriskInATypeValueIsAnInvalidTypeRatherThanACapture()
    {
        var (rejected, _) = Transform(
            "app.a.name=x\napp.a.v=1\n", "app.output=namespace\napp.*.type=arr*y\n");

        rejected.ExitCode.ShouldBe(1);
        Codes(rejected).ShouldContain("SCHEME001");

        var (accepted, sink) = Transform(
            "app.a.name=x\napp.a.v=1\n", "app.output=json\napp.*.type=array\n");

        accepted.State.ShouldBe(PipelineRunState.Finished);
        sink.Written.ShouldContainKey("app.json");
    }

    /// <summary>
    /// Section 12.1 excludes <c>output</c> for a reason the implementation can feel: <c>output</c>
    /// creates the instance instead of binding to one, so the step 13 expansion that supplies every
    /// other instance-scoped directive's captures has nothing to bind its value against. Before the
    /// exclusion was written down the run died with an unhandled <c>NullReferenceException</c>,
    /// which Section 6.3 forbids as a way for a user-caused condition to surface. The value is
    /// literal text instead, and Section 16.1's format list does not contain "*". The declaration
    /// with a format name still runs.
    /// </summary>
    [Test]
    public void AnAsteriskInAnOutputValueIsAnInvalidFormatRatherThanACapture()
    {
        var (rejected, refused) = Transform("a.json.b=hello\n", "a.*.output=*\n");

        rejected.ExitCode.ShouldBe(1);
        Codes(rejected).ShouldContain("SCHEME001");
        refused.Written.ShouldBeEmpty();

        var (accepted, sink) = Transform("a.json.b=hello\n", "a.*.output=json\n");

        accepted.State.ShouldBe(PipelineRunState.Finished);
        sink.Written.ShouldContainKey("a.json.json");
    }

    /// <summary>
    /// Section 13.1: a reference "resolves only the scalar or null payload stored at that exact
    /// canonical path", and Section 13.2 gives a value that is exactly one reference the referent's
    /// own kind. The referring entry is still emitted at its own path.
    /// </summary>
    [Test]
    public void AReferenceIsReplacedByTheReferentsValue()
    {
        var (result, sink) = Transform(
            "app.name=${app.other.name}\napp.other.name=x\n", "app.output=namespace\n");

        result.State.ShouldBe(PipelineRunState.Finished);
        sink.Written["app.properties"].ShouldBe("name=x\nother.name=x\n");
    }

    /// <summary>
    /// Section 13.1: "no match is a missing-reference error". A blocking error publishes nothing,
    /// so a run that could not name every value writes no half-resolved file.
    /// </summary>
    [Test]
    public void AMissingReferenceIsReference002AndPublishesNothing()
    {
        var (result, sink) = Transform("app.name=${nowhere}\n", "app.output=namespace\n");

        Codes(result).ShouldContain("REFERENCE002");
        sink.Written.ShouldBeEmpty();
    }

    /// <summary>
    /// Section 13.1: "References may be recursive" and "cycles are blocking errors with a complete
    /// source chain."
    /// </summary>
    [Test]
    public void AReferenceCycleIsReference003()
    {
        var (result, sink) = Transform(
            "app.a=${app.b}\napp.b=${app.a}\n", "app.output=namespace\n");

        Codes(result).ShouldContain("REFERENCE003");
        sink.Written.ShouldBeEmpty();
    }

    /// <summary>
    /// Section 14.4: defects "in entries unreachable from every concrete output instance do not
    /// fail the run". A broken reference outside every selector is such an entry.
    /// </summary>
    /// <remarks>
    /// This is the clause that forbids resolving the whole model and filtering the diagnostics
    /// afterwards: the filtered run would produce identical output here and a different exit code.
    /// </remarks>
    [Test]
    public void ABrokenReferenceOutsideEverySelectorDoesNotFailTheRun()
    {
        var (result, sink) = Transform(
            "app.name=x\nunused.name=${nowhere}\n", "app.output=namespace\n");

        result.State.ShouldBe(PipelineRunState.Finished);
        Codes(result).ShouldNotContain("REFERENCE002");
        sink.Written["app.properties"].ShouldBe("name=x\n");
    }

    [TestCase("scheme.xml", TestName = "ALowercaseXmlSchemeDeclines")]
    [TestCase("scheme.XML", TestName = "AnUppercaseXmlSchemeDeclines")]
    public void AnXmlSchemeFileDeclinesRatherThanBeingReadAsANamespaceProfile(string path)
    {
        // Section 15: "Scheme files may use the same case-insensitive format extensions as input
        // files for compatibility." It names XML and never gives a projection rule for one, so the
        // question is which way this build says so. Handing the file to the namespace parser
        // reports PARSE001 against Section 8.1 -- naming a contract the file was never written to,
        // and telling the author to add an '=' to syntax that is already correct for the contract
        // Section 15 points them at. A refusal names the capability instead.
        var sink = new Sink();
        var sources = new Sources(
            ("in.txt", "app.name=example\n"),
            (path, "<app><output>namespace</output></app>\n"));

        var result = Run(sink, sources, "-i", "in.txt", "-s", path);

        result.State.ShouldBe(PipelineRunState.Unsupported);
        result.ExitCode.ShouldBeNull();
        result.Unsupported.ShouldNotBeNull().Spec.ShouldBe("\u00A715");
        Codes(result).ShouldNotContain("PARSE001");
        sink.Written.ShouldBeEmpty();
    }

    /// <summary>
    /// Section 15 lets a scheme use any input format's extension, and a JSON or YAML one is read
    /// rather than declined. Each spelling below is the same two directives, so the run finishes
    /// and writes the file the scheme asked for.
    /// </summary>
    [TestCase("scheme.json", "{\"app\":{\"output\":\"namespace\"}}\n", TestName = "AJsonSchemeIsRead")]
    [TestCase("scheme.yaml", "app:\n  output: namespace\n", TestName = "AYamlSchemeIsRead")]
    [TestCase("scheme.yml", "app:\n  output: namespace\n", TestName = "AYmlSchemeIsRead")]
    [TestCase("scheme.JSON", "{\"app\":{\"output\":\"namespace\"}}\n", TestName = "AnUppercaseJsonSchemeIsRead")]
    public void AStructuredSchemeFileIsReadRatherThanDeclined(string path, string text)
    {
        var sink = new Sink();
        var sources = new Sources(("in.txt", "app.name=example\n"), (path, text));

        var result = Run(sink, sources, "-i", "in.txt", "-s", path);

        result.State.ShouldBe(PipelineRunState.Finished);
        result.ExitCode.ShouldBe(0);
        sink.Written["app.properties"].ShouldBe("name=example\n");
    }

    /// <summary>
    /// A scheme file whose extension is not one Section 7.1 names is a namespace profile, however
    /// unusual the extension looks. Only the four structured extensions decline.
    /// </summary>
    [TestCase("scheme.ini")]
    [TestCase("scheme.sh")]
    [TestCase("scheme")]
    public void AnUnstructuredExtensionIsStillANamespaceProfileScheme(string path)
    {
        var sink = new Sink();
        var sources = new Sources(
            ("in.txt", "app.name=example\n"),
            (path, "app.output=namespace\n"));

        Run(sink, sources, "-i", "in.txt", "-s", path).ExitCode.ShouldBe(0);
        sink.Written.ShouldNotBeEmpty();
    }

    // ---- Section 7.3: the global input stream ------------------------------------------------

    /// <summary>
    /// Section 7.3 evaluates the global budgets "after all independently readable sources finish
    /// their parse attempt", over one stream of "all scheme files in <c>-s</c> order, then all
    /// input files in <c>-i</c> order". A file that was read and then failed to parse finished its
    /// parse attempt, so its bytes are in that stream and shift where the bound is crossed.
    /// </summary>
    /// <remarks>
    /// The arithmetic is stated rather than assumed. The scheme is 21 bytes, the malformed JSON is
    /// 5, and the trailing profile is 8, so the running totals over the Section 7.3 stream are 21,
    /// 26 and 34. A limit of 30 is crossed by the third source and by no earlier one. Drop the
    /// malformed source from the stream and the totals become 21 and 29, which crosses nothing at
    /// all -- so this asserts the bound is reported, and reported against the source that actually
    /// crossed it.
    /// </remarks>
    [Test]
    public void ASourceThatFailedToParseStillOccupiesTheGlobalInputStream()
    {
        const string Scheme = "app.output=namespace\n";
        const string Malformed = "{\"a\":";
        const string Trailing = "app.b=1\n";

        Encoding.UTF8.GetByteCount(Scheme).ShouldBe(21);
        Encoding.UTF8.GetByteCount(Malformed).ShouldBe(5);
        Encoding.UTF8.GetByteCount(Trailing).ShouldBe(8);

        var sink = new Sink();
        var sources = new Sources(
            ("scheme.txt", Scheme), ("bad.json", Malformed), ("b.txt", Trailing));

        var result = Run(
            sink,
            sources,
            "-s", "scheme.txt",
            "-i", "bad.json",
            "-i", "b.txt",
            "--max-total-input-bytes", "30");

        Codes(result).ShouldContain("LIMIT001");
        result.Diagnostics.Single(d => d.Code == "LIMIT001").Source.ShouldBe("b.txt");
    }

    /// <summary>
    /// The same rule for a source that never decoded. Section 7.3 says "independently readable",
    /// and a file whose bytes were read is readable whatever those bytes turned out to mean.
    /// </summary>
    /// <remarks>
    /// The malformed source is five bytes of which the last is a bare UTF-8 continuation byte, so
    /// it fails Section 7.4 decoding rather than parsing. The arithmetic is otherwise the fixture
    /// above: 21, then 26, then 34, against a limit of 30.
    /// </remarks>
    [Test]
    public void ASourceThatFailedToDecodeStillOccupiesTheGlobalInputStream()
    {
        var sink = new Sink();
        var sources = new Sources(
            ("scheme.txt", new UTF8Encoding(false).GetBytes("app.output=namespace\n")),
            ("bad.txt", [(byte)'a', (byte)'=', (byte)'1', (byte)'\n', 0x80]),
            ("b.txt", new UTF8Encoding(false).GetBytes("app.b=1\n")));

        var result = Run(
            sink,
            sources,
            "-s", "scheme.txt",
            "-i", "bad.txt",
            "-i", "b.txt",
            "--max-total-input-bytes", "30");

        Codes(result).ShouldContain("LIMIT001");
        result.Diagnostics.Single(d => d.Code == "LIMIT001").Source.ShouldBe("b.txt");
    }

    /// <summary>
    /// The same three sources under a limit that admits all of them cross nothing, so the fixture
    /// above is reporting a bound rather than a source count.
    /// </summary>
    [Test]
    public void TheSameSourcesUnderASufficientLimitCrossNothing()
    {
        var sink = new Sink();
        var sources = new Sources(
            ("scheme.txt", "app.output=namespace\n"),
            ("bad.json", "{\"a\":"),
            ("b.txt", "app.b=1\n"));

        var result = Run(
            sink,
            sources,
            "-s", "scheme.txt",
            "-i", "bad.json",
            "-i", "b.txt",
            "--max-total-input-bytes", "34");

        Codes(result).ShouldNotContain("LIMIT001");
    }

    [Test]
    public void AGapAndANonzeroBaseStillMakeASequence()
    {
        // Section 8.7 classifies a mapping as sequence-inferable when *all* surviving child names
        // are canonical ordering values, and allows "gaps and nonzero bases". `app.5` alone is
        // therefore a sequence with one item at ordering value 5, and Section 5.4 makes namespace
        // output "display fresh dense indices", so the file reads `0=y` and not `5=y`.
        var (result, sink) = Transform("app.5=y\n", "app.output=namespace\n");

        result.ExitCode.ShouldBe(0);
        sink.Written["app.properties"].ShouldBe("0=y\n");
    }

    [Test]
    public void ADenseIndexIsARenderingArtifactAndNotAnAddress()
    {
        // Section 25.12: "Dense output positions are serialization artifacts and never become new
        // scheme, wildcard, or reference addresses." The mask therefore names the stable ordering
        // value 5, and the item it removes is the one displayed at position 0.
        var (result, sink) = Transform("app.5=y\napp.9=z\n!app.5\n", "app.output=namespace\n");

        result.ExitCode.ShouldBe(0);
        sink.Written["app.properties"].ShouldBe("0=z\n");
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

    // ---- Depth --------------------------------------------------------------------------------

    /// <summary>
    /// A document at the documented <c>--max-depth</c> ceiling completes. Several phases walk the
    /// overlay tree by recursion, so the depth a run survives is a property of the stack it is
    /// given, and <see cref="Transformation.Run"/> sizes that stack rather than inheriting whatever
    /// the host chose. Without this the ceiling is a claim no test stands behind: nothing else in
    /// the suite nests a document deeply enough to touch it.
    /// </summary>
    /// <remarks>
    /// The assertion is that the run reaches a Section 6.3 outcome at all. A stack overflow is not
    /// a failing assertion — it terminates the test host — so this test failing and this test
    /// vanishing look different, which is the point.
    /// </remarks>
    [Test]
    public void ADocumentAtTheDepthCeilingCompletes()
    {
        const int depth = (int)LimitValue.MaxDepthCeiling - 1;

        var document = new StringBuilder(depth * 8);

        for (var i = 0; i < depth; i++)
        {
            document.Append("<a>");
        }

        document.Append('v');

        for (var i = 0; i < depth; i++)
        {
            document.Append("</a>");
        }

        var sink = new Sink();
        var sources = new Sources(
            ("in.xml", document.ToString()),
            ("scheme.txt", "a.output=namespace\na.filename=out\n"));

        var result = Run(
            sink,
            sources,
            "-i",
            "in.xml",
            "-s",
            "scheme.txt",
            "--max-depth",
            LimitValue.MaxDepthCeiling.ToString(CultureInfo.InvariantCulture),
            "--max-nodes",
            "10000000");

        result.Diagnostics.ShouldNotContain(d => d.Code == "LIMIT001");
        result.ExitCode.ShouldBe(0);
    }
}
