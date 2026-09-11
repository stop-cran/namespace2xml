using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Namespace2Xml.Gatekeeper;

internal abstract record GateIdentity;

internal sealed record NUnitGateIdentity(
    string AssemblyName,
    string FullyQualifiedName) : GateIdentity;

internal sealed record CiGateIdentity(
    string WorkflowPath,
    string JobId,
    MatrixCell? MatrixCell) : GateIdentity;

internal sealed record MatrixScalar(string CanonicalJson, JsonValueKind Kind)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static MatrixScalar FromJson(string text, bool requireCanonical)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;

        if (root.ValueKind is JsonValueKind.Array or JsonValueKind.Object or JsonValueKind.Undefined)
        {
            throw new FormatException($"'{text}' is not a JSON scalar.");
        }

        var canonical = root.ValueKind switch
        {
            JsonValueKind.String => JsonSerializer.Serialize(root.GetString(), JsonOptions),
            JsonValueKind.Number => CanonicalizeJsonNumber(root.GetRawText()),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => throw new FormatException($"'{text}' is not a JSON scalar."),
        };

        if (requireCanonical && !string.Equals(text, canonical, StringComparison.Ordinal))
        {
            throw new FormatException(
                $"JSON scalar '{text}' is not canonical; its canonical spelling is '{canonical}'.");
        }

        return new MatrixScalar(canonical, root.ValueKind);
    }

    internal static MatrixScalar FromYaml(string value, bool quoted, string? explicitTag = null)
    {
        if (explicitTag is not null)
        {
            return FromExplicitYamlTag(value, quoted, explicitTag);
        }

        if (quoted)
        {
            return String(value);
        }

        if (value is "" or "null" or "Null" or "NULL" or "~")
        {
            return new MatrixScalar("null", JsonValueKind.Null);
        }

        if (value is "true" or "True" or "TRUE")
        {
            return new MatrixScalar("true", JsonValueKind.True);
        }

        if (value is "false" or "False" or "FALSE")
        {
            return new MatrixScalar("false", JsonValueKind.False);
        }

        if (TryParseInteger(value, out var number) || TryParseFloat(value, out number))
        {
            return Number(number, value);
        }

        return String(value);
    }

    internal string ToRunnerString() =>
        Kind switch
        {
            JsonValueKind.String => JsonSerializer.Deserialize<string>(CanonicalJson)
                ?? throw new FormatException("A matrix string cannot be null."),
            JsonValueKind.Number => CanonicalJson,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            _ => throw new FormatException(
                $"JSON kind '{Kind}' cannot be converted to an Actions mapping key."),
        };

    private static MatrixScalar FromExplicitYamlTag(
        string value,
        bool quoted,
        string explicitTag)
    {
        const string stringTag = "tag:yaml.org,2002:str";
        const string booleanTag = "tag:yaml.org,2002:bool";
        const string floatTag = "tag:yaml.org,2002:float";
        const string integerTag = "tag:yaml.org,2002:int";
        const string nullTag = "tag:yaml.org,2002:null";

        if (explicitTag == stringTag)
        {
            return String(value);
        }

        if (explicitTag is not booleanTag and not floatTag and not integerTag and not nullTag)
        {
            throw new FormatException($"YAML tag '{explicitTag}' is not supported.");
        }

        if (quoted)
        {
            throw new FormatException(
                $"YAML tag '{explicitTag}' requires a plain scalar value.");
        }

        if (explicitTag == booleanTag)
        {
            return value switch
            {
                "true" or "True" or "TRUE" => new MatrixScalar("true", JsonValueKind.True),
                "false" or "False" or "FALSE" => new MatrixScalar("false", JsonValueKind.False),
                _ => throw InvalidTaggedValue(value, explicitTag),
            };
        }

        if (explicitTag == integerTag)
        {
            if (TryParseInteger(value, out var integer))
            {
                return Number(integer, value);
            }

            throw InvalidTaggedValue(value, explicitTag);
        }

        if (explicitTag == floatTag)
        {
            if (TryParseFloat(value, out var number))
            {
                return Number(number, value);
            }

            throw InvalidTaggedValue(value, explicitTag);
        }

        if (value is "" or "null" or "Null" or "NULL" or "~")
        {
            return new MatrixScalar("null", JsonValueKind.Null);
        }

        throw InvalidTaggedValue(value, explicitTag);
    }

    private static FormatException InvalidTaggedValue(string value, string tag) =>
        new($"YAML value '{value}' is invalid for tag '{tag}'.");

    private static MatrixScalar String(string value) =>
        new(JsonSerializer.Serialize(value, JsonOptions), JsonValueKind.String);

    private static MatrixScalar Number(double value, string source)
    {
        if (!double.IsFinite(value))
        {
            throw new FormatException(
                $"YAML number '{source}' is non-finite and cannot be represented by a JSON-scalar gate identity.");
        }

        return new MatrixScalar(CanonicalizeNumber(value), JsonValueKind.Number);
    }

    private static string CanonicalizeJsonNumber(string text)
    {
        if (!double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value)
            || !double.IsFinite(value))
        {
            throw new FormatException($"JSON number '{text}' cannot be represented canonically.");
        }

        return CanonicalizeNumber(value);
    }

    private static string CanonicalizeNumber(double value) =>
        value == 0
            ? "0"
            : value.ToString("G15", CultureInfo.InvariantCulture);

    private static bool TryParseInteger(string text, out double value)
    {
        value = default;
        if (text.Length == 0)
        {
            return false;
        }

        var first = text[0];
        var digitsStart = first is '+' or '-' ? 1 : 0;
        if (digitsStart < text.Length && text[digitsStart..].All(IsDecimalDigit))
        {
            if (!double.TryParse(
                    text,
                    digitsStart == 0 ? NumberStyles.None : NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out value))
            {
                throw new FormatException($"YAML integer '{text}' is outside the supported range.");
            }

            return true;
        }

        if (text.Length > 2
            && text[0] == '0'
            && text[1] == 'x'
            && text[2..].All(IsHexDigit))
        {
            if (!int.TryParse(
                    text[2..],
                    NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture,
                    out var integer))
            {
                throw new FormatException($"YAML integer '{text}' is outside the supported range.");
            }

            value = integer;
            return true;
        }

        if (text.Length > 2
            && text[0] == '0'
            && text[1] == 'o'
            && text[2..].All(IsOctalDigit))
        {
            try
            {
                value = Convert.ToInt32(text[2..], 8);
                return true;
            }
            catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
            {
                throw new FormatException(
                    $"YAML integer '{text}' is outside the supported range.",
                    error);
            }
        }

        return false;
    }

    private static bool TryParseFloat(string text, out double value)
    {
        value = default;
        if (text is ".inf" or ".Inf" or ".INF" or "+.inf" or "+.Inf" or "+.INF")
        {
            value = double.PositiveInfinity;
            return true;
        }

        if (text is "-.inf" or "-.Inf" or "-.INF")
        {
            value = double.NegativeInfinity;
            return true;
        }

        if (text is ".nan" or ".NaN" or ".NAN")
        {
            value = double.NaN;
            return true;
        }

        if (!Regex.IsMatch(
                text,
                @"^[+-]?(?:\.[0-9]+|[0-9]+(?:\.[0-9]*)?)(?:[eE][+-]?[0-9]+)?$",
                RegexOptions.CultureInvariant))
        {
            return false;
        }

        if (!double.TryParse(
                text,
                NumberStyles.AllowLeadingSign
                | NumberStyles.AllowDecimalPoint
                | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture,
                out value))
        {
            throw new FormatException($"YAML number '{text}' is outside the supported range.");
        }

        return true;
    }

    private static bool IsDecimalDigit(char value) => value is >= '0' and <= '9';

    private static bool IsOctalDigit(char value) => value is >= '0' and <= '7';

    private static bool IsHexDigit(char value) =>
        IsDecimalDigit(value) || value is >= 'a' and <= 'f' or >= 'A' and <= 'F';
}

internal sealed class MatrixCell : IEquatable<MatrixCell>
{
    private readonly ReadOnlyCollection<KeyValuePair<string, MatrixScalar>> dimensions;

    internal MatrixCell(IEnumerable<KeyValuePair<string, MatrixScalar>> values)
    {
        var ordered = values.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList();

        if (ordered.Count == 0)
        {
            throw new ArgumentException("A matrix cell must contain at least one dimension.", nameof(values));
        }

        for (var index = 1; index < ordered.Count; index++)
        {
            if (string.Equals(ordered[index - 1].Key, ordered[index].Key, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Matrix dimension '{ordered[index].Key}' occurs more than once.",
                    nameof(values));
            }
        }

        dimensions = ordered.AsReadOnly();
    }

    internal IReadOnlyList<KeyValuePair<string, MatrixScalar>> Dimensions => dimensions;

    public bool Equals(MatrixCell? other)
    {
        if (other is null || dimensions.Count != other.dimensions.Count)
        {
            return false;
        }

        for (var index = 0; index < dimensions.Count; index++)
        {
            if (!string.Equals(
                    dimensions[index].Key,
                    other.dimensions[index].Key,
                    StringComparison.Ordinal)
                || dimensions[index].Value != other.dimensions[index].Value)
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as MatrixCell);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var dimension in dimensions)
        {
            hash.Add(dimension.Key, StringComparer.Ordinal);
            hash.Add(dimension.Value);
        }

        return hash.ToHashCode();
    }
}
