using Namespace2Xml.Diagnostics;
using Namespace2Xml.Pipeline;

namespace Namespace2Xml.Output;

/// <summary>Pipeline step 20: Section 21.3 direct publication.</summary>
/// <remarks>
/// <para>
/// Section 21.2 has already completed: every buffer here is finished and immutable. That is the
/// whole guarantee this tool offers — "all semantic work and serialization complete before the
/// first destination is opened" — and it is why this class never computes anything, only writes.
/// </para>
/// <para>
/// Section 21.3 attempts no rollback: "files already completed remain updated; the failing
/// destination may be partial; later destinations remain untouched". Publication therefore stops
/// at the first failure rather than continuing, so the untouched tail stays untouched.
/// </para>
/// </remarks>
public sealed class Publisher
{
    private readonly string outputRoot;
    private readonly DiagnosticBuffer diagnostics;
    private readonly IPublicationSink sink;

    /// <summary>Creates a publisher.</summary>
    /// <param name="outputRoot">The configured <c>--output</c> root.</param>
    /// <param name="diagnostics">The buffer publication faults accumulate in.</param>
    /// <param name="sink">The filesystem, or a test double standing in for it.</param>
    public Publisher(string outputRoot, DiagnosticBuffer diagnostics, IPublicationSink? sink = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputRoot);
        ArgumentNullException.ThrowIfNull(diagnostics);

        this.outputRoot = outputRoot;
        this.diagnostics = diagnostics;
        this.sink = sink ?? new FileSystemPublicationSink();
    }

    /// <summary>Publishes every planned output.</summary>
    /// <param name="outputs">The planned outputs, in any order.</param>
    /// <returns>Whether every destination was written.</returns>
    public bool TryPublish(IEnumerable<PlannedOutput> outputs)
    {
        ArgumentNullException.ThrowIfNull(outputs);

        var ordered = PlannedOutput.InPublicationOrder(outputs);

        // Section 21.1: "a zero-destination plan does not create it", so an invocation that plans
        // nothing leaves no trace at all rather than an empty directory.
        if (ordered.IsEmpty)
        {
            return true;
        }

        var created = new HashSet<string>(StringComparer.Ordinal);

        // Section 21.1: "fail with PATH001 before creating directories or opening destinations if
        // the host platform or filesystem cannot provide the primitives needed to establish secure
        // containment". The specification sanctions declaring the limit; what it does not sanction
        // is publishing anyway and hoping, so this precedes the creation of the root itself.
        if (!sink.SupportsSecureContainment)
        {
            ReportUncontainable(
                string.Empty,
                null,
                "this host does not provide the no-follow, handle-relative filesystem operations "
                + "Section 21.1 requires to keep every destination inside the output root.");

            return false;
        }

        if (!TryCreateDirectory(string.Empty, created, null, null))
        {
            return false;
        }

        for (var order = 0; order < ordered.Length; order++)
        {
            if (!TryPublishOne(ordered[order], order, created))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryPublishOne(PlannedOutput output, int order, HashSet<string> created)
    {
        var destination = new DestinationRef(output.Path.Canonical, order);
        var segments = output.Path.Canonical.Split('/');

        // Section 21.3 creates "each destination's missing parent directories immediately before
        // that destination, ancestor first": immediately before, so a failure leaves no directories
        // for destinations that were never reached.
        for (var i = 1; i < segments.Length; i++)
        {
            if (!TryCreateDirectory(string.Join('/', segments.Take(i)), created, destination, output))
            {
                return false;
            }
        }

        try
        {
            sink.Write(outputRoot, output.Path.Canonical, output.Buffer);
        }
        catch (UncontainableDestinationException e)
        {
            ReportUncontainable(output.Path.Canonical, destination, e.Message);
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ReportWriteFailure(output, destination, e.Message);
            return false;
        }

        return true;
    }

    private bool TryCreateDirectory(
        string relative, HashSet<string> created, DestinationRef? destination, PlannedOutput? output)
    {
        if (!created.Add(relative))
        {
            return true;
        }

        try
        {
            sink.CreateDirectory(outputRoot, relative);
        }
        catch (UncontainableDestinationException e)
        {
            ReportUncontainable(relative, destination, e.Message);
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ReportDirectoryFailure(relative, destination, output, e.Message);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Appendix B: an "uncontainable destination path" is <c>PATH001</c>. Nothing was opened,
    /// created, or written, so this is not the <c>PATH002</c> publication failure.
    /// </summary>
    private void ReportUncontainable(string relative, DestinationRef? destination, string message)
    {
        var named = destination?.Canonical ?? relative;

        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Path001(
                DiagnosticPhase.Publication,
                "\u00A721.1",
                message,
                cardinalityKey: named,
                destination: destination?.Canonical),
            DestinationOrder: destination?.Order));
    }

    private void ReportWriteFailure(PlannedOutput output, DestinationRef destination, string message) =>
        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Path002(
                DiagnosticPhase.Publication,
                "\u00A721.3",
                $"the destination '{output.Path}' could not be written: {message}",
                cardinalityKey: destination.Canonical,
                destination: destination.Canonical),
            DestinationOrder: destination.Order));

    private void ReportDirectoryFailure(
        string relative, DestinationRef? destination, PlannedOutput? output, string message)
    {
        var named = relative.Length == 0 ? "the output root" : $"the directory '{relative}'";

        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Path002(
                DiagnosticPhase.Publication,
                "\u00A721.3",
                $"{named} could not be created: {message}",
                cardinalityKey: destination?.Canonical ?? relative,
                destination: output is null ? null : destination?.Canonical),
            DestinationOrder: output is null ? null : destination?.Order));
    }
}

/// <summary>The filesystem operations Section 21.3 publication needs.</summary>
/// <remarks>
/// Publication is the one step whose correctness cannot be observed from its return value alone, so
/// it is expressed against an interface a test can watch. The order of the calls is the contract.
/// </remarks>
public interface IPublicationSink
{
    /// <summary>Creates one directory, if it does not already exist.</summary>
    /// <param name="root">The configured output root.</param>
    /// <param name="relative">The canonical relative directory, empty for the root itself.</param>
    void CreateDirectory(string root, string relative);

    /// <summary>Creates or truncates one destination, writes it, flushes it, and closes it.</summary>
    /// <param name="root">The configured output root.</param>
    /// <param name="relative">The canonical relative destination path.</param>
    /// <param name="buffer">The complete buffer.</param>
    void Write(string root, string relative, OutputBuffer buffer);

    /// <summary>
    /// Gets whether this host can guarantee Section 21.1 containment.
    /// </summary>
    /// <remarks>
    /// Section 21.1 requires failing "before creating directories or opening destinations if the
    /// host platform or filesystem cannot provide the primitives needed to establish secure
    /// containment". Discovering the absence when the first destination is opened is too late: the
    /// output root, and possibly some of its subdirectories, already exist by then. So the question
    /// is asked once, before anything is created.
    /// </remarks>
    bool SupportsSecureContainment { get; }
}

/// <summary>The real filesystem.</summary>
internal sealed class FileSystemPublicationSink : IPublicationSink
{
    private readonly SecureRootFactory factory = new();

    public bool SupportsSecureContainment => factory.SupportsSecureContainment;

    public void CreateDirectory(string root, string relative)
    {
        // Validate before creating, not after. Section 21.1 requires failing "before creating
        // directories or opening destinations" when containment cannot be established, and a
        // directory materialised outside the root is exactly the side effect this check exists to
        // prevent: reporting it afterwards reports damage already done.
        if (relative.Length == 0)
        {
            var path = Path.GetFullPath(root);
            RefuseNonDirectoryRoot(path);
            Directory.CreateDirectory(path);
            return;
        }

        using var secureRoot = factory.OpenRoot(root);
        var opened = OpenDirectories(secureRoot, relative.Split('/'));
        DisposeReverse(opened);
    }

    public void Write(string root, string relative, OutputBuffer buffer)
    {
        var segments = relative.Split('/');
        using var secureRoot = factory.OpenRoot(root);
        var opened = OpenDirectories(secureRoot, segments[..^1]);

        // Section 21.3: created or truncated "only after its complete byte buffer exists", then
        // flushed and closed "before beginning the next one". Disposing the stream here, rather
        // than at the end of publication, is what makes the next destination's write independent
        // of this one.
        try
        {
            var parent = opened.Count == 0 ? secureRoot : opened[^1];

            using var stream = parent.CreateOrTruncateChildFile(segments[^1]);

            buffer.WriteTo(stream);
            stream.Flush();
        }
        finally
        {
            DisposeReverse(opened);
        }
    }

    /// <summary>
    /// Section 21.1: "An existing non-directory output root is <c>PATH001</c>."
    /// </summary>
    /// <remarks>
    /// Left to <see cref="Directory.CreateDirectory(string)"/> this surfaces as an
    /// <see cref="IOException"/> and would be reported as a <c>PATH002</c> publication failure,
    /// which says the destination could not be written when the truth is that no destination can be
    /// placed under this root at all. The condition is therefore recognized before creation is
    /// attempted, where the specification puts it.
    /// </remarks>
    private static void RefuseNonDirectoryRoot(string path)
    {
        if (File.Exists(path))
        {
            throw new UncontainableDestinationException(
                $"the output root '{path}' exists and is not a directory.");
        }
    }

    private static List<ISecureDirectory> OpenDirectories(ISecureDirectory root, string[] components)
    {
        var opened = new List<ISecureDirectory>();
        var current = root;

        try
        {
            foreach (var component in components)
            {
                var child = current.OpenOrCreateChildDirectory(component);
                opened.Add(child);
                current = child;
            }

            return opened;
        }
        catch
        {
            DisposeReverse(opened);
            throw;
        }
    }

    private static void DisposeReverse(List<ISecureDirectory> directories)
    {
        for (var i = directories.Count - 1; i >= 0; i--)
        {
            directories[i].Dispose();
        }
    }
}

/// <summary>
/// A destination that cannot be placed inside the output root, whether because it resolves outside
/// it or because reaching it would follow a link.
/// </summary>
/// <remarks>
/// Appendix B maps "invalid, escaping, insecure, traversal, portability-key-colliding, or
/// uncontainable destination path" to <c>PATH001</c> and reserves <c>PATH002</c> for a "destination
/// open, create, write, flush, or close failure". A refusal is not a failure to write: nothing was
/// attempted. Reporting one as the other tells a reader the filesystem misbehaved when in fact the
/// tool declined, which is the difference between retrying and rewriting the scheme. The type
/// derives from <see cref="IOException"/> so that a caller catching the broad case still catches it,
/// and every such caller must test for this type first.
/// </remarks>
public sealed class UncontainableDestinationException : IOException
{
    /// <summary>Creates the exception.</summary>
    public UncontainableDestinationException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">Why the destination cannot be contained.</param>
    public UncontainableDestinationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">Why the destination cannot be contained.</param>
    /// <param name="innerException">The underlying failure.</param>
    public UncontainableDestinationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
