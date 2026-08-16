using System.Text;

namespace Namespace2Xml.Conformance;

/// <summary>
/// Byte-for-byte comparison of an output-root tree against an expected tree, per specification
/// Appendix C.3. Unexpected, missing, differently cased, or differently normalized paths fail.
/// </summary>
public static class OutputTreeComparer
{
    /// <summary>Returns one human-readable failure per difference, or an empty list when equal.</summary>
    public static IReadOnlyList<string> Compare(string? expectedRoot, string actualRoot)
    {
        var failures = new List<string>();

        var expected = Enumerate(expectedRoot);
        var actual = Enumerate(actualRoot);

        foreach (var relative in expected.Keys.Except(actual.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            failures.Add($"missing output '{relative}'");
        }

        foreach (var relative in actual.Keys.Except(expected.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            // A path that differs only by case is reported as unexpected plus missing, which is
            // what a portable-collision regression looks like and is exactly what should fail.
            failures.Add($"unexpected output '{relative}'");
        }

        foreach (var relative in expected.Keys.Intersect(actual.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            // Directory entries carry a trailing '/' and have no bytes to compare.
            if (expected[relative] is not { } expectedPath || actual[relative] is not { } actualPath)
            {
                continue;
            }

            var expectedBytes = File.ReadAllBytes(expectedPath);
            var actualBytes = File.ReadAllBytes(actualPath);

            if (!expectedBytes.AsSpan().SequenceEqual(actualBytes))
            {
                failures.Add($"'{relative}' differs: {Describe(expectedBytes, actualBytes)}");
            }
        }

        return failures;
    }

    /// <summary>
    /// Maps every entry below <paramref name="root"/> to its full path, or to <see langword="null"/>
    /// for a directory. Directories are enumerated as well as files: Appendix C.3 says an absent
    /// expected tree means no destination may be created, and a spuriously created empty directory
    /// is a created destination.
    /// </summary>
    private static Dictionary<string, string?> Enumerate(string? root)
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);

        if (root is null || !Directory.Exists(root))
        {
            return map;
        }

        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            map[Relative(root, directory) + "/"] = null;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            map[Relative(root, file)] = file;
        }

        return map;
    }

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string Describe(byte[] expected, byte[] actual)
    {
        var limit = Math.Min(expected.Length, actual.Length);

        for (var i = 0; i < limit; i++)
        {
            if (expected[i] != actual[i])
            {
                return $"first difference at byte {i} (expected 0x{expected[i]:x2}, actual 0x{actual[i]:x2})";
            }
        }

        return $"length differs (expected {expected.Length} bytes, actual {actual.Length})";
    }

    /// <summary>Renders bytes for a failure message, escaping anything not printable ASCII.</summary>
    internal static string Printable(byte[] bytes)
    {
        var builder = new StringBuilder();

        foreach (var b in bytes)
        {
            builder.Append(b is >= 0x20 and < 0x7F ? (char)b : $"\\x{b:x2}");
        }

        return builder.ToString();
    }
}
