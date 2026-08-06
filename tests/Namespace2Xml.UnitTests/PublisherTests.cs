using System.Collections.ObjectModel;
using System.Diagnostics;
using Namespace2Xml.Budgets;
using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Output;
using Namespace2Xml.Pipeline;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>Pipeline step 20: Section 21.3 direct publication.</summary>
/// <remarks>
/// Publication is observed through the order of its filesystem calls, because that order is the
/// contract: Section 21.3 fixes destination order, directory creation "immediately before" each
/// destination, and flush-and-close before the next one. Every expectation is authored from the
/// specification clause named in the test.
/// </remarks>
[TestFixture]
public class PublisherTests
{
    private DiagnosticBuffer diagnostics = null!;
    private RecordingSink sink = null!;

    [SetUp]
    public void SetUp()
    {
        diagnostics = new DiagnosticBuffer();
        sink = new RecordingSink();
    }

    private static OutputBuffer Buffer(string text)
    {
        var writer = new OutputBufferWriter(new GlobalBudget(ResourceLimits.Defaults));

        writer.TryWrite(text).ShouldBeTrue();

        return writer.Build();
    }

    private static PlannedOutput Planned(
        string path,
        long declarationOrder = 0,
        int formatOrdinal = 0,
        long wildcardOrder = 0,
        string selector = "")
    {
        DestinationPathComposer.TryCompose(path, out var composed, out _).ShouldBeTrue();

        return new PlannedOutput(
            composed!,
            new FoldKey(declarationOrder, formatOrdinal, wildcardOrder, selector),
            Buffer(path));
    }

    private bool Publish(params PlannedOutput[] outputs) =>
        new Publisher("root", diagnostics, sink).TryPublish(outputs);

    // ---- Section 21.3 destination order -----------------------------------------------------------

    /// <summary>
    /// Section 21.3 orders destinations "by the minimum §17.5 fold key ... then by canonical
    /// relative path compared as unsigned UTF-8 bytes".
    /// </summary>
    [Test]
    public void DestinationsAreWrittenInPublicationKeyOrder()
    {
        Publish(
            Planned("c.json", declarationOrder: 2),
            Planned("a.json", declarationOrder: 1),
            Planned("b.json", declarationOrder: 0))
            .ShouldBeTrue();

        sink.Writes.ShouldBe(["b.json", "a.json", "c.json"]);
    }

    /// <summary>
    /// The path tie-break is not decoration: one declaration can write several destinations, and
    /// without it their order would follow whatever collection happened to hold them.
    /// </summary>
    [Test]
    public void DestinationsSharingAKeyAreOrderedByCanonicalPath()
    {
        Publish(Planned("b.json"), Planned("a.json"), Planned("A.json")).ShouldBeTrue();

        // Uppercase sorts before lowercase as unsigned UTF-8 bytes.
        sink.Writes.ShouldBe(["A.json", "a.json", "b.json"]);
    }

    /// <summary>
    /// Section 17.5 orders the fold key by declaration, then format ordinal within one
    /// comma-separated <c>output</c> value, then wildcard match order, then selector bytes. The
    /// order of the components matters: a later format ordinal loses to a later wildcard match,
    /// because format is compared first.
    /// </summary>
    [Test]
    public void TheFoldKeyOrdersItsComponentsFromCoarsestToFinest()
    {
        Publish(
            Planned("d.json", declarationOrder: 1),
            Planned("c.json", selector: "z"),
            Planned("b.json", wildcardOrder: 1),
            Planned("a.json", formatOrdinal: 1))
            .ShouldBeTrue();

        // c=(0,0,0,"z") < b=(0,0,1,"") < a=(0,1,0,"") < d=(1,0,0,"").
        sink.Writes.ShouldBe(["c.json", "b.json", "a.json", "d.json"]);
    }

    /// <summary>
    /// Section 17.5 uses "concrete selector encoded as UTF-8 and compared by unsigned-byte ordinal
    /// order as the final deterministic tie-breaker", so two contributions of one declaration and
    /// format still have a defined order.
    /// </summary>
    [Test]
    public void TheSelectorIsTheFinalTieBreaker()
    {
        Publish(Planned("a.json", selector: "z"), Planned("b.json", selector: "a")).ShouldBeTrue();

        sink.Writes.ShouldBe(["b.json", "a.json"]);
    }

    // ---- Section 21.1 and 21.3 directory creation ------------------------------------------------

    /// <summary>
    /// Section 21.1: the output root "is included as the first directory in the validated creation
    /// plan" when at least one destination is planned.
    /// </summary>
    [Test]
    public void TheOutputRootIsCreatedFirst()
    {
        Publish(Planned("a.json")).ShouldBeTrue();

        sink.Calls[0].ShouldBe("mkdir:");
    }

    /// <summary>
    /// Section 21.1: "a zero-destination plan does not create it". An invocation that plans nothing
    /// leaves no trace, rather than an empty directory that looks like a partial run.
    /// </summary>
    [Test]
    public void AZeroDestinationPlanTouchesNothing()
    {
        new Publisher("root", diagnostics, sink).TryPublish([]).ShouldBeTrue();

        sink.Calls.ShouldBeEmpty();
        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 21.3 creates "each destination's missing parent directories immediately before that
    /// destination, ancestor first". Immediately before, so a failure leaves no directories behind
    /// for destinations that were never reached.
    /// </summary>
    [Test]
    public void ParentDirectoriesAreCreatedAncestorFirstImmediatelyBeforeTheirDestination()
    {
        Publish(Planned("a/b/c.json")).ShouldBeTrue();

        sink.Calls.ShouldBe(["mkdir:", "mkdir:a", "mkdir:a/b", "write:a/b/c.json"]);
    }

    /// <summary>
    /// A directory shared by two destinations is created once. Creating it again would be harmless
    /// but would make the call sequence depend on how many destinations happened to share it.
    /// </summary>
    [Test]
    public void ASharedDirectoryIsCreatedOnce()
    {
        Publish(Planned("a/x.json"), Planned("a/y.json")).ShouldBeTrue();

        sink.Calls.ShouldBe(["mkdir:", "mkdir:a", "write:a/x.json", "write:a/y.json"]);
    }

    // ---- Section 21.2 and 21.3 write discipline --------------------------------------------------

    /// <summary>
    /// Section 21.3 flushes and closes each destination "before beginning the next one", so no two
    /// destinations are ever open at once and a later failure cannot corrupt an earlier file.
    /// </summary>
    [Test]
    public void EachDestinationIsClosedBeforeTheNextBegins()
    {
        Publish(Planned("a.json"), Planned("b.json", declarationOrder: 1)).ShouldBeTrue();

        sink.Concurrent.ShouldBe(0);
        sink.Calls.ShouldBe(["mkdir:", "write:a.json", "write:b.json"]);
    }

    /// <summary>
    /// Section 21.2 requires the complete buffer to exist before the destination is created or
    /// truncated, which is what lets the tool promise that no error after the gate leaves a
    /// half-computed file.
    /// </summary>
    [Test]
    public void EachDestinationReceivesItsCompleteBuffer()
    {
        Publish(Planned("a.json")).ShouldBeTrue();

        sink.Contents["a.json"].ShouldBe("a.json");
    }

    // ---- Section 21.3 failure ---------------------------------------------------------------------

    /// <summary>
    /// Section 21.3: "no rollback is attempted. Files already completed remain updated; the failing
    /// destination may be partial; later destinations remain untouched." Publication therefore stops
    /// at the first failure, which is what keeps the untouched tail untouched.
    /// </summary>
    [Test]
    public void AFailureStopsPublicationAndLeavesEarlierFilesInPlace()
    {
        sink.FailOn = "b.json";

        Publish(
            Planned("a.json"),
            Planned("b.json", declarationOrder: 1),
            Planned("c.json", declarationOrder: 2))
            .ShouldBeFalse();

        sink.Writes.ShouldBe(["a.json", "b.json"]);
        sink.Contents.ShouldContainKey("a.json");
        sink.Contents.ShouldNotContainKey("c.json");
    }

    /// <summary>
    /// Section 21.3: "an external storage failure is reported immediately". The diagnostic names the
    /// destination, because that is the file the operator has to inspect.
    /// </summary>
    [Test]
    public void AStorageFailureIsReportedAgainstItsDestination()
    {
        sink.FailOn = "a.json";

        Publish(Planned("a.json")).ShouldBeFalse();

        var diagnostic = diagnostics.Drain().ShouldHaveSingleItem();

        diagnostic.Code.ShouldBe("PATH002");
        diagnostic.Destination.ShouldBe("a.json");
        diagnostic.Phase.ShouldBe(DiagnosticPhase.Publication);
    }

    /// <summary>A directory that cannot be created is the same class of failure as a file.</summary>
    [Test]
    public void ADirectoryFailureIsAlsoReported()
    {
        sink.FailOn = "mkdir:a";

        Publish(Planned("a/b.json")).ShouldBeFalse();

        diagnostics.Drain().ShouldHaveSingleItem().Code.ShouldBe("PATH002");
        sink.Writes.ShouldBeEmpty();
    }

    // ---- The real filesystem ---------------------------------------------------------------------

    /// <summary>
    /// Section 21.1 creates the output root as "the first directory in the validated creation plan"
    /// when it does not exist, and Section 24 writes the buffer's bytes exactly.
    /// </summary>
    [Test]
    public void PublishingCreatesTheRootAndWritesTheBytes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"n2x-{Guid.NewGuid():N}", "out");

        try
        {
            new Publisher(root, diagnostics).TryPublish([Planned("a/b.json")]).ShouldBeTrue();

            File.ReadAllBytes(Path.Combine(root, "a", "b.json"))
                .ShouldBe(System.Text.Encoding.UTF8.GetBytes("a/b.json"));

            diagnostics.Drain().ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(Directory.GetParent(root)!.FullName, recursive: true);
        }
    }

    /// <summary>
    /// Section 21.4 allows replacing an existing destination, and Section 21.3 creates or truncates
    /// it. Appending instead would make the output depend on what was already on disk, which the
    /// Section 24 promise of byte-identical output from identical inputs forbids.
    /// </summary>
    [Test]
    public void RepublishingTruncatesRatherThanAppends()
    {
        var root = Path.Combine(Path.GetTempPath(), $"n2x-{Guid.NewGuid():N}");

        try
        {
            new Publisher(root, diagnostics).TryPublish([Planned("a.json")]).ShouldBeTrue();
            new Publisher(root, diagnostics).TryPublish([Planned("a.json")]).ShouldBeTrue();

            File.ReadAllText(Path.Combine(root, "a.json")).ShouldBe("a.json");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Section 21.1 requires verifying "symbolic-link, junction, and reparse-point containment when
    /// opening each destination". Syntactic validation cannot see a link, so a path that was inside
    /// the root when its name was checked can still resolve outside it.
    /// </summary>
    [Test]
    public void ADestinationBehindALinkLeavingTheRootIsRefused()
    {
        var work = Path.Combine(Path.GetTempPath(), $"n2x-{Guid.NewGuid():N}");
        var root = Path.Combine(work, "out");
        var outside = Path.Combine(work, "elsewhere");
        var link = Path.Combine(root, "link");

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);

        try
        {
            if (!TryLinkDirectory(link, outside))
            {
                Assert.Ignore("this host does not permit creating directory links.");
            }

            new Publisher(root, diagnostics).TryPublish([Planned("link/x.json")]).ShouldBeFalse();

            diagnostics.Drain().ShouldHaveSingleItem().Code.ShouldBe("PATH002");
            File.Exists(Path.Combine(outside, "x.json")).ShouldBeFalse();
        }
        finally
        {
            // A recursive delete refuses to descend through the link, so the link goes first. This
            // removes the link alone and leaves its target, which the recursive delete then reaches
            // as an ordinary directory.
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }

            Directory.Delete(work, recursive: true);
        }
    }

    /// <summary>
    /// Section 21.1 requires opening destinations "through handle-relative or equivalent no-follow
    /// filesystem operations". Verifying the parent directory is not enough: the link can be the
    /// destination itself, and <see cref="FileMode.Create"/> through one writes to its target.
    /// </summary>
    /// <remarks>
    /// The link here is a directory link because that is the kind every supported host can create
    /// unprivileged; the guard it exercises reads the reparse point, not the kind. The diagnostic
    /// message is asserted rather than only the code, because a directory sitting at a destination
    /// also fails to open — with the guard removed the run still reports <c>PATH002</c>, just for
    /// the operating system's reason instead of this one.
    /// </remarks>
    [Test]
    public void ADestinationThatIsItselfALinkIsRefused()
    {
        var work = Path.Combine(Path.GetTempPath(), $"n2x-{Guid.NewGuid():N}");
        var root = Path.Combine(work, "out");
        var outside = Path.Combine(work, "elsewhere");
        var link = Path.Combine(root, "x.json");

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);

        try
        {
            if (!TryLinkDirectory(link, outside))
            {
                Assert.Ignore("this host does not permit creating directory links.");
            }

            new Publisher(root, diagnostics).TryPublish([Planned("x.json")]).ShouldBeFalse();

            var diagnostic = diagnostics.Drain().ShouldHaveSingleItem();

            diagnostic.Code.ShouldBe("PATH002");
            diagnostic.Message.ShouldContain("is a link to");
        }
        finally
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }

            Directory.Delete(work, recursive: true);
        }
    }

    /// <summary>
    /// Creating a symbolic link requires a privilege Windows withholds by default. A directory
    /// junction is also a reparse point and <c>ResolveLinkTarget</c> follows it, so it stands in for
    /// one there. Without some link the containment check has no witness at all, and a skipped test
    /// witnesses nothing.
    /// </summary>
    private static bool TryLinkDirectory(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }
        }

        using var process = Process.Start(
            new ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", link, target])
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

        process!.WaitForExit();
        return process.ExitCode == 0 && Directory.Exists(link);
    }

    private sealed class RecordingSink : IPublicationSink
    {
        private readonly List<string> calls = [];
        private readonly List<string> writes = [];
        private readonly Dictionary<string, string> contents = [];

        public string? FailOn { get; set; }

        public int Concurrent { get; private set; }

        public ReadOnlyCollection<string> Calls => calls.AsReadOnly();

        public ReadOnlyCollection<string> Writes => writes.AsReadOnly();

        public Dictionary<string, string> Contents => contents;

        public void CreateDirectory(string root, string relative)
        {
            calls.Add($"mkdir:{relative}");

            if (FailOn == $"mkdir:{relative}")
            {
                throw new IOException("refused");
            }
        }

        public void Write(string root, string relative, OutputBuffer buffer)
        {
            calls.Add($"write:{relative}");
            writes.Add(relative);

            Concurrent++;

            try
            {
                if (FailOn == relative)
                {
                    throw new IOException("refused");
                }

                using var stream = new MemoryStream();

                buffer.WriteTo(stream);
                contents[relative] = System.Text.Encoding.UTF8.GetString(stream.ToArray());
            }
            finally
            {
                Concurrent--;
            }
        }
    }
}
