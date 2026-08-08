using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.Conformance;

/// <summary>
/// Drives the Appendix C corpus. Every case runs twice: once verbatim to verify the exit code and
/// the produced output tree, and once in the JSON diagnostic encoding to verify the diagnostic
/// stream. The tool is always observed as a separate process with a real argument vector.
/// </summary>
[TestFixture]
public class ConformanceCaseTests
{
    /// <summary>Names owned by the fixture. Everything else in the run directory is a destination.</summary>
    private static readonly HashSet<string> ReservedNames = CorpusLayout.ReservedNames;

    public static IEnumerable<ConformanceCase> Cases => ConformanceCase.Discover(CorpusLayout.Corpus);

    [Test]
    public void CorpusIsNotEmpty() =>
        Cases.ShouldNotBeEmpty("the conformance corpus is the oracle; an empty corpus proves nothing.");

    [TestCaseSource(nameof(Cases))]
    public void CaseProducesTheExpectedTreeAndExitCode(ConformanceCase conformanceCase)
    {
        using var run = new CaseRun(conformanceCase);

        var before = run.FixtureSnapshot(ReservedNames);
        var result = ToolRunner.Run(conformanceCase.Arguments, run.Directory);

        var failures = new List<string>();

        if (result.ExitCode != conformanceCase.ExpectedExitCode)
        {
            failures.Add($"expected exit code {conformanceCase.ExpectedExitCode} but got {result.ExitCode}.");
        }

        failures.AddRange(OutputTreeComparer.Compare(conformanceCase.ExpectedTree, run.ProducedTree(ReservedNames)));
        failures.AddRange(StandardOutputComparer.Compare(conformanceCase.ExpectedStandardOutput, result.StandardOutput));
        failures.AddRange(CompareFixtureSnapshots(before, run.FixtureSnapshot(ReservedNames)));

        failures.ShouldBeEmpty(Describe(conformanceCase, failures, result));
    }

    [TestCaseSource(nameof(Cases))]
    public void CaseProducesTheExpectedDiagnosticStream(ConformanceCase conformanceCase)
    {
        using var run = new CaseRun(conformanceCase);

        var before = run.FixtureSnapshot(ReservedNames);
        var result = ToolRunner.Run(conformanceCase.DiagnosticArguments, run.Directory);
        var expected = conformanceCase.ExpectedDiagnostics;

        var failures = new List<string>();

        if (expected is null)
        {
            // The informational modes of Section 6.1 write no stream at all, which is not the
            // same as writing an empty array.
            if (result.StandardError.Length != 0)
            {
                failures.Add("the case declares no diagnostic stream, so standard error must be empty.");
            }
        }
        else
        {
            failures.AddRange(DiagnosticComparer.Compare(expected, result.StandardError));
        }

        // Section 6.4: the diagnostic encoding never changes the generated output files or the
        // exit code. Checking only standard error here would let JSON mode return a different
        // status, or create a file, without any case noticing.
        if (result.ExitCode != conformanceCase.ExpectedExitCode)
        {
            failures.Add(
                $"the diagnostic encoding changed the exit code: expected " +
                $"{conformanceCase.ExpectedExitCode} but got {result.ExitCode}.");
        }

        failures.AddRange(OutputTreeComparer.Compare(conformanceCase.ExpectedTree, run.ProducedTree(ReservedNames)));
        failures.AddRange(StandardOutputComparer.Compare(conformanceCase.ExpectedStandardOutput, result.StandardOutput));
        failures.AddRange(CompareFixtureSnapshots(before, run.FixtureSnapshot(ReservedNames)));

        failures.ShouldBeEmpty(Describe(conformanceCase, failures, result));
    }

    /// <summary>
    /// Fixture-owned files are inputs, not destinations. Reserved names are excluded from the
    /// produced tree, so without this the tool could rewrite a case's own inputs, or drop a file
    /// into <c>inputs/</c>, and every assertion would still pass.
    /// </summary>
    private static IEnumerable<string> CompareFixtureSnapshots(
        Dictionary<string, string> before,
        Dictionary<string, string> after)
    {
        foreach (var entry in before)
        {
            if (!after.TryGetValue(entry.Key, out var hash))
            {
                yield return $"the tool deleted fixture-owned entry '{entry.Key}'.";
            }
            else if (!string.Equals(hash, entry.Value, StringComparison.Ordinal))
            {
                yield return $"the tool modified fixture-owned entry '{entry.Key}'.";
            }
        }

        foreach (var key in after.Keys.Where(key => !before.ContainsKey(key)).Order(StringComparer.Ordinal))
        {
            yield return $"the tool created '{key}' inside a fixture-owned directory.";
        }
    }

    [TestCaseSource(nameof(Cases))]
    public void CaseIsByteIdenticalAcrossRepeatedRuns(ConformanceCase conformanceCase)
    {
        // Section 24: identical inputs must produce identical bytes. A single run cannot show that.
        using var first = new CaseRun(conformanceCase);
        using var second = new CaseRun(conformanceCase);

        var a = ToolRunner.Run(conformanceCase.DiagnosticArguments, first.Directory);
        var b = ToolRunner.Run(conformanceCase.DiagnosticArguments, second.Directory);

        b.ExitCode.ShouldBe(a.ExitCode);

        // Section 24 fixes what must repeat: "Diagnostic codes, severities, structured fields, and
        // ordering must be identical; localized human-readable prose may differ." Comparing the raw
        // bytes would also compare the prose, and Section 7.2 requires the missing-file warning to
        // contain "its resolved path" — a value that necessarily differs between two working
        // copies. The comparer validates framing, layout, and every structured member, so nothing
        // Section 24 does require goes unchecked.
        //
        // Section 6.4.1 gives `--help` and `--version` no diagnostic stream in either encoding. An
        // absent stream carries no structured field, so there the bytes are the whole content.
        if (a.StandardError.Length == 0 || b.StandardError.Length == 0)
        {
            b.StandardError.ShouldBe(a.StandardError);
        }
        else
        {
            DiagnosticComparer.Compare(a.StandardError, b.StandardError).ShouldBeEmpty();
        }

        OutputTreeComparer
            .Compare(first.ProducedTree(ReservedNames), second.ProducedTree(ReservedNames))
            .ShouldBeEmpty();
    }

    private static string Describe(ConformanceCase conformanceCase, IReadOnlyList<string> failures, ToolResult result)
    {
        var stderr = System.Text.Encoding.UTF8.GetString(result.StandardError);

        return $"{conformanceCase.Name}:{Environment.NewLine}" +
               string.Join(Environment.NewLine, failures.Select(failure => "  - " + failure)) +
               $"{Environment.NewLine}  stderr: {stderr}";
    }
}
