using System.Text;
using System.Text.Json;

namespace Namespace2Xml.Gatekeeper;

internal static class GateIdentityParser
{
    internal static GateIdentity Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        GateIdentity identity = text.StartsWith("nunit:", StringComparison.Ordinal)
            ? ParseNUnit(text)
            : text.StartsWith("ci:", StringComparison.Ordinal)
                ? ParseCi(text)
                : throw new FormatException(
                    $"Gate identity '{text}' must begin with 'nunit:' or 'ci:'.");

        var rendered = GateIdentityRenderer.Render(identity);
        if (!string.Equals(text, rendered, StringComparison.Ordinal))
        {
            throw new FormatException(
                $"Gate identity '{text}' is not canonical; its canonical spelling is '{rendered}'.");
        }

        return identity;
    }

    internal static bool IsIdentifier(string value)
    {
        if (value.Length == 0 || !(IsAsciiLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        return value.Skip(1).All(character =>
            IsAsciiLetter(character) || char.IsAsciiDigit(character) || character is '_' or '-');
    }

    private static NUnitGateIdentity ParseNUnit(string text)
    {
        var payload = text["nunit:".Length..];
        var separator = payload.IndexOf("::", StringComparison.Ordinal);

        if (separator <= 0
            || separator != payload.LastIndexOf("::", StringComparison.Ordinal)
            || separator == payload.Length - 2)
        {
            throw new FormatException(
                "An NUnit gate must be 'nunit:<assembly-name>::<fully-qualified-test-name>'.");
        }

        var assemblyName = payload[..separator];
        var fullyQualifiedName = payload[(separator + 2)..];

        ValidateExactComponent(assemblyName, "NUnit assembly name");
        ValidateExactComponent(fullyQualifiedName, "NUnit fully-qualified test name");

        return new NUnitGateIdentity(assemblyName, fullyQualifiedName);
    }

    private static CiGateIdentity ParseCi(string text)
    {
        var payload = text["ci:".Length..];
        var separator = payload.IndexOf("::", StringComparison.Ordinal);

        if (separator <= 0
            || separator != payload.LastIndexOf("::", StringComparison.Ordinal)
            || separator == payload.Length - 2)
        {
            throw new FormatException(
                "A CI gate must be 'ci:<workflow-path>::<job-id>' with an optional matrix cell.");
        }

        var workflowPath = payload[..separator];
        ValidateWorkflowPath(workflowPath);

        var jobAndCell = payload[(separator + 2)..];
        var bracket = jobAndCell.IndexOf('[');
        var jobId = bracket < 0 ? jobAndCell : jobAndCell[..bracket];

        if (!IsIdentifier(jobId))
        {
            throw new FormatException($"CI job ID '{jobId}' is not a valid canonical identifier.");
        }

        MatrixCell? matrixCell = null;
        if (bracket >= 0)
        {
            if (!jobAndCell.EndsWith(']'))
            {
                throw new FormatException("A matrix-cell identity must end with ']'.");
            }

            matrixCell = ParseMatrixCell(jobAndCell[(bracket + 1)..^1]);
        }

        return new CiGateIdentity(workflowPath, jobId, matrixCell);
    }

    private static MatrixCell ParseMatrixCell(string text)
    {
        if (text.Length == 0)
        {
            throw new FormatException("A matrix-cell identity cannot contain empty brackets.");
        }

        var dimensions = new List<KeyValuePair<string, MatrixScalar>>();
        var index = 0;
        string? previousKey = null;

        while (index < text.Length)
        {
            var equals = text.IndexOf('=', index);
            if (equals <= index)
            {
                throw new FormatException("Every matrix dimension must be '<key>=<json-scalar>'.");
            }

            var key = text[index..equals];
            if (!IsIdentifier(key))
            {
                throw new FormatException(
                    $"Matrix dimension '{key}' is not a valid canonical identifier.");
            }

            if (previousKey is not null
                && StringComparer.Ordinal.Compare(previousKey, key) >= 0)
            {
                throw new FormatException("Matrix dimension keys must be unique and ordinally sorted.");
            }

            index = equals + 1;
            var valueEnd = FindJsonScalarEnd(text, index);
            if (valueEnd == index)
            {
                throw new FormatException($"Matrix dimension '{key}' has no JSON scalar value.");
            }

            var scalarText = text[index..valueEnd];
            MatrixScalar scalar;
            try
            {
                scalar = MatrixScalar.FromJson(scalarText, requireCanonical: true);
            }
            catch (JsonException error)
            {
                throw new FormatException(
                    $"Matrix dimension '{key}' has invalid JSON scalar '{scalarText}'.",
                    error);
            }

            dimensions.Add(new KeyValuePair<string, MatrixScalar>(key, scalar));
            previousKey = key;
            index = valueEnd;

            if (index == text.Length)
            {
                break;
            }

            if (text[index] != ',')
            {
                throw new FormatException(
                    $"Unexpected character '{text[index]}' after matrix dimension '{key}'.");
            }

            index++;
            if (index == text.Length)
            {
                throw new FormatException("A matrix-cell identity cannot end with a comma.");
            }
        }

        return new MatrixCell(dimensions);
    }

    private static int FindJsonScalarEnd(string text, int start)
    {
        if (start >= text.Length || text[start] != '"')
        {
            var comma = text.IndexOf(',', start);
            return comma < 0 ? text.Length : comma;
        }

        var escaped = false;
        for (var index = start + 1; index < text.Length; index++)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (text[index] == '\\')
            {
                escaped = true;
                continue;
            }

            if (text[index] == '"')
            {
                return index + 1;
            }
        }

        return text.Length;
    }

    private static void ValidateWorkflowPath(string path)
    {
        if (path.Contains('\\', StringComparison.Ordinal)
            || !path.StartsWith(".github/workflows/", StringComparison.Ordinal)
            || !(path.EndsWith(".yml", StringComparison.Ordinal)
                || path.EndsWith(".yaml", StringComparison.Ordinal)))
        {
            throw new FormatException(
                $"Workflow path '{path}' must use '/' and name a .yml or .yaml file under "
                + ".github/workflows/.");
        }

        var segments = path.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw new FormatException(
                $"Workflow path '{path}' contains an empty, current, or parent segment.");
        }
    }

    private static void ValidateExactComponent(string value, string description)
    {
        if (string.IsNullOrWhiteSpace(value)
            || char.IsWhiteSpace(value[0])
            || char.IsWhiteSpace(value[^1])
            || value.Any(char.IsControl))
        {
            throw new FormatException($"{description} must be nonblank exact text.");
        }
    }

    private static bool IsAsciiLetter(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
}

internal static class GateIdentityRenderer
{
    internal static string Render(GateIdentity identity) =>
        identity switch
        {
            NUnitGateIdentity nunit =>
                $"nunit:{nunit.AssemblyName}::{nunit.FullyQualifiedName}",
            CiGateIdentity ci =>
                $"ci:{ci.WorkflowPath}::{ci.JobId}"
                + (ci.MatrixCell is null ? string.Empty : RenderCell(ci.MatrixCell)),
            _ => throw new ArgumentOutOfRangeException(nameof(identity)),
        };

    internal static string RenderCell(MatrixCell cell)
    {
        var builder = new StringBuilder("[");

        for (var index = 0; index < cell.Dimensions.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            var dimension = cell.Dimensions[index];
            builder.Append(dimension.Key);
            builder.Append('=');
            builder.Append(dimension.Value.CanonicalJson);
        }

        return builder.Append(']').ToString();
    }
}
