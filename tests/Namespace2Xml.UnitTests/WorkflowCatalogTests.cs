using Namespace2Xml.Gatekeeper;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

[TestFixture]
public class WorkflowCatalogTests
{
    [Test]
    public void SupportedScalarSequenceMappingAndScheduleTriggersAreCataloged()
    {
        WithRepository(root =>
        {
            WriteWorkflow(root, "first.yml", Workflow("on: push\n", "first"));
            WriteWorkflow(
                root,
                "second.yaml",
                "on: [pull_request, workflow_dispatch]\n"
                + ExecutableJob("second"));
            WriteWorkflow(
                root,
                "third.yml",
                "on:\n"
                + "  push:\n"
                + "    branches: [master]\n"
                + "  pull_request:\n"
                + "  workflow_dispatch:\n"
                + "  schedule:\n"
                + "    - cron: '0 0 * * *'\n"
                + ExecutableJob("third"));

            var catalog = WorkflowCatalog.Load(root);

            catalog.Workflows.Keys.ShouldBe(
            [
                ".github/workflows/first.yml",
                ".github/workflows/second.yaml",
                ".github/workflows/third.yml",
            ]);
            catalog.Workflows.Values.ShouldAllBe(workflow => workflow.HasTrigger);
        });
    }

    [Test]
    public void NullEmptyAndUnsupportedTriggersFailClosed()
    {
        string[] triggers =
        [
            "on:\n",
            "on: []\n",
            "on: {}\n",
            "on: made_up\n",
            "on: [push, made_up]\n",
        ];

        foreach (var trigger in triggers)
        {
            WithRepository(root =>
            {
                WriteWorkflow(
                    root,
                    "invalid.yml",
                    Workflow(trigger, "build"));

                Should.Throw<InvalidDataException>(() => WorkflowCatalog.Load(root));
            });
        }
    }

    [Test]
    public void DuplicateEventsFailClosed()
    {
        string[] triggers =
        [
            "on: [push, push]\n",
            "on:\n  push:\n  push:\n",
        ];

        foreach (var trigger in triggers)
        {
            WithRepository(root =>
            {
                WriteWorkflow(
                    root,
                    "duplicate-event.yml",
                    Workflow(trigger, "build"));

                Should.Throw<InvalidDataException>(() => WorkflowCatalog.Load(root));
            });
        }
    }

    [Test]
    public void MalformedEventConfigurationsFailClosed()
    {
        string[] triggers =
        [
            "on:\n  push: branches\n",
            "on:\n  push: ''\n",
            "on:\n  schedule:\n",
            "on:\n  schedule: {}\n",
            "on:\n  schedule:\n    - cron:\n",
            "on:\n  push:\n    branches: [master]\n    branches: [v3]\n",
        ];

        foreach (var trigger in triggers)
        {
            WithRepository(root =>
            {
                WriteWorkflow(
                    root,
                    "malformed-event.yml",
                    Workflow(trigger, "build"));

                Should.Throw<InvalidDataException>(() => WorkflowCatalog.Load(root));
            });
        }
    }

    [Test]
    public void EmptyWorkflowDirectoriesFailClosed()
    {
        WithRepository(root =>
        {
            Directory.CreateDirectory(Path.Combine(root, ".github", "workflows"));

            Should.Throw<InvalidDataException>(() => WorkflowCatalog.Load(root));
        });
    }

    [Test]
    public void MalformedYamlAndNonMappingJobsFailClosed()
    {
        WithRepository(root =>
        {
            var malformed = WriteWorkflow(root, "malformed.yml", "on: [\njobs: {}\n");
            Should.Throw<InvalidDataException>(() => WorkflowCatalog.Load(root));
            File.Delete(malformed);

            WriteWorkflow(root, "jobs.yml", "on: push\njobs: nope\n");
            Should.Throw<InvalidDataException>(() => WorkflowCatalog.Load(root));
        });
    }

    [Test]
    public void DuplicateCanonicalPathsFailClosed()
    {
        WithRepository(root =>
        {
            var path = WriteWorkflow(
                root,
                "duplicate.yml",
                Workflow("on: push\n", "build"));
            var source = new WorkflowSource(path, ".github/workflows/duplicate.yml");

            Should.Throw<InvalidDataException>(() => WorkflowCatalog.Load([source, source]));
        });
    }

    [Test]
    public void ScheduleRequiresAValidFiveFieldCronExpression()
    {
        string[] invalid =
        [
            "schedule",
            "[schedule]",
            "schedule: '0 0 * * *'",
            "schedule: [cron]",
            "schedule:\n    - cron: '* * * *'",
            "schedule:\n    - cron: '60 0 * * *'",
            "schedule:\n    - cron: '0 24 * * *'",
            "schedule:\n    - cron: '0 0 0 * *'",
            "schedule:\n    - cron: '0 0 * 13 *'",
            "schedule:\n    - cron: '0 0 * * 7'",
            "schedule:\n    - cron: '0 0 * * ?'",
        ];

        foreach (var trigger in invalid)
        {
            WithRepository(root =>
            {
                WriteWorkflow(
                    root,
                    "invalid-schedule.yml",
                    Workflow($"on:\n  {trigger}\n", "build"));

                Should.Throw<InvalidDataException>(() => WorkflowCatalog.Load(root), trigger);
            });
        }
    }

    [Test]
    public void OrdinaryAndReusableWorkflowJobsAreExecutableCatalogEntries()
    {
        WithRepository(root =>
        {
            WriteWorkflow(
                root,
                "executable.yml",
                "on: push\n"
                + "jobs:\n"
                + "  ordinary:\n"
                + "    runs-on: [self-hosted, linux]\n"
                + "    steps:\n"
                + "      - uses: actions/checkout@v4\n"
                + "      - run: dotnet test\n"
                + "  grouped:\n"
                + "    runs-on:\n"
                + "      group: production\n"
                + "      labels: [linux, x64]\n"
                + "    steps:\n"
                + "      - run: echo ok\n"
                + "  reusable:\n"
                + "    uses: owner/repository/.github/workflows/build.yml@v1\n");

            var catalog = WorkflowCatalog.Load(root);

            catalog.Workflows.Single().Value.Jobs.Keys.ShouldBe(
                ["ordinary", "grouped", "reusable"]);
        });
    }

    [Test]
    public void NonexecutableAndMixedJobsFailClosed()
    {
        string[] jobs =
        [
            "    runs-on: ubuntu-latest\n",
            "    steps:\n      - run: echo ok\n",
            "    runs-on: ''\n    steps:\n      - run: echo ok\n",
            "    runs-on: ubuntu-latest\n    steps: []\n",
            "    runs-on: ubuntu-latest\n    steps:\n      - name: inert\n",
            "    runs-on: ubuntu-latest\n    steps:\n      - run: echo ok\n        uses: actions/checkout@v4\n",
            "    runs-on: ubuntu-latest\n    steps:\n      - run: ''\n",
            "    uses: owner/repository/.github/workflows/build.yml@v1\n    runs-on: ubuntu-latest\n",
            "    uses: ''\n",
        ];

        foreach (var job in jobs)
        {
            WithRepository(root =>
            {
                WriteWorkflow(root, "invalid-job.yml", "on: push\njobs:\n  build:\n" + job);

                Should.Throw<InvalidDataException>(() => WorkflowCatalog.Load(root), job);
            });
        }
    }

    [Test]
    public void StaticallyFalseJobsFailClosed()
    {
        string[] jobs =
        [
            "    if: false\n"
            + "    runs-on: ubuntu-latest\n"
            + "    steps:\n"
            + "      - run: echo unreachable\n",
            "    if: ${{ false }}\n"
            + "    uses: owner/repository/.github/workflows/build.yml@v1\n",
        ];

        foreach (var job in jobs)
        {
            WithRepository(root =>
            {
                WriteWorkflow(root, "static-false-job.yml", "on: push\njobs:\n  build:\n" + job);

                Should.Throw<InvalidDataException>(() => WorkflowCatalog.Load(root))
                    .Message.ShouldContain("statically false condition");
            });
        }
    }

    [Test]
    public void AllStaticallyFalseStepsFailClosedButDynamicOrRunnableStepsRemainEligible()
    {
        WithRepository(root =>
        {
            WriteWorkflow(
                root,
                "all-static-false.yml",
                "on: push\n"
                + "jobs:\n"
                + "  build:\n"
                + "    runs-on: ubuntu-latest\n"
                + "    steps:\n"
                + "      - if: False\n"
                + "        run: echo unreachable\n"
                + "      - if: ${{ false }}\n"
                + "        uses: actions/checkout@v4\n");

            Should.Throw<InvalidDataException>(() => WorkflowCatalog.Load(root))
                .Message.ShouldContain("every step condition is statically false");
        });

        WithRepository(root =>
        {
            WriteWorkflow(
                root,
                "potentially-runnable.yml",
                "on: push\n"
                + "jobs:\n"
                + "  dynamic:\n"
                + "    if: ${{ github.ref == 'refs/heads/master' }}\n"
                + "    runs-on: ubuntu-latest\n"
                + "    steps:\n"
                + "      - if: false\n"
                + "        run: echo unreachable\n"
                + "      - if: ${{ success() }}\n"
                + "        run: echo dynamic\n");

            Should.NotThrow(() => WorkflowCatalog.Load(root));
        });
    }

    private static void WithRepository(Action<string> action)
    {
        var root = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "workflow-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            action(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string WriteWorkflow(string root, string name, string text)
    {
        var directory = Path.Combine(root, ".github", "workflows");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, text);
        return path;
    }

    private static string Workflow(string trigger, string jobId) =>
        trigger + ExecutableJob(jobId);

    private static string ExecutableJob(string jobId) =>
        $"jobs:\n  {jobId}:\n"
        + "    runs-on: ubuntu-latest\n"
        + "    steps:\n"
        + "      - run: echo ok\n";
}
