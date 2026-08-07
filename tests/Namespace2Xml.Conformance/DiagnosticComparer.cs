using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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

        // The expected file is held to the same Section 6.4.3 layout as the stream, because it is a
        // stream: a fixture author writing "[\n]\n" for the empty array has written a document the
        // tool must never produce, and a corpus that accepts it stops being able to say what
        // correct looks like. A defect here is the fixture's, not the tool's, so it throws.
        var expectedFraming = ValidateFraming(expected)
            .Concat(ValidateCanonicalLayout(expected, expectedRoot.ValueKind == JsonValueKind.Array
                ? expectedRoot.GetArrayLength()
                : 0))
            .ToList();

        if (expectedFraming.Count > 0)
        {
            throw new ConformanceFormatException(
                "expected-diagnostics.json is not canonical: " + string.Join(" ", expectedFraming));
        }

        if (actualRoot.ValueKind != JsonValueKind.Array)
        {
            failures.Add("diagnostic stream is not a JSON array.");
            return failures;
        }

        var actualItems = actualRoot.EnumerateArray().ToList();
        var expectedItems = expectedRoot.EnumerateArray().ToList();

        failures.AddRange(ValidateCanonicalLayout(actual, actualItems.Count));

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

        string text;

        // Section 6.4.3 makes UTF-8 contractual. The replacement fallback would silently turn an
        // invalid byte into U+FFFD and compare it as though the tool had written valid text.
        var decoded = TryDecodeStrictly(actual, out var decodeFailure);

        if (decoded is null)
        {
            yield return $"diagnostic stream is not valid UTF-8: {decodeFailure}";
            yield break;
        }

        text = decoded;

        if (text.Contains('\r'))
        {
            yield return "diagnostic stream contains CR.";
        }

        if (text == "[]\n")
        {
            yield break;
        }

        // The empty array has exactly one normative spelling. "[\n]\n" is the mistake a first
        // implementation makes, and the slice below would compute a negative length on it.
        if (text.Length < "[\n{}\n]\n".Length)
        {
            yield return $"diagnostic stream framing is not normative: '{Preview(text)}'. " +
                         "An empty stream is exactly '[]\\n'.";
            yield break;
        }

        if (!text.StartsWith("[\n", StringComparison.Ordinal) || !text.EndsWith("\n]\n", StringComparison.Ordinal))
        {
            yield return $"diagnostic stream framing is not normative: '{Preview(text)}'.";
            yield break;
        }
    }

    private static string? TryDecodeStrictly(byte[] actual, out string? failure)
    {
        try
        {
            failure = null;

            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(actual);
        }
        catch (DecoderFallbackException exception)
        {
            failure = exception.Message;

            return null;
        }
    }

    /// <summary>
    /// Enforces the Section 6.4.3 byte layout: one compact element per line, separated by
    /// ",\n", with no insignificant whitespace anywhere outside a string literal. This is
    /// deliberately reimplemented here rather than delegated to the production writer, because a
    /// comparer that asks the writer what canonical means cannot detect a wrong writer.
    /// </summary>
    private static IEnumerable<string> ValidateCanonicalLayout(byte[] actual, int elementCount)
    {
        var text = Encoding.UTF8.GetString(actual);

        if (text == "[]\n")
        {
            if (elementCount != 0)
            {
                yield return $"stream is framed as empty but carries {elementCount} elements.";
            }

            yield break;
        }

        if (!text.StartsWith("[\n", StringComparison.Ordinal) || !text.EndsWith("\n]\n", StringComparison.Ordinal))
        {
            yield break;
        }

        // Framing validation has already reported this shape; the body slice below would compute a
        // negative length on it.
        if (text.Length < "[\n{}\n]\n".Length)
        {
            yield break;
        }

        // A raw LF cannot occur inside a JSON string, so it is unambiguously the element separator.
        var lines = text[2..^3].Split('\n');

        if (lines.Length != elementCount)
        {
            yield return $"the stream carries {elementCount} elements on {lines.Length} lines; " +
                         "Section 6.4.3 puts exactly one element on each line.";
            yield break;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var last = i == lines.Length - 1;

            if (!last && !line.EndsWith(','))
            {
                yield return $"element {i} is not followed by the normative ',' separator.";
                continue;
            }

            if (last && line.EndsWith(','))
            {
                yield return $"element {i} is the last element but carries a trailing ','.";
                continue;
            }

            var element = last ? line : line[..^1];

            foreach (var failure in ValidateNoInsignificantWhitespace(i, element))
            {
                yield return failure;
            }

            foreach (var failure in ValidateStringEscapes(i, element))
            {
                yield return failure;
            }
        }
    }

    /// <summary>
    /// Enforces the Section 6.4.3 escape spellings: <c>"</c> and <c>\</c> take a backslash, the
    /// five named controls take <c>\b</c>, <c>\f</c>, <c>\n</c>, <c>\r</c> and <c>\t</c>, every
    /// other C0 control takes a lowercase <c>\u00xx</c>, and every other Unicode scalar is emitted
    /// literally as UTF-8. An unescaped C0 control needs no arm here: RFC 8259 forbids one, so the
    /// reader rejects the stream before any layout rule is consulted.
    /// </summary>
    /// <remarks>
    /// This has to read the raw bytes. A JSON reader decodes <c>\u0041</c>, <c>\/</c> and a literal
    /// <c>A</c> or <c>/</c> to the same string, so every comparison performed on decoded values is
    /// blind to the one thing Section 24 byte-identity depends on. Without this, a writer that
    /// escaped every non-ASCII scalar would satisfy the whole corpus while emitting different bytes
    /// on every platform, which is precisely what byte-identity forbids.
    /// </remarks>
    private static IEnumerable<string> ValidateStringEscapes(int index, string element)
    {
        const string named = "\"\\bfnrt";
        var inString = false;

        for (var i = 0; i < element.Length; i++)
        {
            var character = element[i];

            if (!inString)
            {
                inString = character == '"';
                continue;
            }

            if (character == '"')
            {
                inString = false;
                continue;
            }

            if (character != '\\')
            {
                continue;
            }

            if (i + 1 >= element.Length)
            {
                yield return $"element {index} ends in an incomplete escape.";
                yield break;
            }

            var next = element[++i];

            if (named.Contains(next, StringComparison.Ordinal))
            {
                continue;
            }

            if (next != 'u')
            {
                yield return $"element {index} spells an escape as '\\{next}'; Section 6.4.3 permits " +
                             "only \\\", \\\\, \\b, \\f, \\n, \\r, \\t and \\u00xx.";
                continue;
            }

            if (i + 4 >= element.Length)
            {
                yield return $"element {index} ends in an incomplete '\\u' escape.";
                yield break;
            }

            var digits = element.Substring(i + 1, 4);
            i += 4;

            foreach (var failure in ValidateUnicodeEscape(index, digits))
            {
                yield return failure;
            }
        }
    }

    /// <summary>Checks one <c>\uXXXX</c> against the only spelling Section 6.4.3 admits.</summary>
    private static IEnumerable<string> ValidateUnicodeEscape(int index, string digits)
    {
        // Uppercase is rejected before the value is read, so "\u000A" is reported as the wrong
        // spelling of a control rather than passing as the right one.
        if (digits.Any(d => d is >= 'A' and <= 'F'))
        {
            yield return $"element {index} spells '\\u{digits}' with uppercase hexadecimal; " +
                         "Section 6.4.3 fixes lowercase.";
            yield break;
        }

        if (!int.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var scalar))
        {
            yield return $"element {index} carries a malformed escape '\\u{digits}'.";
            yield break;
        }

        if (scalar is 0x08 or 0x09 or 0x0A or 0x0C or 0x0D)
        {
            yield return $"element {index} escapes U+{scalar:X4} as '\\u{digits}'; Section 6.4.3 " +
                         "fixes a named escape for that control.";
            yield break;
        }

        if (scalar > 0x1F)
        {
            yield return $"element {index} escapes U+{scalar:X4} as '\\u{digits}'; Section 6.4.3 " +
                         "emits every scalar above the C0 controls literally as UTF-8.";
        }
    }

    private static IEnumerable<string> ValidateNoInsignificantWhitespace(int index, string element)
    {
        var inString = false;
        var escaped = false;

        foreach (var character in element)
        {
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                yield return $"element {index} carries insignificant whitespace; Section 6.4.3 " +
                             "fixes a compact encoding with no whitespace between tokens.";
                yield break;
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

            // System.Text.Json keeps the first duplicate, jq keeps the last. A stream whose
            // meaning depends on the consumer is not a contract.
            if (seen.Contains(member.Name, StringComparer.Ordinal))
            {
                yield return $"member '{member.Name}' appears more than once.";
            }

            foreach (var failure in ValidateMemberValue(member.Name, member.Value))
            {
                yield return failure;
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

    /// <summary>
    /// Value constraints, driven from <c>spec/diagnostic-stream.schema.json</c> rather than
    /// restated here, so the published schema cannot drift away from what the oracle enforces.
    /// </summary>
    private static IEnumerable<string> ValidateMemberValue(string name, JsonElement value)
    {
        if (!SchemaProperties.Value.TryGetValue(name, out var constraint))
        {
            yield break;
        }

        if (constraint.Enum is not null)
        {
            if (value.ValueKind != JsonValueKind.String ||
                !constraint.Enum.Contains(value.GetString()!, StringComparer.Ordinal))
            {
                yield return $"member '{name}' is '{Raw(value)}', which is not one of " +
                             $"[{string.Join(", ", constraint.Enum)}].";
            }

            yield break;
        }

        switch (constraint.Type)
        {
            case "string":
                if (value.ValueKind != JsonValueKind.String)
                {
                    yield return $"member '{name}' must be a string but is {value.ValueKind}.";
                    yield break;
                }

                var text = value.GetString()!;

                if (constraint.Pattern is not null && !Regex.IsMatch(text, constraint.Pattern))
                {
                    yield return $"member '{name}' is '{text}', which does not match {constraint.Pattern}.";
                }

                if (constraint.MinLength is int minLength && text.Length < minLength)
                {
                    yield return $"member '{name}' is shorter than the required {minLength} characters.";
                }

                break;

            case "integer":
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number))
                {
                    yield return $"member '{name}' must be an integer but is {Raw(value)}.";
                    yield break;
                }

                if (constraint.Minimum is long minimum && number < minimum)
                {
                    yield return $"member '{name}' is {number}, below the required minimum of {minimum}.";
                }

                break;

            default:
                break;
        }
    }

    private static string Raw(JsonElement value) => value.GetRawText();

    private sealed record MemberConstraint(
        string? Type,
        string[]? Enum,
        string? Pattern,
        int? MinLength,
        long? Minimum);

    private static readonly Lazy<IReadOnlyDictionary<string, MemberConstraint>> SchemaProperties =
        new(LoadSchemaProperties);

    private static Dictionary<string, MemberConstraint> LoadSchemaProperties()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(CorpusLayout.StreamSchema));

        var properties = document.RootElement
            .GetProperty("items")
            .GetProperty("properties");

        var result = new Dictionary<string, MemberConstraint>(StringComparer.Ordinal);

        foreach (var property in properties.EnumerateObject())
        {
            var body = property.Value;

            result[property.Name] = new MemberConstraint(
                Type: body.TryGetProperty("type", out var type) ? type.GetString() : null,
                Enum: body.TryGetProperty("enum", out var choices)
                    ? choices.EnumerateArray().Select(choice => choice.GetString()!).ToArray()
                    : null,
                Pattern: body.TryGetProperty("pattern", out var pattern) ? pattern.GetString() : null,
                MinLength: body.TryGetProperty("minLength", out var minLength) ? minLength.GetInt32() : null,
                Minimum: body.TryGetProperty("minimum", out var minimum) ? minimum.GetInt64() : null);
        }

        // The byte order of members is a Section 6.4.3 rule, not a JSON Schema one, so MemberOrder
        // is authored here. It must still name exactly the members the schema defines.
        var schemaNames = result.Keys.OrderBy(name => name, StringComparer.Ordinal);
        var orderedNames = MemberOrder.OrderBy(name => name, StringComparer.Ordinal);

        if (!schemaNames.SequenceEqual(orderedNames, StringComparer.Ordinal))
        {
            throw new ConformanceFormatException(
                "DiagnosticComparer.MemberOrder and spec/diagnostic-stream.schema.json disagree " +
                "about which members exist. Regenerate the schema or update the comparer.");
        }

        return result;
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
