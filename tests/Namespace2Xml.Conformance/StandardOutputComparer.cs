using System.Text;
using System.Text.Json;

namespace Namespace2Xml.Conformance;

/// <summary>
/// Compares standard output against a case's <c>expected-stdout.txt</c> under the Appendix C.5
/// rules: every non-empty expected line must occur as a complete line of standard output, and the
/// expected lines must occur in the stated relative order.
/// </summary>
/// <remarks>
/// Standard output is asserted rather than reproduced on purpose. Section 6.4.1 fixes a minimum
/// field set for <c>--version</c> and Section 6.4.2 leaves informational prose localizable, so a
/// byte-exact expectation would fail a conforming implementation. The failure mode this replaces
/// is worse in the other direction: before it existed, the <c>cli-version</c> case asserted an
/// exit code and an empty output tree, both of which a binary that printed nothing at all
/// satisfies, while claiming to cover Section 26 item 85.
/// </remarks>
internal static class StandardOutputComparer
{
    /// <summary>
    /// Placeholder values, resolved from the committed contract bundle rather than from the binary
    /// under test. Asking the tool for its own contract revision would establish that a binary
    /// agrees with itself, which is not what Section 22 is for.
    /// </summary>
    private static readonly Lazy<Dictionary<string, string>> Placeholders = new(ReadContractBundle);

    /// <summary>Compares one run's standard output, returning one message per failure.</summary>
    /// <param name="expectedPath">
    /// Path to <c>expected-stdout.txt</c>, which need not exist. Appendix C.5 makes its absence
    /// the assertion that standard output is empty.
    /// </param>
    /// <param name="actual">Raw standard output bytes.</param>
    internal static IEnumerable<string> Compare(string expectedPath, byte[] actual)
    {
        var failures = new List<string>();
        var text = DecodeUtf8(actual, failures);

        if (!File.Exists(expectedPath))
        {
            if (actual.Length != 0)
            {
                failures.Add(
                    "the case declares no expected-stdout.txt, so standard output must be empty, " +
                    $"but {actual.Length} byte(s) were written: {Excerpt(text)}");
            }

            return failures;
        }

        if (text is null)
        {
            return failures;
        }

        var expectedLines = ReadExpectedLines(expectedPath, failures);
        var actualLines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var searchFrom = 0;

        foreach (var (expected, forbidden) in expectedLines)
        {
            if (forbidden)
            {
                if (Array.Exists(actualLines, line => string.Equals(line, expected, StringComparison.Ordinal)))
                {
                    failures.Add($"standard output contains the forbidden line '{expected}'.");
                }

                continue;
            }

            var found = Array.FindIndex(
                actualLines,
                searchFrom,
                line => string.Equals(line, expected, StringComparison.Ordinal));

            if (found < 0)
            {
                var anywhere = Array.Exists(
                    actualLines,
                    line => string.Equals(line, expected, StringComparison.Ordinal));

                failures.Add(anywhere
                    ? $"standard output contains the line '{expected}', but out of the expected order."
                    : $"standard output does not contain the line '{expected}'.");
                continue;
            }

            searchFrom = found + 1;
        }

        return failures;
    }

    private static string? DecodeUtf8(byte[] actual, List<string> failures)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(actual);
        }
        catch (ArgumentException)
        {
            failures.Add("standard output is not valid UTF-8.");
            return null;
        }
    }

    private static List<(string Text, bool Forbidden)> ReadExpectedLines(string expectedPath, List<string> failures)
    {
        var raw = File.ReadAllBytes(expectedPath);

        if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
        {
            failures.Add("expected-stdout.txt must be UTF-8 without a BOM.");
        }

        var content = Encoding.UTF8.GetString(raw);

        if (content.Contains('\r', StringComparison.Ordinal))
        {
            failures.Add("expected-stdout.txt must use LF line endings.");
        }

        return [.. content
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 0)
            .Select(Classify)];
    }

    /// <summary>Applies the Appendix C.5 line prefixes, then expands placeholders.</summary>
    private static (string Text, bool Forbidden) Classify(string line) => line[0] switch
    {
        '!' => (Expand(line[1..]), true),
        '\\' => (Expand(line[1..]), false),
        _ => (Expand(line), false),
    };

    /// <summary>Expands the closed Appendix C.5 placeholder set.</summary>
    /// <exception cref="ConformanceFormatException">
    /// An unknown placeholder. Leaving it unexpanded would make the line unmatchable and report a
    /// missing line, which points at the tool for a defect in the fixture.
    /// </exception>
    private static string Expand(string line)
    {
        if (!line.Contains("${", StringComparison.Ordinal))
        {
            return line;
        }

        var builder = new StringBuilder();
        var index = 0;

        while (index < line.Length)
        {
            var open = line.IndexOf("${", index, StringComparison.Ordinal);

            if (open < 0)
            {
                builder.Append(line, index, line.Length - index);
                break;
            }

            var close = line.IndexOf('}', open);

            if (close < 0)
            {
                throw new ConformanceFormatException(
                    $"expected-stdout.txt has an unterminated placeholder in '{line}'.");
            }

            var name = line[(open + 2)..close];

            if (!Placeholders.Value.TryGetValue(name, out var value))
            {
                throw new ConformanceFormatException(
                    $"expected-stdout.txt uses the unknown placeholder '${{{name}}}'. " +
                    $"Appendix C.5 closes the set to: {string.Join(", ", Placeholders.Value.Keys.Order(StringComparer.Ordinal))}.");
            }

            builder.Append(line, index, open - index).Append(value);
            index = close + 1;
        }

        return builder.ToString();
    }

    private static Dictionary<string, string> ReadContractBundle()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(CorpusLayout.ContractBundle));
        var root = document.RootElement;

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["contract-bundle"] = root.GetProperty("revision").GetString()!,
            ["specification-sha256"] = root.GetProperty("specification").GetProperty("sha256").GetString()!,
            ["registry-sha256"] = root.GetProperty("registry").GetProperty("sha256").GetString()!,
        };
    }

    private static string Excerpt(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "(not decodable as UTF-8)";
        }

        var single = text.Replace('\n', '\u23ce').Replace('\r', '\u23ce');
        return single.Length <= 200 ? $"'{single}'" : $"'{single[..200]}'...";
    }
}
