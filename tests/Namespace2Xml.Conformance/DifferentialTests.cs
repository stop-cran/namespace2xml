using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.Conformance;

/// <summary>
/// The Appendix C.6 differential lane. Runs the pinned namespace2xml 2.4.0 package over the whole
/// corpus and checks each case's declared verdict against what the baseline actually does.
/// <para>
/// The baseline is compared against the case's <em>expected</em> tree and exit code, never against
/// what the tool under test produced. Comparing two binaries to each other would answer "do these
/// agree", which is not the question: Section 3 is a claim about 2.4.0 and the contract, and a
/// version of this harness that compared implementations would go green if both were wrong in the
/// same way.
/// </para>
/// </summary>
[TestFixture]
[Category("Differential")]
public class DifferentialTests
{
    public static IEnumerable<ConformanceCase> Cases => ConformanceCase.Discover(CorpusLayout.Corpus);

    /// <summary>
    /// How many times each case's baseline behaviour is sampled. Appendix C.6 requires more than
    /// one because a single run cannot distinguish a stable result from a lucky one.
    /// </summary>
    private const int Samples = 10;

    /// <summary>
    /// The bound on samples taken for a case that claims <c>nondeterministic</c>. Appendix C.6 makes
    /// that verdict unrefutable by sampling — one branch of the baseline was measured at one run in
    /// forty, and a budget large enough to be confident of catching it is a budget whose own result
    /// is decided by chance — so the case is run once to prove it still runs and nothing is asserted
    /// about which way it fell.
    /// </summary>
    private const int NondeterminismSamples = 1;

    [TestCaseSource(nameof(Cases))]
    public void TheBaselineBehavesAsTheCaseClaims(ConformanceCase conformanceCase)
    {
        ArgumentNullException.ThrowIfNull(conformanceCase);

        var baseline = RequireBaseline();
        var claim = LegacyClaim.Read(conformanceCase);

        if (claim?.Verdict == LegacyVerdict.Nondeterministic)
        {
            // Appendix C.6: sampling refutes, it does not confirm. The case has already recorded a
            // baseline observed to fall both ways, and no set of samples can contradict that, so the
            // measurement lives in the case's prose and the lane only proves the case still runs.
            Sample(baseline, conformanceCase, NondeterminismSamples);

            return;
        }

        var samples = Sample(baseline, conformanceCase, Samples);

        // Appendix C.6 partitions the samples by how they stand to the expected result, not by
        // their exact bytes. A baseline that produces two different wrong answers is wrong either
        // way; scoring the bytes would make this lane fail on the schedule of whatever varies.
        var agreeing = samples.Count(sample => sample.Divergences.Count == 0);

        Straddles(samples).ShouldBeFalse(
            $"{conformanceCase.Name} claims '{claim?.Line ?? "no verdict"}', but {agreeing} of " +
            $"{samples.Count} identical runs reproduced the expected result and the rest did not. " +
            $"Appendix C.6 requires the nondeterministic verdict for that.{Environment.NewLine}" +
            DescribeSamples(samples));

        var divergences = samples[0].Divergences;

        if (claim is null)
        {
            divergences.ShouldBeEmpty(
                $"{conformanceCase.Name} diverges from namespace2xml 2.4.0 but declares no Appendix C.6 " +
                "verdict explaining it. An unexplained divergence is the one thing this lane exists to " +
                $"catch.{Environment.NewLine}{Describe(divergences)}");

            return;
        }

        if (claim.ExpectsAgreement)
        {
            divergences.ShouldBeEmpty(
                $"{conformanceCase.Name} claims '{claim.Line}', so Appendix C.6 requires the baseline " +
                $"to produce this case's expected tree and exit code, and it does not.{Environment.NewLine}" +
                $"{Describe(divergences)}");

            return;
        }

        divergences.ShouldNotBeEmpty(
            $"{conformanceCase.Name} claims '{claim.Line}', but the baseline produced exactly the " +
            "expected tree and exit code. Either the correction was never needed or the case does not " +
            "reach it; a divergence nobody can observe is not a correction.");

        if (claim.Verdict == LegacyVerdict.Crashes)
        {
            samples.ShouldAllBe(
                sample => sample.ExitCode != 0,
                $"{conformanceCase.Name} claims the baseline crashes, but at least one run exited " +
                "successfully.");
        }
    }

    /// <summary>
    /// Appendix C.6 pins the baseline by digest and size. This asserts the refusal directly, so the
    /// gate is proved by a test rather than by the absence of a failure everywhere else.
    /// </summary>
    [Test]
    public void ABaselinePackageThatIsNotThePinnedOneIsRefused()
    {
        var forged = Path.Combine(Path.GetTempPath(), "n2x-forged-" + Guid.NewGuid().ToString("n") + ".nupkg");
        File.WriteAllBytes(forged, new byte[LegacyBaseline.PinnedSize]);

        try
        {
            // Right size, wrong content: the size check alone would pass this, so the failure that
            // fires here is the digest check and not a shortcut in front of it.
            var refusal = Should.Throw<BaselineIntegrityException>(() => LegacyBaseline.Verify(forged));

            refusal.Message.ShouldContain(LegacyBaseline.PinnedSha256, Case.Sensitive);
        }
        finally
        {
            File.Delete(forged);
        }
    }

    /// <summary>A package of the wrong length is refused before it is read.</summary>
    [Test]
    public void ABaselinePackageOfTheWrongSizeIsRefused()
    {
        var truncated = Path.Combine(Path.GetTempPath(), "n2x-short-" + Guid.NewGuid().ToString("n") + ".nupkg");
        File.WriteAllBytes(truncated, [0x50, 0x4B, 0x03, 0x04]);

        try
        {
            Should.Throw<BaselineIntegrityException>(() => LegacyBaseline.Verify(truncated))
                .Message.ShouldContain("bytes", Case.Sensitive);
        }
        finally
        {
            File.Delete(truncated);
        }
    }

    /// <summary>One baseline run: what it produced, and how that differed from what the case expects.</summary>
    private sealed record Observation(int ExitCode, List<string> Divergences, string Fingerprint);

    /// <summary>
    /// Whether the samples disagree about the one thing a verdict claims: some reproduced the
    /// case's expected result and some did not.
    /// </summary>
    private static bool Straddles(List<Observation> samples)
    {
        var agreeing = samples.Count(sample => sample.Divergences.Count == 0);

        return agreeing > 0 && agreeing < samples.Count;
    }

    /// <summary>Runs the baseline the given number of times and records each result.</summary>
    private static List<Observation> Sample(
        string baseline,
        ConformanceCase conformanceCase,
        int count)
    {
        var observations = new List<Observation>(count);

        for (var sample = 0; sample < count; sample++)
        {
            observations.Add(Observe(baseline, conformanceCase));
        }

        return observations;
    }

    /// <summary>
    /// Runs the baseline once and lists every way its result differs from what the case expects.
    /// </summary>
    private static Observation Observe(string baseline, ConformanceCase conformanceCase)
    {
        using var run = new CaseRun(conformanceCase);

        var result = ToolRunner.Run(baseline, conformanceCase.Arguments, run.Directory, LegacyBaseline.Host);
        var produced = run.ProducedTree(CorpusLayout.ReservedNames);

        var divergences = new List<string>();

        if (result.ExitCode != conformanceCase.ExpectedExitCode)
        {
            divergences.Add(
                $"exit code {result.ExitCode}, expected {conformanceCase.ExpectedExitCode}.");
        }

        divergences.AddRange(OutputTreeComparer.Compare(conformanceCase.ExpectedTree, produced));

        return new Observation(result.ExitCode, divergences, Fingerprint(result.ExitCode, produced));
    }

    /// <summary>
    /// Names the distinct results a case produced, so an instability report says what varied rather
    /// than only that something did.
    /// </summary>
    private static string DescribeSamples(List<Observation> samples)
    {
        var groups = samples
            .GroupBy(sample => sample.Fingerprint, StringComparer.Ordinal)
            .Select((group, index) =>
                $"  result {index + 1} ({group.Count()} of {samples.Count} runs): exit " +
                $"{group.First().ExitCode}, " +
                (group.First().Divergences.Count == 0
                    ? "matches the expected tree"
                    : string.Join("; ", group.First().Divergences)));

        return string.Join(Environment.NewLine, groups);
    }

    private static string Fingerprint(int exitCode, string tree)
    {
        var entries = Directory
            .EnumerateFiles(tree, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(tree, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .Select(relative =>
                relative + ":" +
                Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        File.ReadAllBytes(Path.Combine(tree, relative)))));

        return exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture) +
               "\n" + string.Join("\n", entries);
    }

    private static string Describe(List<string> divergences) =>
        string.Join(Environment.NewLine, divergences.Select(divergence => "  - " + divergence));

    private static string RequireBaseline()
    {
        var assembly = LegacyBaseline.EntryAssembly;

        if (assembly is null)
        {
            Assert.Ignore(LegacyBaseline.WhyInert);
        }

        return assembly!;
    }
}
