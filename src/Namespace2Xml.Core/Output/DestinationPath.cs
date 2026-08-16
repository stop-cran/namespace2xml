using System.Diagnostics.CodeAnalysis;
using System.Text;
using Namespace2Xml.Overlay;
using Namespace2Xml.Profiles;

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
    /// <summary>
    /// The Section 16.2 step 2 placeholder a capture contributes to the step 1 skeleton: one
    /// character that is neither a separator, an ASCII letter, nor a colon, so no capture can make
    /// a path look rooted or drive-relative.
    /// </summary>
    private const char CaptureMark = '\u0001';

    /// <summary>Composes a written relative path template against one instance's captures.</summary>
    /// <param name="template">The <c>filename</c> value as the scheme wrote it.</param>
    /// <param name="captures">The Section 14.1 captures the selector expansion bound.</param>
    /// <param name="path">The canonical path, when this returns <see langword="true"/>.</param>
    /// <param name="violation">Why the path is <c>PATH001</c>, otherwise.</param>
    /// <remarks>
    /// <para>
    /// Section 16.2's algorithm is ordered, and the order is the whole of its security argument:
    /// step 1 splits "the scheme-written path only at literally written <c>/</c> and <c>\</c>", and
    /// step 2 substitutes captures "as decoded opaque text <i>inside</i> the segment". Splitting the
    /// substituted text instead reverses those two steps, and a capture holding a separator then
    /// creates directory hierarchy -- which is exactly what "separators originating inside captured
    /// data are encoded" and "captured data cannot create traversal because it is encoded" forbid.
    /// </para>
    /// <para>
    /// The same reversal loses the distinction step 4 needs. "Statically written <c>.</c> and
    /// <c>..</c> segments are prohibited", but a <i>captured</i> one is a step 4 condition that step
    /// 7 renames with <c>%5F</c>, because Section 16.2 renames unsafe names "deterministically ...
    /// rather than rejecting" them. Only the split can tell the two apart, so each segment records
    /// whether a capture contributed to it.
    /// </para>
    /// </remarks>
    public static bool TryCompose(
        InterpretedValue template,
        WildcardCaptures captures,
        [NotNullWhen(true)] out DestinationPath? path,
        out string? violation)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(captures);

        path = null;

        // Section 21.1's rejected forms are properties of what the scheme wrote, so they are tested
        // against the skeleton. A capture spelling 'C:' or '/etc' is data, and reaches step 5 to be
        // encoded rather than being rejected as a path the scheme never wrote.
        if (!TryRejectNonRelative(Skeleton(template), WildcardSubstitution.Apply(template, captures), out violation))
        {
            return false;
        }

        var segments = new List<string>();

        foreach (var segment in Split(template, captures))
        {
            if (segment.WhollyLiteral && PortableSegment.IsDotSegment(segment.Text))
            {
                violation =
                    $"the segment '{segment.Text}' is a statically written dot segment, which "
                    + "Section 16.2 prohibits.";
                return false;
            }

            if (!PortableSegment.TryEncode(segment.Text, out var encoded, out violation))
            {
                return false;
            }

            segments.Add(encoded!);
        }

        path = new DestinationPath(string.Join('/', segments));
        violation = null;
        return true;
    }

    /// <summary>
    /// Section 16.2 steps 1 and 2: the assembled segments, split only at separators the scheme wrote
    /// literally.
    /// </summary>
    private static List<AssembledSegment> Split(InterpretedValue template, WildcardCaptures captures)
    {
        var segments = new List<AssembledSegment>();
        var text = new StringBuilder();
        var literal = true;
        var next = 0;

        void Flush()
        {
            segments.Add(new AssembledSegment(text.ToString(), literal));
            text.Clear();
            literal = true;
        }

        foreach (var token in template.Tokens)
        {
            switch (token)
            {
                case LiteralValueToken plain:
                    foreach (var c in plain.Text)
                    {
                        if (c is '/' or '\\')
                        {
                            Flush();
                        }
                        else
                        {
                            text.Append(c);
                        }
                    }

                    break;

                // Section 12.1's legacy clamp: "if a legacy value contains more wildcard
                // substitutions than the name produced, the last capture is repeated". The counter
                // spans the whole template rather than one segment, because the value the clamp is
                // about is the whole 'filename', not the part of it before a separator.
                case ValueWildcardToken { CaptureId: null }:
                    if (!captures.Positional.IsEmpty)
                    {
                        text.Append(
                            captures.Positional[Math.Min(next, captures.Positional.Length - 1)]);
                    }

                    next++;
                    literal = false;
                    break;

                case ValueWildcardToken { CaptureId: { } id }:
                    text.Append(captures.Named[id]);
                    literal = false;
                    break;

                case ResolvedReferenceToken opaque:
                    text.Append(opaque.Text);
                    literal = false;
                    break;

                default:
                    throw new InvalidOperationException(
                        "Section 15.1 step 1 resolves every reference in a scheme value, so an "
                        + "unresolved reference cannot reach Section 16.2 composition.");
            }
        }

        Flush();

        return segments;
    }

    /// <summary>
    /// The scheme-written path with every capture and every resolved reference replaced by
    /// <see cref="CaptureMark"/>, which is what Section 21.1's rooted and drive-relative tests are
    /// about. Text a reference supplied is data, so a referent holding <c>/etc</c> or <c>C:</c>
    /// does not make the path rooted any more than a capture holding it does.
    /// </summary>
    private static string Skeleton(InterpretedValue template)
    {
        var text = new StringBuilder();

        foreach (var token in template.Tokens)
        {
            text.Append(token is LiteralValueToken plain ? plain.Text : CaptureMark.ToString());
        }

        return text.ToString();
    }

    /// <summary>
    /// Section 21.1's rejected forms, tested on the written text rather than through
    /// <see cref="System.IO.Path"/>, which answers differently per platform.
    /// </summary>
    /// <remarks>
    /// <c>\\server\share</c>, <c>\\?\</c>, and <c>\\.\</c> all begin with a separator, so the rooted
    /// test covers UNC, device, and extended-length forms as well.
    /// </remarks>
    private static bool TryRejectNonRelative(string skeleton, string written, out string? violation)
    {
        if (skeleton.Length == 0)
        {
            violation = "an empty 'filename' names no destination.";
            return false;
        }

        if (skeleton[0] is '/' or '\\')
        {
            violation =
                $"the path '{written}' is rooted, and Section 16.2 requires a path relative to the "
                + "configured output root.";
            return false;
        }

        if (skeleton.Length >= 2 && skeleton[1] == ':' && IsAsciiLetter(skeleton[0]))
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

    /// <param name="Text">The segment after step 2 substitution.</param>
    /// <param name="WhollyLiteral">
    /// Whether the scheme wrote every character of it, which is what makes a <c>.</c> or <c>..</c>
    /// "statically written" rather than a step 4 condition.
    /// </param>
    private readonly record struct AssembledSegment(string Text, bool WhollyLiteral);
}
