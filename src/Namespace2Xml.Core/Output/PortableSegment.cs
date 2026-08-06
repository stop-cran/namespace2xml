using System.Text;

namespace Namespace2Xml.Output;

/// <summary>The Section 16.2 portable segment algorithm.</summary>
/// <remarks>
/// <para>
/// The algorithm runs "identically on every operating system so identical inputs produce identical
/// relative paths". Nothing here consults the host: the reserved-device list is a literal ASCII
/// list rather than a platform query, and no <see cref="System.IO.Path"/> member is used, because
/// every one of them answers differently on Windows and Linux.
/// </para>
/// <para>
/// Encoding is what makes captured data safe: "captured data cannot create traversal because it is
/// encoded", so a capture holding <c>../..</c> becomes an ordinary file name rather than an escape.
/// </para>
/// </remarks>
public static class PortableSegment
{
    private static readonly string[] ReservedDevices =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>Whether a decoded segment is one Section 16.2 prohibits writing statically.</summary>
    /// <param name="decoded">The assembled segment before encoding.</param>
    public static bool IsDotSegment(string decoded) =>
        decoded is "." or "..";

    /// <summary>Encodes one assembled segment.</summary>
    /// <param name="decoded">The assembled segment, with captures already substituted as opaque text.</param>
    /// <param name="segment">The encoded segment, when this returns <see langword="true"/>.</param>
    /// <param name="violation">Why the segment cannot be encoded, otherwise.</param>
    public static bool TryEncode(string decoded, out string? segment, out string? violation)
    {
        ArgumentNullException.ThrowIfNull(decoded);

        // Step 3.
        if (decoded.Length == 0)
        {
            segment = null;
            violation = "Section 16.2 step 3 rejects an empty assembled segment.";
            return false;
        }

        // Step 4: recorded before encoding, because encoding is what destroys the evidence.
        var unsafeName = IsDotSegment(decoded) || IsReservedDevice(decoded);

        var builder = new StringBuilder(decoded.Length);

        // Step 5.
        foreach (var b in Encoding.UTF8.GetBytes(decoded))
        {
            if (IsRetained(b))
            {
                builder.Append((char)b);
            }
            else
            {
                builder.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        // Step 6. A trailing space is already '%20' after step 5, so only dots remain to fix; both
        // are encoded because Windows silently strips them, which would fold two distinct
        // destinations into one file.
        EncodeTrailingDots(builder);

        // Step 7.
        segment = unsafeName ? "%5F" + builder : builder.ToString();
        violation = null;
        return true;
    }

    private static bool IsRetained(byte b) =>
        b is >= (byte)'a' and <= (byte)'z'
        or >= (byte)'A' and <= (byte)'Z'
        or >= (byte)'0' and <= (byte)'9'
        or (byte)'-' or (byte)'_' or (byte)'.';

    private static void EncodeTrailingDots(StringBuilder builder)
    {
        var dots = 0;

        while (dots < builder.Length && builder[^(dots + 1)] == '.')
        {
            dots++;
        }

        if (dots == 0)
        {
            return;
        }

        builder.Length -= dots;

        for (var i = 0; i < dots; i++)
        {
            builder.Append("%2E");
        }
    }

    /// <summary>
    /// Section 16.2: the portion before the first dot, compared case-insensitively against an
    /// ASCII-only list that deliberately excludes <c>COM0</c>, superscript-digit variants,
    /// <c>CONIN$</c>, and <c>CONOUT$</c>.
    /// </summary>
    private static bool IsReservedDevice(string decoded)
    {
        var dot = decoded.IndexOf('.', StringComparison.Ordinal);
        var stem = dot < 0 ? decoded : decoded[..dot];

        return Array.Exists(
            ReservedDevices,
            device => stem.Equals(device, StringComparison.OrdinalIgnoreCase));
    }
}
