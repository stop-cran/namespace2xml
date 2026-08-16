namespace Namespace2Xml.Pipeline;

/// <summary>How an attempt to read one source ended.</summary>
public enum SourceReadStatus
{
    /// <summary>The bytes were read.</summary>
    Read,

    /// <summary>
    /// The path does not exist. Section 7.2 gives this case "warning-and-ignore behavior": it
    /// "emits a warning containing its resolved path", "contributes no data", and "does not by
    /// itself cause failure".
    /// </summary>
    Missing,

    /// <summary>
    /// The path exists but could not be read. Section 7.2: "A path that exists but is unreadable,
    /// is a directory where a file is required, changes incompatibly while being read, or fails
    /// for another I/O reason is a blocking <c>PARSE001</c> source error."
    /// </summary>
    Failed,
}

/// <summary>The result of reading one source.</summary>
/// <param name="Status">How the attempt ended.</param>
/// <param name="Bytes">The raw bytes, when <see cref="SourceReadStatus.Read"/>.</param>
/// <param name="Reason">Why the read failed, when <see cref="SourceReadStatus.Failed"/>.</param>
/// <param name="ResolvedPath">
/// The path as the diagnostic reports it. Section 7.2 requires the missing-file warning to contain
/// "its resolved path", so resolution happens where the read is attempted rather than at each
/// reporting site.
/// </param>
public sealed record SourceRead(
    SourceReadStatus Status,
    byte[]? Bytes,
    string? Reason,
    string ResolvedPath);

/// <summary>Reads the byte content of a source named on the command line.</summary>
/// <remarks>
/// An interface only so that a test can produce a read failure without arranging one on a real
/// filesystem. Section 7.2 distinguishes missing from unreadable, and a test that cannot produce
/// the second cannot show that the two are distinguished.
/// </remarks>
public interface ISourceReader
{
    /// <summary>Reads one source.</summary>
    /// <param name="path">The path as written on the command line.</param>
    SourceRead Read(string path);
}

/// <summary>Reads sources from the filesystem.</summary>
public sealed class FileSystemSourceReader : ISourceReader
{
    /// <inheritdoc/>
    public SourceRead Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string resolved;

        try
        {
            resolved = Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // A path the host cannot even resolve exists nowhere, but it is not "missing" in
            // Section 7.2's sense either: nothing could ever appear at it. Reporting it as a read
            // failure keeps the blocking outcome that a malformed path deserves.
            return new SourceRead(SourceReadStatus.Failed, null, e.Message, path);
        }

        // Section 7.2 gives a directory where a file is required the blocking treatment, not the
        // warning: it is a path that exists.
        if (Directory.Exists(resolved))
        {
            return new SourceRead(
                SourceReadStatus.Failed,
                null,
                "the path names a directory, and a file is required here.",
                resolved);
        }

        try
        {
            return new SourceRead(SourceReadStatus.Read, File.ReadAllBytes(resolved), null, resolved);
        }
        catch (FileNotFoundException)
        {
            return new SourceRead(SourceReadStatus.Missing, null, null, resolved);
        }
        catch (DirectoryNotFoundException)
        {
            return new SourceRead(SourceReadStatus.Missing, null, null, resolved);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new SourceRead(SourceReadStatus.Failed, null, e.Message, resolved);
        }
    }
}
