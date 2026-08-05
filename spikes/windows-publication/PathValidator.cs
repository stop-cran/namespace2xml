using System.Text;

namespace Namespace2Xml.Spikes.WindowsPublication;

/// <summary>
/// Planning-stage (string-layer) validation, mirroring specification §16.2 (portable segment
/// algorithm), §21.1 (structural rejection of rooted/drive/UNC/device/extended-length forms and
/// <c>.</c>/<c>..</c> segments), and §17.5 (portability-key case-collision detection).
///
/// This is the layer that neutralises adversarial *strings*. The runtime <see cref="SecureWriter"/>
/// layer neutralises adversarial *filesystems* (reparse points / TOCTOU). Both are required; neither
/// subsumes the other. Every rejection here is diagnostic code <c>PATH001</c> (§22).
/// </summary>
public static class PathValidator
{
    public sealed record Result(bool Ok, string? Code, string Detail, string? Canonical = null, string? PortabilityKey = null);

    private static Result Reject(string detail) => new(false, "PATH001", detail);

    /// <summary>Validate one explicit <c>filename</c> directive value into a canonical relative path.</summary>
    public static Result ValidateExplicitFilename(string filename)
    {
        if (string.IsNullOrEmpty(filename))
            return Reject("empty filename");

        // §21.1 structural whole-path rejection (evaluated on the raw string).
        string? structural = RejectStructuralForm(filename);
        if (structural is not null)
            return Reject(structural);

        // §16.2 step 1: split only at literally written '/' and '\'.
        string[] rawSegments = filename.Split('/', '\\');
        var encoded = new List<string>(rawSegments.Length);
        foreach (string seg in rawSegments)
        {
            // §16.2 step 3: reject an empty assembled segment (also collapses "redundant separators").
            if (seg.Length == 0)
                return Reject($"empty/redundant path segment in '{filename}'");

            // §16.2 + §21.1: statically written '.' and '..' are prohibited (not encoded).
            if (seg is "." or "..")
                return Reject($"prohibited '{seg}' segment in '{filename}'");

            encoded.Add(EncodeSegment(seg));
        }

        string canonical = string.Join('/', encoded);
        return new Result(true, null, "ok", canonical, PortabilityKey(canonical));
    }

    /// <summary>§21.1 rejection of rooted, drive-absolute, drive-relative, UNC, device, and extended-length forms.</summary>
    private static string? RejectStructuralForm(string f)
    {
        if (f.Length >= 2 && f[1] == ':')
            return $"drive-qualified path form '{f}' (e.g. C:\\x or C:x)";
        if (f.StartsWith(@"\\", StringComparison.Ordinal) || f.StartsWith("//", StringComparison.Ordinal)
            || f.StartsWith(@"\/", StringComparison.Ordinal) || f.StartsWith(@"/\", StringComparison.Ordinal))
            return $"UNC / device / extended-length form '{f}' (\\\\server, \\\\?\\, \\\\.\\ )";
        if (f[0] == '\\' || f[0] == '/')
            return $"rooted path form '{f}'";
        return null;
    }

    /// <summary>§16.2 steps 4–7 applied to a single opaque segment.</summary>
    public static string EncodeSegment(string seg)
    {
        // step 4: reserved-device condition (portion before first dot, ASCII case-insensitive).
        bool reservedDevice = IsReservedDevice(seg);

        // step 5: retain [A-Za-z0-9-_.]; encode every other UTF-8 byte, including '%', as uppercase %HH.
        var sb = new StringBuilder(seg.Length + 8);
        foreach (byte b in Encoding.UTF8.GetBytes(seg))
        {
            char c = (char)b;
            bool retain = c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9')
                          or '-' or '_' or '.';
            if (retain) sb.Append(c);
            else sb.Append('%').Append(b.ToString("X2"));
        }

        // step 6: percent-encode every trailing dot as %2E. (Trailing spaces are already %20 from step 5.)
        string result = sb.ToString();
        int end = result.Length;
        while (end >= 1 && result[end - 1] == '.') end--;
        int trailingDots = result.Length - end;
        if (trailingDots > 0)
            result = result[..end] + string.Concat(Enumerable.Repeat("%2E", trailingDots));

        // step 7: prefix %5F for a reserved-device (dot-segments were rejected earlier).
        if (reservedDevice)
            result = "%5F" + result;

        return result;
    }

    /// <summary>§16.2 step 4 reserved-name list: CON, PRN, AUX, NUL, COM1–COM9, LPT1–LPT9.</summary>
    public static bool IsReservedDevice(string seg)
    {
        int dot = seg.IndexOf('.');
        string stem = (dot >= 0 ? seg[..dot] : seg).ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL")
            return true;
        if (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
            && stem[3] is >= '1' and <= '9')
            return true;
        return false;
    }

    /// <summary>§17.5 portability key: uppercase ASCII letters only (encoded bytes are already uppercase).</summary>
    public static string PortabilityKey(string canonical)
    {
        char[] chars = canonical.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (chars[i] is >= 'a' and <= 'z')
                chars[i] = (char)(chars[i] - 32);
        return new string(chars);
    }
}
