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
            var expectedBytes = File.ReadAllBytes(expected[relative]);
            var actualBytes = File.ReadAllBytes(actual[relative]);

            if (!expectedBytes.AsSpan().SequenceEqual(actualBytes))
            {
                failures.Add($"'{relative}' differs: {Describe(expectedBytes, actualBytes)}");
            }
        }

        return failures;
    }

    private static Dictionary<string, string> Enumerate(string? root)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        if (root is null || !Directory.Exists(root))
        {
            return map;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            map[relative] = file;
        }

        return map;
    }

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
