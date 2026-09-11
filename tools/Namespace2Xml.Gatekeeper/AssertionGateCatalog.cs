using System.Text.Json;

namespace Namespace2Xml.Gatekeeper;

internal sealed record AssertionGateReference(int Item, string Status, GateIdentity Identity);

internal enum AssertionEvidenceKind
{
    Fixture,
    Gate,
}

internal sealed record AssertionEvidenceReference(
    int Item,
    string Assertion,
    AssertionEvidenceKind Kind,
    string Name,
    string Artifact,
    string Observation);

internal sealed class AssertionGateCatalog
{
    private AssertionGateCatalog(
        IReadOnlyList<AssertionGateReference> references,
        IReadOnlyList<AssertionEvidenceReference> evidence)
    {
        References = references;
        Evidence = evidence;
    }

    internal IReadOnlyList<AssertionGateReference> References { get; }

    internal IReadOnlyList<AssertionEvidenceReference> Evidence { get; }

    internal static AssertionGateCatalog Load(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    $"Assertion manifest '{path}' has no items array.");
            }

            var references = new List<AssertionGateReference>();
            var evidence = new List<AssertionEvidenceReference>();
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("item", out var numberElement)
                    || !numberElement.TryGetInt32(out var number))
                {
                    throw new InvalidDataException(
                        "Assertion manifest contains an item without an integer item number.");
                }

                var status = RequiredString(item, number, "item", "status");
                var fixtures = ReadUniqueStrings(item, number, "fixtures");
                var gates = ReadUniqueStrings(item, number, "gates");

                foreach (var text in gates)
                {
                    var identity = GateIdentityParser.Parse(text);
                    var canonical = GateIdentityRenderer.Render(identity);

                    if (!string.Equals(text, canonical, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            $"Assertion item {number} gate '{text}' is not canonical.");
                    }

                    references.Add(new AssertionGateReference(number, status, identity));
                }

                var mapped = ReadAssertionEvidence(item, number, fixtures, gates);
                evidence.AddRange(mapped);

                if (string.Equals(status, "required", StringComparison.Ordinal)
                    && mapped.Count == 0)
                {
                    throw new InvalidDataException(
                        $"Required assertion item {number} has no authored assertions.");
                }

                RequireEveryOwnerUsed(number, fixtures, gates, mapped);
            }

            return new AssertionGateCatalog(references, evidence);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                $"Assertion manifest '{path}' is malformed JSON.",
                error);
        }
    }

    private static List<string> ReadUniqueStrings(
        JsonElement item,
        int number,
        string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var values))
        {
            return [];
        }

        if (values.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"Assertion item {number} has a non-array {propertyName} field.");
        }

        var result = new List<string>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(value.GetString()))
            {
                throw new InvalidDataException(
                    $"Assertion item {number} contains a blank or non-string {propertyName} value.");
            }

            var text = value.GetString()!;
            if (!unique.Add(text))
            {
                throw new InvalidDataException(
                    $"Assertion item {number} repeats {propertyName} value '{text}'.");
            }

            result.Add(text);
        }

        return result;
    }

    private static List<AssertionEvidenceReference> ReadAssertionEvidence(
        JsonElement item,
        int number,
        IReadOnlyCollection<string> fixtures,
        IReadOnlyCollection<string> gates)
    {
        if (!item.TryGetProperty("assertions", out var assertions)
            || assertions.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"Assertion item {number} has no assertions array.");
        }

        var result = new List<AssertionEvidenceReference>();
        var texts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assertion in assertions.EnumerateArray())
        {
            RequireProperties(assertion, number, "assertion", "text", "evidence");
            var text = RequiredString(assertion, number, "assertion", "text");
            if (!texts.Add(text))
            {
                throw new InvalidDataException(
                    $"Assertion item {number} repeats assertion text '{text}'.");
            }

            var owner = assertion.GetProperty("evidence");
            RequireProperties(
                owner,
                number,
                $"evidence for '{text}'",
                "kind",
                "name",
                "artifact",
                "observation");

            var kindText = RequiredString(owner, number, $"evidence for '{text}'", "kind");
            var name = RequiredString(owner, number, $"evidence for '{text}'", "name");
            var artifact = RequiredString(owner, number, $"evidence for '{text}'", "artifact");
            var observation = RequiredString(
                owner,
                number,
                $"evidence for '{text}'",
                "observation");

            var kind = kindText switch
            {
                "fixture" => AssertionEvidenceKind.Fixture,
                "gate" => AssertionEvidenceKind.Gate,
                _ => throw new InvalidDataException(
                    $"Assertion item {number} assertion '{text}' has unknown evidence kind "
                    + $"'{kindText}'."),
            };

            var owners = kind == AssertionEvidenceKind.Fixture ? fixtures : gates;
            if (!owners.Contains(name, StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    $"Assertion item {number} assertion '{text}' names unlisted "
                    + $"{kindText} evidence '{name}'.");
            }

            if (kind == AssertionEvidenceKind.Fixture)
            {
                ValidateFixtureArtifact(number, text, artifact, observation);
            }
            else
            {
                ValidateGateArtifact(number, text, name, artifact);
            }

            result.Add(new AssertionEvidenceReference(
                number,
                text,
                kind,
                name,
                artifact,
                observation));
        }

        RequireSubstantiveOracleForExitStatus(number, result);
        return result;
    }

    private static void RequireEveryOwnerUsed(
        int number,
        IReadOnlyCollection<string> fixtures,
        IReadOnlyCollection<string> gates,
        IReadOnlyList<AssertionEvidenceReference> evidence)
    {
        var usedFixtures = evidence
            .Where(reference => reference.Kind == AssertionEvidenceKind.Fixture)
            .Select(reference => reference.Name)
            .ToHashSet(StringComparer.Ordinal);
        var usedGates = evidence
            .Where(reference => reference.Kind == AssertionEvidenceKind.Gate)
            .Select(reference => reference.Name)
            .ToHashSet(StringComparer.Ordinal);

        var unusedFixtures = fixtures.Where(name => !usedFixtures.Contains(name)).ToList();
        var unusedGates = gates.Where(name => !usedGates.Contains(name)).ToList();

        if (unusedFixtures.Count > 0 || unusedGates.Count > 0)
        {
            throw new InvalidDataException(
                $"Assertion item {number} lists evidence with no assertion owner: "
                + $"fixtures [{string.Join(", ", unusedFixtures)}], "
                + $"gates [{string.Join(", ", unusedGates)}].");
        }
    }

    private static void ValidateFixtureArtifact(
        int number,
        string assertion,
        string artifact,
        string observation)
    {
        if (artifact == "expected-exit-code.txt")
        {
            if (observation is not "expected-exit-code.txt = 0."
                and not "expected-exit-code.txt = 1."
                and not "expected-exit-code.txt = 70.")
            {
                throw new InvalidDataException(
                    $"Assertion item {number} assertion '{assertion}' must state the exact "
                    + "expected-exit-code.txt value in its status observation.");
            }

            return;
        }

        if (artifact is "expected/" or "expected-diagnostics.json" or "expected-stdout.txt"
            or "legacy.md")
        {
            return;
        }

        const string expectedPrefix = "expected/";
        if (!artifact.StartsWith(expectedPrefix, StringComparison.Ordinal)
            || artifact.Length == expectedPrefix.Length
            || artifact.Contains('\\', StringComparison.Ordinal))
        {
            throw InvalidFixtureArtifact(number, assertion, artifact);
        }

        var segments = artifact[expectedPrefix.Length..].Split('/');
        if (segments.Any(segment => segment is "" or "." or ".."))
        {
            throw InvalidFixtureArtifact(number, assertion, artifact);
        }
    }

    private static void ValidateGateArtifact(
        int number,
        string assertion,
        string name,
        string artifact)
    {
        var expected = name.StartsWith("nunit:", StringComparison.Ordinal)
            ? "nunit-result"
            : "ci-job-result";
        if (!string.Equals(artifact, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Assertion item {number} assertion '{assertion}' gate '{name}' must use "
                + $"artifact '{expected}', not '{artifact}'.");
        }
    }

    private static void RequireSubstantiveOracleForExitStatus(
        int number,
        IReadOnlyList<AssertionEvidenceReference> evidence)
    {
        foreach (var fixture in evidence
                     .Where(reference =>
                         reference.Kind == AssertionEvidenceKind.Fixture
                         && reference.Artifact == "expected-exit-code.txt")
                     .Select(reference => reference.Name)
                     .Distinct(StringComparer.Ordinal))
        {
            if (!evidence.Any(reference =>
                    reference.Kind == AssertionEvidenceKind.Fixture
                    && string.Equals(reference.Name, fixture, StringComparison.Ordinal)
                    && IsSubstantiveFixtureArtifact(reference.Artifact)))
            {
                throw new InvalidDataException(
                    $"Assertion item {number} fixture '{fixture}' uses expected-exit-code.txt "
                    + "without a substantive oracle assertion.");
            }
        }
    }

    private static bool IsSubstantiveFixtureArtifact(string artifact) =>
        artifact is "expected/" or "expected-diagnostics.json" or "expected-stdout.txt"
        || artifact.StartsWith("expected/", StringComparison.Ordinal);

    private static InvalidDataException InvalidFixtureArtifact(
        int number,
        string assertion,
        string artifact) =>
        new(
            $"Assertion item {number} assertion '{assertion}' has invalid fixture artifact "
            + $"'{artifact}'.");

    private static string RequiredString(
        JsonElement element,
        int number,
        string context,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException(
                $"Assertion item {number} {context} has no nonblank string {propertyName}.");
        }

        return value.GetString()!;
    }

    private static void RequireProperties(
        JsonElement element,
        int number,
        string context,
        params string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"Assertion item {number} {context} is not an object.");
        }

        var actual = element.EnumerateObject().Select(property => property.Name).ToList();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Assertion item {number} {context} properties are "
                + $"[{string.Join(", ", actual)}], expected [{string.Join(", ", expected)}].");
        }
    }
}
