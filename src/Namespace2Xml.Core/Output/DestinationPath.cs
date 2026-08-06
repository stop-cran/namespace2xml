using System.Diagnostics.CodeAnalysis;

namespace Namespace2Xml.Output;

/// <summary>
/// A Section 17.5 canonical destination path: the portable-encoded relative path, with <c>/</c>
/// separators, no <c>.</c> or <c>..</c> segments, and no redundant separators.
/// </summary>
/// <param name="Canonical">The canonical relative path.</param>
public sealed record DestinationPath(string Canonical)
{
    /// <summary>
    /// The Section 17.5 portability key: the canonical path with ASCII letters uppercased.
    /// </summary>
    /// <remarks>
    /// This is what detects a collision between two paths that differ only in case, which is a
    /// merge on Windows and two files on Linux. Section 17.5 makes it "a blocking <c>PATH001</c>
    /// collision rather than a merge" so the same inputs describe the same outputs everywhere.
    /// Portable segment encoding leaves only ASCII here, so uppercasing needs no culture.
    /// </remarks>
    public string PortabilityKey { get; } =
        Canonical.ToUpperInvariant();

    /// <summary>
    /// Section 21.3's tie-break: the canonical relative path compared as unsigned UTF-8 bytes.
    /// </summary>
    public static IComparer<DestinationPath> Utf8Bytes { get; } = new Comparer();

    /// <inheritdoc/>
    public override string ToString() => Canonical;

    private sealed class Comparer : IComparer<DestinationPath>
    {
        public int Compare(DestinationPath? x, DestinationPath? y) =>
            Overlay.Utf8Order.Compare(x?.Canonical, y?.Canonical);
    }
}

/// <summary>Composes a Section 16.2 <c>filename</c> into a Section 17.5 canonical path.</summary>
public static class DestinationPathComposer
{
    /// <summary>Composes a written relative path.</summary>
    /// <param name="written">The path as the scheme wrote it, with captures already substituted.</param>
    /// <param name="path">The canonical path, when this returns <see langword="true"/>.</param>
    /// <param name="violation">Why the path is <c>PATH001</c>, otherwise.</param>
    /// <remarks>
    /// Only separators "written literally in the scheme create directory hierarchy", so this splits
    /// its argument and the caller is responsible for having encoded anything a capture supplied.
    /// </remarks>
    public static bool TryCompose(
        string written,
        [NotNullWhen(true)] out DestinationPath? path,
        out string? violation)
    {
        ArgumentNullException.ThrowIfNull(written);

        path = null;

        if (!TryRejectNonRelative(written, out violation))
        {
            return false;
        }

        var segments = new List<string>();

        foreach (var segment in written.Split('/', '\\'))
        {
            // Section 16.2 prohibits statically written dot segments outright. A captured one would
            // have been encoded before reaching here, so anything still spelled '.' or '..' was
            // written in the scheme, and Section 21.1 rejects it "after filename expansion".
            if (PortableSegment.IsDotSegment(segment))
            {
                violation =
                    $"the path '{written}' contains a statically written '{segment}' segment, "
                    + "which Section 16.2 prohibits.";
                return false;
            }

            if (!PortableSegment.TryEncode(segment, out var encoded, out violation))
            {
                violation = $"the path '{written}' is not portable: {violation}";
                return false;
            }

            segments.Add(encoded!);
        }

        path = new DestinationPath(string.Join('/', segments));
        violation = null;
        return true;
    }

    /// <summary>
    /// Section 21.1's rejected forms, tested on the written text rather than through
    /// <see cref="System.IO.Path"/>, which answers differently per platform.
    /// </summary>
    /// <remarks>
    /// <c>\\server\share</c>, <c>\\?\</c>, and <c>\\.\</c> all begin with a separator, so the rooted
    /// test covers UNC, device, and extended-length forms as well.
    /// </remarks>
    private static bool TryRejectNonRelative(string written, out string? violation)
    {
        if (written.Length == 0)
        {
            violation = "an empty 'filename' names no destination.";
            return false;
        }

        if (written[0] is '/' or '\\')
        {
            violation =
                $"the path '{written}' is rooted, and Section 16.2 requires a path relative to the "
                + "configured output root.";
            return false;
        }

        if (written.Length >= 2 && written[1] == ':' && IsAsciiLetter(written[0]))
        {
            violation =
                $"the path '{written}' is drive-absolute or drive-relative, which Section 21.1 "
                + "rejects on every platform so that one scheme names one destination everywhere.";
            return false;
        }

        violation = null;
        return true;
    }

    private static bool IsAsciiLetter(char c) =>
        c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
}
