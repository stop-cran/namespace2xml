using YamlDotNet.RepresentationModel;

namespace Namespace2Xml.Gatekeeper;

internal static class MatrixExpander
{
    internal static IReadOnlyList<MatrixCell> Expand(YamlNode matrixNode)
    {
        if (matrixNode is not YamlMappingNode matrix)
        {
            throw new InvalidDataException(
                "A gated matrix must be a literal YAML mapping, not an expression or scalar.");
        }

        var fields = Mapping(matrix, "matrix");
        var axes = fields
            .Where(field => field.Key is not "include" and not "exclude")
            .Select(field => new KeyValuePair<string, List<MatrixScalar>>(
                ValidateDimensionKey(field.Key),
                ScalarSequence(field.Value, $"matrix axis '{field.Key}'")))
            .ToList();

        var originals = new List<Expansion>();
        if (axes.Count > 0)
        {
            Cartesian(axes, 0, new Dictionary<string, MatrixScalar>(StringComparer.Ordinal), originals);
        }

        if (fields.TryGetValue("exclude", out var excludeNode))
        {
            foreach (var exclusion in MappingSequence(excludeNode, "matrix exclude"))
            {
                originals.RemoveAll(expansion => IsSubset(exclusion, expansion.Current));
            }
        }

        var additions = new List<Expansion>();
        if (fields.TryGetValue("include", out var includeNode))
        {
            foreach (var inclusion in MappingSequence(includeNode, "matrix include"))
            {
                var matched = false;

                foreach (var expansion in originals)
                {
                    if (!CanApply(inclusion, expansion.Original!))
                    {
                        continue;
                    }

                    Merge(expansion.Current, inclusion);
                    matched = true;
                }

                if (!matched)
                {
                    additions.Add(new Expansion(
                        Original: null,
                        new Dictionary<string, MatrixScalar>(inclusion, StringComparer.Ordinal)));
                }
            }
        }

        originals.AddRange(additions);

        if (originals.Count == 0)
        {
            throw new InvalidDataException("A gated matrix expands to no cells.");
        }

        var cells = originals.Select(expansion => new MatrixCell(expansion.Current)).ToList();
        var duplicate = cells.GroupBy(cell => cell).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException(
                $"A gated matrix produces duplicate cell {GateIdentityRenderer.RenderCell(duplicate.Key)}.");
        }

        return cells;
    }

    private static void Cartesian(
        IReadOnlyList<KeyValuePair<string, List<MatrixScalar>>> axes,
        int index,
        Dictionary<string, MatrixScalar> current,
        List<Expansion> expansions)
    {
        if (index == axes.Count)
        {
            var original = new Dictionary<string, MatrixScalar>(current, StringComparer.Ordinal);
            expansions.Add(new Expansion(
                original,
                new Dictionary<string, MatrixScalar>(original, StringComparer.Ordinal)));
            return;
        }

        var axis = axes[index];
        foreach (var value in axis.Value)
        {
            current[axis.Key] = value;
            Cartesian(axes, index + 1, current, expansions);
        }

        current.Remove(axis.Key);
    }

    private static List<MatrixScalar> ScalarSequence(YamlNode node, string description)
    {
        if (node is not YamlSequenceNode sequence || sequence.Children.Count == 0)
        {
            throw new InvalidDataException($"{description} must be a nonempty literal sequence.");
        }

        return sequence.Children.Select(child => Scalar(child, description)).ToList();
    }

    private static List<Dictionary<string, MatrixScalar>> MappingSequence(
        YamlNode node,
        string description)
    {
        if (node is not YamlSequenceNode sequence)
        {
            throw new InvalidDataException($"{description} must be a literal sequence.");
        }

        return sequence.Children.Select((child, index) =>
        {
            if (child is not YamlMappingNode mapping)
            {
                throw new InvalidDataException($"{description} entry {index} must be a mapping.");
            }

            var result = Mapping(mapping, $"{description} entry {index}")
                .ToDictionary(
                    field => ValidateDimensionKey(field.Key),
                    field => Scalar(field.Value, $"{description} entry {index}"),
                    StringComparer.Ordinal);

            if (result.Count == 0)
            {
                throw new InvalidDataException($"{description} entry {index} cannot be empty.");
            }

            return result;
        }).ToList();
    }

    private static MatrixScalar Scalar(YamlNode node, string description)
    {
        if (node is not YamlScalarNode scalar)
        {
            throw new InvalidDataException($"{description} contains a non-scalar matrix value.");
        }

        var value = scalar.Value ?? string.Empty;
        if (value.Contains("${{", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"{description} depends on an expression and cannot be expanded statically.");
        }

        var quoted = scalar.Style is not YamlDotNet.Core.ScalarStyle.Plain
            and not YamlDotNet.Core.ScalarStyle.Any;
        var explicitTag = scalar.Tag.IsEmpty ? null : scalar.Tag.Value;
        return MatrixScalar.FromYaml(value, quoted, explicitTag);
    }

    private static Dictionary<string, YamlNode> Mapping(
        YamlMappingNode mapping,
        string description)
    {
        var result = new Dictionary<string, YamlNode>(StringComparer.Ordinal);

        foreach (var field in mapping.Children)
        {
            if (field.Key is not YamlScalarNode key)
            {
                throw new InvalidDataException($"{description} contains a non-scalar key.");
            }

            var decodedKey = MappingKey(key, description);
            if (!result.TryAdd(decodedKey, field.Value))
            {
                throw new InvalidDataException(
                    $"{description} contains duplicate key '{decodedKey}'.");
            }
        }

        return result;
    }

    private static string MappingKey(YamlScalarNode key, string description)
    {
        var value = key.Value ?? string.Empty;
        if (value.Contains("${{", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"{description} contains an expression key and cannot be expanded statically.");
        }

        var quoted = key.Style is not YamlDotNet.Core.ScalarStyle.Plain
            and not YamlDotNet.Core.ScalarStyle.Any;
        var explicitTag = key.Tag.IsEmpty ? null : key.Tag.Value;
        return MatrixScalar.FromYaml(value, quoted, explicitTag).ToRunnerString();
    }

    private static string ValidateDimensionKey(string key)
    {
        if (!GateIdentityParser.IsIdentifier(key))
        {
            throw new InvalidDataException(
                $"Matrix dimension '{key}' cannot be represented in a canonical gate identity.");
        }

        return key;
    }

    private static bool IsSubset(
        Dictionary<string, MatrixScalar> subset,
        Dictionary<string, MatrixScalar> candidate) =>
        subset.All(field =>
            candidate.TryGetValue(field.Key, out var value) && value == field.Value);

    private static bool CanApply(
        Dictionary<string, MatrixScalar> inclusion,
        Dictionary<string, MatrixScalar> original) =>
        inclusion.All(field =>
            !original.TryGetValue(field.Key, out var value) || value == field.Value);

    private static void Merge(
        Dictionary<string, MatrixScalar> target,
        Dictionary<string, MatrixScalar> source)
    {
        foreach (var field in source)
        {
            target[field.Key] = field.Value;
        }
    }

    private sealed record Expansion(
        Dictionary<string, MatrixScalar>? Original,
        Dictionary<string, MatrixScalar> Current);
}
