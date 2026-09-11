using System.Text;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.Conformance;

[TestFixture]
[Category("Differential")]
public sealed class DifferentialCultureTests
{
    [Test]
    public void RuntimeCultureChangesTheBaselineButNotTheReplacement()
    {
        var baseline = LegacyBaseline.EntryAssembly;

        if (baseline is null)
        {
            Assert.Ignore(LegacyBaseline.WhyInert);
        }

        LegacyBaseline.RequireRuntime();

        var replacementEnglish = ObserveReplacement("en_US.UTF-8");
        var replacementGerman = ObserveReplacement("de_DE.UTF-8");
        const string Expected = "{\n  \"n\": \"1,5\"\n}\n";

        replacementEnglish.ExitCode.ShouldBe(0);
        replacementGerman.ExitCode.ShouldBe(0);
        replacementEnglish.Output.ShouldBe(Expected);
        replacementGerman.Output.ShouldBe(Expected);
        replacementGerman.Fingerprint.ShouldBe(replacementEnglish.Fingerprint);

        var baselineEnglish = ObserveBaseline(baseline!, "en_US.UTF-8");
        var baselineGerman = ObserveBaseline(baseline!, "de_DE.UTF-8");

        baselineEnglish.ExitCode.ShouldBe(0);
        baselineGerman.ExitCode.ShouldBe(0);
        baselineEnglish.Fingerprint.ShouldNotBe(baselineGerman.Fingerprint);
        baselineEnglish.Output.ShouldNotBe(Expected);
        baselineGerman.Output.ShouldNotBe(Expected);
    }

    private static Observation ObserveReplacement(string culture) =>
        Observe(
            culture,
            (arguments, directory) => ToolRunner.Run(arguments, directory, culture));

    private static Observation ObserveBaseline(string baseline, string culture) =>
        Observe(
            culture,
            (arguments, directory) =>
                ToolRunner.Run(baseline, arguments, directory, LegacyBaseline.Host, culture));

    private static Observation Observe(
        string culture,
        Func<IReadOnlyList<string>, string, ToolResult> run)
    {
        var directory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "differential-culture-" + culture.Replace('.', '-') + "-"
            + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(
                Path.Combine(directory, "input.txt"),
                "cfg.n=1,5\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(
                Path.Combine(directory, "scheme.txt"),
                "cfg.output=json\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var result = run(["-i", "input.txt", "-s", "scheme.txt"], directory);
            var output = File.ReadAllText(
                Path.Combine(directory, "cfg.json"),
                Encoding.UTF8);

            return new Observation(
                result.ExitCode,
                output,
                result.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "\n"
                + Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        Encoding.UTF8.GetBytes(output))));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed record Observation(int ExitCode, string Output, string Fingerprint);
}
