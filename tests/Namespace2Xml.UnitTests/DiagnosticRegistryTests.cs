using System.Text.Json;
using System.Text.RegularExpressions;
using Namespace2Xml.Diagnostics;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// The registry-drift gate. Specification section 22 makes the registry authoritative only inside
/// its declared domain, so these assertions check that the registry says exactly what the
/// specification text says, and that the C# model can express everything the registry declares.
/// </summary>
[TestFixture]
public class DiagnosticRegistryTests
{
    private static readonly Regex RegistryRow = new(
        @"^\|\s*`([A-Z]+[0-9]{3})`\s*\|\s*(error|warning)\s*\|\s*(.+?)\s*\|\s*(.+?)\s*\|\s*$",
        RegexOptions.Compiled);

    private static JsonElement Registry =>
        JsonDocument.Parse(File.ReadAllText(RepositoryLayout.Registry)).RootElement;

    [Test]
    public void RegistryReproducesTheSection22Table()
    {
        var fromSpecification = File.ReadLines(RepositoryLayout.Specification)
            .Select(line => RegistryRow.Match(line))
            .Where(match => match.Success)
            .ToDictionary(
                match => match.Groups[1].Value,
                match => (Severity: match.Groups[2].Value, Cardinality: match.Groups[4].Value));

        fromSpecification.ShouldNotBeEmpty();

        var codes = Registry.GetProperty("codes").EnumerateArray().ToList();

        codes.Count.ShouldBe(fromSpecification.Count);

        foreach (var entry in codes)
        {
            var code = entry.GetProperty("code").GetString()!;

            fromSpecification.ShouldContainKey(code);
            entry.GetProperty("severity").GetString().ShouldBe(fromSpecification[code].Severity);
            entry.GetProperty("cardinality").GetString().ShouldBe(fromSpecification[code].Cardinality);
        }
    }

    [Test]
    public void RegistryRecordsTheSpecificationItWasDerivedFrom() =>
        Registry.GetProperty("specification").GetProperty("sha256").GetString()
            .ShouldBe(RepositoryLayout.Sha256Of(RepositoryLayout.Specification));

    [Test]
    public void EveryCodeHasAtLeastOneAppendixBMapping()
    {
        foreach (var entry in Registry.GetProperty("codes").EnumerateArray())
        {
            entry.GetProperty("mappings").GetArrayLength()
                .ShouldBeGreaterThan(0, entry.GetProperty("code").GetString());
        }
    }

    [Test]
    public void EveryDeclaredFieldExistsOnTheDiagnosticModel()
    {
        var members = typeof(Diagnostic).GetProperties()
            .Select(property => char.ToLowerInvariant(property.Name[0]) + property.Name[1..])
            .ToHashSet(StringComparer.Ordinal);

        foreach (var entry in Registry.GetProperty("codes").EnumerateArray())
        {
            foreach (var field in entry.GetProperty("fields").EnumerateArray())
            {
                members.ShouldContain(field.GetString()!);
            }
        }
    }

    [Test]
    public void RegistryDoesNotClaimOccurrenceLevelFacts()
    {
        var notAuthoritative = Registry.GetProperty("notAuthoritativeFor")
            .EnumerateArray().Select(value => value.GetString()).ToList();

        notAuthoritative.ShouldContain("phase");
        notAuthoritative.ShouldContain("spec");
        notAuthoritative.ShouldContain("message");
    }

    [Test]
    public void ExtractedStreamSchemaMatchesTheSpecificationBlock()
    {
        var schema = File.ReadAllText(RepositoryLayout.StreamSchema).ReplaceLineEndings("\n");
        var specification = File.ReadAllText(RepositoryLayout.Specification).ReplaceLineEndings("\n");

        specification.ShouldContain(schema.TrimEnd('\n'));
    }

    [Test]
    public void StreamSchemaEnumeratesExactlyTheImplementedPhases()
    {
        var schema = JsonDocument.Parse(File.ReadAllText(RepositoryLayout.StreamSchema)).RootElement;

        var phases = schema
            .GetProperty("items").GetProperty("properties").GetProperty("phase").GetProperty("enum")
            .EnumerateArray().Select(value => value.GetString()!).ToList();

        phases.ShouldBe(Enum.GetValues<DiagnosticPhase>()
            .Select(phase => phase.ToString().ToLowerInvariant()).ToList());
    }
}
