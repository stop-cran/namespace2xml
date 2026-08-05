using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Namespace2Xml.Diagnostics;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Gates on <c>spec/diagnostics.registry.json</c>, which declares itself authoritative for the set
/// of codes, each code's severity, its cardinality, and the members it may carry.
/// </summary>
/// <remarks>
/// <para>
/// The generated surface is checked in, so nothing forces it to be regenerated. CI regenerating and
/// diffing catches a stale artifact in the repository; these tests catch the case CI cannot see —
/// a registry the compiled assembly no longer agrees with — and they name the specific fact that
/// diverged instead of printing a diff.
/// </para>
/// <para>
/// Every comparison here is deliberately two-directional. A one-directional check passes while the
/// generator silently drops codes, which is the failure mode most likely to go unnoticed: a missing
/// diagnostic produces no output to be surprised by.
/// </para>
/// </remarks>
public sealed class DiagnosticRegistryDriftTests
{
    private static readonly ImmutableArray<RegistryEntry> Registry = LoadRegistry();

    /// <summary>
    /// Every other test in this class iterates <see cref="Registry"/>, so all of them would pass
    /// vacuously against an empty or truncated load — and the set-equality test would pass against
    /// two empty sets. This is the assertion that makes the rest mean something.
    /// </summary>
    [Test]
    public void TheRegistryLoadsEveryCodeItDeclares()
    {
        var declared = JsonDocument.Parse(File.ReadAllBytes(RepositoryLayout.Registry))
            .RootElement.GetProperty("codes").GetArrayLength();

        declared.ShouldBeGreaterThan(0);
        Registry.Length.ShouldBe(declared);
        DiagnosticCodes.All.Length.ShouldBe(declared);
    }

    [Test]
    public void GeneratedCodeSetEqualsRegistryCodeSet()
    {
        var generated = DiagnosticCodes.All.Select(info => info.Code).ToHashSet(StringComparer.Ordinal);
        var registry = Registry.Select(entry => entry.Code).ToHashSet(StringComparer.Ordinal);

        registry.Except(generated).ShouldBeEmpty("the registry declares codes the generated surface does not");
        generated.Except(registry).ShouldBeEmpty("the generated surface declares codes the registry does not");
    }

    [Test]
    public void GeneratedOrderMatchesRegistryOrder() =>
        DiagnosticCodes.All.Select(info => info.Code)
            .ShouldBe(Registry.Select(entry => entry.Code));

    [Test]
    public void EverySeverityMatchesTheRegistry()
    {
        foreach (var entry in Registry)
        {
            var expected = entry.Severity switch
            {
                "error" => DiagnosticSeverity.Error,
                "warning" => DiagnosticSeverity.Warning,
                _ => throw new InvalidOperationException($"{entry.Code} has unknown severity '{entry.Severity}'."),
            };

            Info(entry.Code).Severity.ShouldBe(expected, entry.Code);
        }
    }

    [Test]
    public void EveryCardinalityAndConditionMatchesTheRegistry()
    {
        foreach (var entry in Registry)
        {
            Info(entry.Code).Cardinality.ShouldBe(entry.Cardinality, entry.Code);
            Info(entry.Code).Condition.ShouldBe(entry.Condition, entry.Code);
        }
    }

    [Test]
    public void EveryFieldSetMatchesTheRegistryInBothDirections()
    {
        foreach (var entry in Registry)
        {
            var generated = Info(entry.Code).Fields.ToHashSet(StringComparer.Ordinal);
            var declared = entry.Fields.ToHashSet(StringComparer.Ordinal);

            declared.Except(generated).ShouldBeEmpty($"{entry.Code} declares fields the generated surface omits");
            generated.Except(declared).ShouldBeEmpty($"{entry.Code} carries fields the registry does not declare");
        }
    }

    /// <summary>
    /// The factory signature is the enforcement mechanism, so it is what has to be checked. A field
    /// set that agrees with the registry in <see cref="DiagnosticCodes.All"/> while the factory
    /// accepts something else would leave the compile-time guarantee undelivered and every other
    /// test in this class green.
    /// </summary>
    [Test]
    public void EveryFactorySignatureMatchesItsRegistryFieldSet()
    {
        var alwaysRequired = new[] { "phase", "spec", "message" };

        foreach (var entry in Registry)
        {
            var factory = Factory(entry.Code);
            var parameters = factory.GetParameters().Select(parameter => parameter.Name!).ToArray();

            parameters.Take(3).ShouldBe(alwaysRequired, entry.Code);

            var scopedToInvocation = entry.Cardinality == "once per invocation";
            var expectedKey = scopedToInvocation ? Array.Empty<string>() : ["cardinalityKey"];
            var fieldParameters = parameters.Skip(3).ToArray();

            fieldParameters.Take(expectedKey.Length).ShouldBe(expectedKey, entry.Code);

            fieldParameters.Skip(expectedKey.Length)
                .ToHashSet(StringComparer.Ordinal)
                .ShouldBe(entry.Fields.ToHashSet(StringComparer.Ordinal), entry.Code);
        }
    }

    [Test]
    public void EveryFactoryStampsItsOwnCodeAndSeverity()
    {
        foreach (var entry in Registry)
        {
            var occurrence = Invoke(entry.Code);

            occurrence.Diagnostic.Code.ShouldBe(entry.Code);
            occurrence.Diagnostic.Severity.ShouldBe(Info(entry.Code).Severity, entry.Code);
        }
    }

    [Test]
    public void InvocationScopedFactoriesShareOneCardinalityKey()
    {
        foreach (var entry in Registry.Where(entry => entry.Cardinality == "once per invocation"))
        {
            Invoke(entry.Code).CardinalityKey.ShouldBe(DiagnosticCodes.Invocation, entry.Code);
        }
    }

    private static DiagnosticCodeInfo Info(string code) =>
        DiagnosticCodes.All.Single(info => info.Code == code);

    private static MethodInfo Factory(string code)
    {
        // CLI001 -> Cli001. ToLowerInvariant leaves the trailing digits alone.
        var name = string.Concat(code.AsSpan(0, 1), code[1..].ToLowerInvariant());
        var method = typeof(DiagnosticCodes).GetMethod(name, BindingFlags.Public | BindingFlags.Static);

        method.ShouldNotBeNull($"no factory named '{name}' exists for registry code '{code}'");
        return method;
    }

    /// <summary>
    /// Calls a factory with a placeholder for every parameter, which is enough to read back the
    /// facts the factory stamps without the test needing to know each signature.
    /// </summary>
    private static DiagnosticOccurrence Invoke(string code)
    {
        var factory = Factory(code);
        var arguments = factory.GetParameters()
            .Select(parameter => parameter.Name switch
            {
                "phase" => DiagnosticPhase.Cli,
                "spec" => (object?)"§22",
                "message" => "placeholder",
                "cardinalityKey" => "key",
                _ => null,
            })
            .ToArray();

        return (DiagnosticOccurrence)factory.Invoke(null, arguments)!;
    }

    private static ImmutableArray<RegistryEntry> LoadRegistry()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(RepositoryLayout.Registry));

        return [.. document.RootElement.GetProperty("codes").EnumerateArray().Select(element =>
            new RegistryEntry(
                element.GetProperty("code").GetString()!,
                element.GetProperty("severity").GetString()!,
                element.GetProperty("cardinality").GetString()!,
                element.GetProperty("condition").GetString()!,
                [.. element.GetProperty("fields").EnumerateArray().Select(field => field.GetString()!)]))];
    }

    private sealed record RegistryEntry(
        string Code,
        string Severity,
        string Cardinality,
        string Condition,
        ImmutableArray<string> Fields);
}
