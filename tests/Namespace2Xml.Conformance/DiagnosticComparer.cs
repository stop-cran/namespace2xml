using System.Text;
using System.Text.Json;

namespace Namespace2Xml.Conformance;

/// <summary>
/// Validates a JSON diagnostic stream against specification Sections 6.4.3 and Appendix C.4:
/// framing bytes, closed schema, then structural comparison against the expected array.
/// </summary>
public static class DiagnosticComparer
{
    private static readonly string[] MemberOrder =
    [
        "code", "severity", "phase", "source", "line", "column",
        "path", "declaration", "rule", "destination", "spec", "message",
    ];

    private static readonly HashSet<string> Required =
        new(["code", "severity", "phase", "spec", "message"], StringComparer.Ordinal);

    /// <summary>Returns one failure per difference, or an empty list when the stream is correct.</summary>
    public static IReadOnlyList<string> Compare(byte[] expected, byte[] actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        var failures = new List<string>();

        failures.AddRange(ValidateFraming(actual));

        JsonElement actualRoot;
        JsonElement expectedRoot;

        try
        {
            actualRoot = JsonDocument.Parse(actual).RootElement.Clone();
        }
        catch (JsonException exception)
        {
            failures.Add($"diagnostic stream is not valid JSON: {exception.Message}");
            return failures;
        }

        try
        {
            expectedRoot = JsonDocument.Parse(expected).RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new ConformanceFormatException($"expected-diagnostics.json is not valid JSON: {exception.Message}");
        }

        if (actualRoot.ValueKind != JsonValueKind.Array)
        {
            failures.Add("diagnostic stream is not a JSON array.");
            return failures;
        }

        var actualItems = actualRoot.EnumerateArray().ToList();
        var expectedItems = expectedRoot.EnumerateArray().ToList();

        foreach (var item in actualItems)
        {
            failures.AddRange(ValidateSchema(item));
        }

        if (actualItems.Count != expectedItems.Count)
        {
            failures.Add($"expected {expectedItems.Count} diagnostics but got {actualItems.Count}.");
        }

        for (var i = 0; i < Math.Min(actualItems.Count, expectedItems.Count); i++)
        {
            failures.AddRange(CompareElement(i, expectedItems[i], actualItems[i]));
        }

        return failures;
    }

    private static IEnumerable<string> ValidateFraming(byte[] actual)
    {
        if (actual.Length >= 3 && actual[0] == 0xEF && actual[1] == 0xBB && actual[2] == 0xBF)
        {
            yield return "diagnostic stream carries a byte-order mark.";
            yield break;
        }

        var text = Encoding.UTF8.GetString(actual);

        if (text.Contains('\r'))
        {
            yield return "diagnostic stream contains CR.";
        }

        if (text == "[]\n")
        {
            yield break;
        }

        if (!text.StartsWith("[\n", StringComparison.Ordinal) || !text.EndsWith("\n]\n", StringComparison.Ordinal))
        {
            yield return $"diagnostic stream framing is not normative: '{Preview(text)}'.";
            yield break;
        }

        // Every element occupies exactly one line, so line count and element count must agree.
        var body = text[2..^3];

        if (body.Length > 0)
        {
            foreach (var line in body.Split(",\n"))
            {
                if (line.Contains('\n'))
                {
                    yield return "a diagnostic element spans more than one line.";
                    yield break;
                }
            }
        }
    }

    private static IEnumerable<string> ValidateSchema(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            yield return "a diagnostic element is not an object.";
            yield break;
        }

        var seen = new List<string>();

        foreach (var member in item.EnumerateObject())
        {
            if (!MemberOrder.Contains(member.Name, StringComparer.Ordinal))
            {
                yield return $"unknown diagnostic member '{member.Name}'. The schema is closed.";
            }

            if (member.Value.ValueKind == JsonValueKind.Null)
            {
                yield return $"member '{member.Name}' is null; inapplicable members must be omitted.";
            }

            seen.Add(member.Name);
        }

        foreach (var required in Required)
        {
            if (!seen.Contains(required, StringComparer.Ordinal))
            {
                yield return $"required diagnostic member '{required}' is missing.";
            }
        }

        var ordered = seen.OrderBy(name => Array.IndexOf(MemberOrder, name)).ToList();

        if (!seen.SequenceEqual(ordered, StringComparer.Ordinal))
        {
            yield return $"diagnostic members are out of normative order: {string.Join(", ", seen)}.";
        }

        if (seen.Contains("column", StringComparer.Ordinal) && !seen.Contains("line", StringComparer.Ordinal))
        {
            yield return "'column' requires 'line'.";
        }

        if (seen.Contains("line", StringComparer.Ordinal) && !seen.Contains("source", StringComparer.Ordinal))
        {
            yield return "'line' requires 'source'.";
        }
    }

    private static IEnumerable<string> CompareElement(int index, JsonElement expected, JsonElement actual)
    {
        foreach (var member in MemberOrder)
        {
            // Prose is never compared; the specification anchor is compared only when pinned,
            // so renumbering the specification does not invalidate the whole corpus.
            if (member == "message")
            {
                continue;
            }

            var hasExpected = expected.TryGetProperty(member, out var expectedValue);
            var hasActual = actual.TryGetProperty(member, out var actualValue);

            if (member == "spec" && !hasExpected)
            {
                continue;
            }

            if (hasExpected != hasActual)
            {
                yield return hasExpected
                    ? $"[{index}] expected member '{member}' is missing."
                    : $"[{index}] unexpected member '{member}'.";

                continue;
            }

            if (hasExpected && expectedValue.GetRawText() != actualValue.GetRawText())
            {
                yield return $"[{index}] '{member}': expected {expectedValue.GetRawText()}, got {actualValue.GetRawText()}.";
            }
        }
    }

    private static string Preview(string text) =>
        text.Length <= 120 ? text.Replace("\n", "\\n") : text[..120].Replace("\n", "\\n") + "…";
}
