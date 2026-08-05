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
    private static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal)
    {
        "args.txt", "args-diagnostics.txt", "inputs", "schemes", "expected",
        "expected-diagnostics.json", "expected-exit-code.txt", "requirements.txt", "legacy.md",
    };

    public static IEnumerable<ConformanceCase> Cases => ConformanceCase.Discover(CorpusLayout.Corpus);

    [Test]
    public void CorpusIsNotEmpty() =>
        Cases.ShouldNotBeEmpty("the conformance corpus is the oracle; an empty corpus proves nothing.");

    [TestCaseSource(nameof(Cases))]
    public void CaseProducesTheExpectedTreeAndExitCode(ConformanceCase conformanceCase)
    {
        using var run = new CaseRun(conformanceCase);

        var result = ToolRunner.Run(conformanceCase.Arguments, run.Directory);

        var failures = new List<string>();

        if (result.ExitCode != conformanceCase.ExpectedExitCode)
        {
            failures.Add($"expected exit code {conformanceCase.ExpectedExitCode} but got {result.ExitCode}.");
        }

        failures.AddRange(OutputTreeComparer.Compare(conformanceCase.ExpectedTree, run.ProducedTree(ReservedNames)));

        failures.ShouldBeEmpty(Describe(conformanceCase, failures, result));
    }

    [TestCaseSource(nameof(Cases))]
    public void CaseProducesTheExpectedDiagnosticStream(ConformanceCase conformanceCase)
    {
        using var run = new CaseRun(conformanceCase);

        var result = ToolRunner.Run(conformanceCase.DiagnosticArguments, run.Directory);
        var expected = conformanceCase.ExpectedDiagnostics;

        if (expected is null)
        {
            // The informational modes of Section 6.1 write no stream at all, which is not the
            // same as writing an empty array.
            result.StandardError.ShouldBeEmpty(Describe(conformanceCase, [], result));
            return;
        }

        var failures = DiagnosticComparer.Compare(expected, result.StandardError);

        failures.ShouldBeEmpty(Describe(conformanceCase, failures, result));
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
        b.StandardError.ShouldBe(a.StandardError);

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

    /// <summary>A fresh working copy of a case, deleted when the run finishes.</summary>
    private sealed class CaseRun : IDisposable
    {
        internal CaseRun(ConformanceCase conformanceCase)
        {
            Directory = Path.Combine(Path.GetTempPath(), "n2x-case-" + Guid.NewGuid().ToString("n"));
            CopyDirectory(conformanceCase.Directory, Directory);
        }

        internal string Directory { get; }

        internal string ProducedTree(HashSet<string> reserved)
        {
            var staging = Directory + "-produced";
            System.IO.Directory.CreateDirectory(staging);

            foreach (var entry in System.IO.Directory.EnumerateFileSystemEntries(Directory))
            {
                if (reserved.Contains(Path.GetFileName(entry)))
                {
                    continue;
                }

                var target = Path.Combine(staging, Path.GetFileName(entry)!);

                if (System.IO.Directory.Exists(entry))
                {
                    CopyDirectory(entry, target);
                }
                else
                {
                    File.Copy(entry, target, overwrite: true);
                }
            }

            return staging;
        }

        public void Dispose()
        {
            Delete(Directory);
            Delete(Directory + "-produced");
        }

        private static void Delete(string path)
        {
            try
            {
                if (System.IO.Directory.Exists(path))
                {
                    System.IO.Directory.Delete(path, recursive: true);
                }
            }
            catch (IOException)
            {
                // A leaked temporary directory must never fail a conformance run.
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            System.IO.Directory.CreateDirectory(destination);

            foreach (var file in System.IO.Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(destination, Path.GetRelativePath(source, file));
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
        }
    }
}
