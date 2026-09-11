using Namespace2Xml.Gatekeeper;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

[TestFixture]
public class StaticGateValidatorTests
{
    [Test]
    public void WholeJobsWholeMatricesAndCompleteCellsResolveExactly()
    {
        WithCatalog(
            """
            on: push
            jobs:
              plain:
                name: 'ci:.github/workflows/test.yml::plain'
                runs-on: ubuntu-latest
                steps:
                  - run: echo ok
              matrix:
                name: 'ci:.github/workflows/test.yml::matrix[os=${{ toJSON(matrix.os) }},runtime=${{ toJSON(matrix.runtime) }}]'
                strategy:
                  matrix:
                    os: [linux, windows]
                    runtime: [9, 10]
                runs-on: ${{ matrix.os }}
                steps:
                  - run: echo ok
            """,
            catalog =>
            {
                var references = References(
                    "nunit:Tests::Tests.Fixture.Leaf",
                    "ci:.github/workflows/test.yml::plain",
                    "ci:.github/workflows/test.yml::matrix",
                    "ci:.github/workflows/test.yml::matrix[os=\"windows\",runtime=10]");

                Should.NotThrow(() => StaticGateValidator.Validate(references, Tests(), catalog));
            });
    }

    [Test]
    public void PartialUnknownAndNonMatrixCellsDoNotResolve()
    {
        WithCatalog(
            """
            on: push
            jobs:
              plain:
                name: 'ci:.github/workflows/test.yml::plain'
                runs-on: ubuntu-latest
                steps:
                  - run: echo ok
              matrix:
                name: 'ci:.github/workflows/test.yml::matrix[os=${{ toJSON(matrix.os) }},runtime=${{ toJSON(matrix.runtime) }}]'
                strategy:
                  matrix:
                    os: [linux, windows]
                    runtime: [9, 10]
                runs-on: ${{ matrix.os }}
                steps:
                  - run: echo ok
            """,
            catalog =>
            {
                string[] invalid =
                [
                    "ci:.github/workflows/test.yml::matrix[os=\"windows\"]",
                    "ci:.github/workflows/test.yml::matrix[os=\"other\",runtime=10]",
                    "ci:.github/workflows/test.yml::plain[os=\"linux\"]",
                ];

                foreach (var identity in invalid)
                {
                    var error = Should.Throw<InvalidDataException>(() =>
                        StaticGateValidator.Validate(References(identity), Tests(), catalog));
                    error.Message.ShouldContain("item 1", Case.Sensitive);
                }
            });
    }

    [Test]
    public void ReferencedJobsRequireTheExactCanonicalRuntimeName()
    {
        WithCatalog(
            """
            on: push
            jobs:
              matrix:
                name: Friendly display name
                strategy:
                  matrix:
                    os: [linux, windows]
                runs-on: ${{ matrix.os }}
                steps:
                  - run: echo ok
            """,
            catalog =>
            {
                var error = Should.Throw<InvalidDataException>(() =>
                    StaticGateValidator.Validate(
                        References("ci:.github/workflows/test.yml::matrix"),
                        Tests(),
                        catalog));

                error.Message.ShouldContain(
                    "ci:.github/workflows/test.yml::matrix[os=${{ toJSON(matrix.os) }}]",
                    Case.Sensitive);
            });
    }

    [Test]
    public void DuplicateFullyQualifiedLeavesCannotResolveAGate()
    {
        WithCatalog(
            """
            on: push
            jobs:
              plain:
                name: 'ci:.github/workflows/test.yml::plain'
                runs-on: ubuntu-latest
                steps:
                  - run: echo ok
            """,
            catalog =>
            {
                var tests = new NUnitTestCatalog(
                [
                    new KeyValuePair<string, IEnumerable<string>>(
                        "Tests",
                        ["Tests.Fixture.Leaf", "Tests.Fixture.Leaf"]),
                ]);

                var error = Should.Throw<InvalidDataException>(() =>
                    StaticGateValidator.Validate(
                        References("nunit:Tests::Tests.Fixture.Leaf"),
                        tests,
                        catalog));

                error.Message.ShouldContain("resolves 2 times", Case.Sensitive);
            });
    }

    private static NUnitTestCatalog Tests() =>
        new(
        [
            new KeyValuePair<string, IEnumerable<string>>(
                "Tests",
                ["Tests.Fixture.Leaf"]),
        ]);

    private static List<AssertionGateReference> References(params string[] identities) =>
        identities.Select(identity =>
            new AssertionGateReference(1, "required", GateIdentityParser.Parse(identity))).ToList();

    private static void WithCatalog(string workflow, Action<WorkflowCatalog> action)
    {
        var root = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "static-gate-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, ".github", "workflows");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(Path.Combine(directory, "test.yml"), workflow);
            action(WorkflowCatalog.Load(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
