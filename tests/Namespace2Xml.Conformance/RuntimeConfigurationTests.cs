using System.Text.Json;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.Conformance;

/// <summary>
/// Guards the runtime settings the contract rests on, as they are shipped rather than as they are
/// written in a project file.
/// </summary>
/// <remarks>
/// <para>
/// Appendix C.7 repeats every fixture under locales with different decimal conventions.
/// <c>tools/hash-corpus-outputs.ps1</c> sets <c>LANG</c> and <c>LC_ALL</c> to do so, and they have
/// no effect: invariant globalization makes the current culture invariant whatever the host says,
/// which is why Section 3 states that behaviour cannot vary with the host locale and that this
/// will not become configurable.
/// </para>
/// <para>
/// That is a stronger guarantee than the probe, not a weaker one — but only while the flag is
/// actually set. Remove it and the determinism script keeps passing, because it would then be
/// measuring a locale-sensitive tool with variables that finally mean something, on a Windows
/// runner where they still do not. So the guarantee is asserted here directly, against the
/// runtime configuration the host reads at startup.
/// </para>
/// </remarks>
[TestFixture]
public sealed class RuntimeConfigurationTests
{
    [Test]
    public void TheShippedToolRunsWithInvariantGlobalization()
    {
        JsonElement properties = ConfigProperties();

        properties.TryGetProperty("System.Globalization.Invariant", out JsonElement invariant)
            .ShouldBeTrue(
                "Section 3 makes locale-independence a property of the tool rather than of its " +
                "environment, and InvariantGlobalization is what enforces it.");
        invariant.GetBoolean().ShouldBeTrue();
    }

    [Test]
    public void TheShippedToolAdmitsNoHostDefinedCultures()
    {
        JsonElement properties = ConfigProperties();

        properties.TryGetProperty("System.Globalization.PredefinedCulturesOnly", out JsonElement predefined)
            .ShouldBeTrue(
                "A culture constructed from host data could reintroduce locale-sensitive " +
                "comparison through a path invariant mode alone does not close.");
        predefined.GetBoolean().ShouldBeTrue();
    }

    private static JsonElement ConfigProperties()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "namespace2xml.runtimeconfig.json");
        File.Exists(path).ShouldBeTrue($"The tool's runtime configuration was not found at '{path}'.");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));

        return document.RootElement
            .GetProperty("runtimeOptions")
            .GetProperty("configProperties")
            .Clone();
    }
}
