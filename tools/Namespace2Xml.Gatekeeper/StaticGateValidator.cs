namespace Namespace2Xml.Gatekeeper;

internal static class StaticGateValidator
{
    internal static void ValidateRepository(string repositoryRoot, string executingAssemblyPath)
    {
        var assertions = AssertionGateCatalog.Load(
            Path.Combine(repositoryRoot, "conformance", "assertions.json"));
        var tests = NUnitTestDiscovery.Discover(repositoryRoot, executingAssemblyPath);
        var workflows = WorkflowCatalog.Load(repositoryRoot);

        Validate(assertions.References, tests, workflows);
    }

    internal static void Validate(
        IEnumerable<AssertionGateReference> references,
        NUnitTestCatalog tests,
        WorkflowCatalog workflows)
    {
        var failures = new List<string>();

        foreach (var reference in references)
        {
            switch (reference.Identity)
            {
                case NUnitGateIdentity nunit:
                    var resolutions = tests.ResolutionCount(
                        nunit.AssemblyName,
                        nunit.FullyQualifiedName);
                    if (resolutions != 1)
                    {
                        failures.Add(
                            $"item {reference.Item} names NUnit leaf "
                            + $"'{GateIdentityRenderer.Render(nunit)}', which resolves "
                            + $"{resolutions} times");
                    }

                    break;

                case CiGateIdentity ci:
                    ValidateCi(reference.Item, ci, workflows, failures);
                    break;

                default:
                    failures.Add($"item {reference.Item} has an unknown gate identity type");
                    break;
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidDataException(
                "Static gate validation failed:"
                + Environment.NewLine
                + string.Join(
                    Environment.NewLine,
                    failures.Order(StringComparer.Ordinal).Select(failure => " - " + failure)));
        }
    }

    private static void ValidateCi(
        int item,
        CiGateIdentity identity,
        WorkflowCatalog catalog,
        List<string> failures)
    {
        if (!catalog.Workflows.TryGetValue(identity.WorkflowPath, out var workflow))
        {
            failures.Add(
                $"item {item} names missing workflow '{identity.WorkflowPath}'");
            return;
        }

        if (!workflow.Jobs.TryGetValue(identity.JobId, out var job))
        {
            failures.Add(
                $"item {item} names missing job '{identity.JobId}' in '{identity.WorkflowPath}'");
            return;
        }

        IReadOnlyList<MatrixCell>? cells = null;
        try
        {
            if (job.Matrix is not null)
            {
                cells = MatrixExpander.Expand(job.Matrix);
            }
        }
        catch (InvalidDataException error)
        {
            failures.Add(
                $"item {item} names job '{identity.JobId}' whose matrix cannot be resolved: "
                + error.Message);
            return;
        }

        if (identity.MatrixCell is not null)
        {
            if (cells is null)
            {
                failures.Add(
                    $"item {item} names a matrix cell for non-matrix job "
                    + $"'{identity.WorkflowPath}::{identity.JobId}'");
            }
            else
            {
                var matches = cells.Count(cell => cell.Equals(identity.MatrixCell));
                if (matches != 1)
                {
                    failures.Add(
                        $"item {item} names matrix cell "
                        + $"'{GateIdentityRenderer.Render(identity)}', which matches {matches} "
                        + "complete expanded cells");
                }
            }
        }

        string expectedName;
        try
        {
            expectedName = ExpectedJobName(identity.WorkflowPath, identity.JobId, cells);
        }
        catch (InvalidDataException error)
        {
            failures.Add(
                $"item {item} names job '{identity.JobId}' whose runtime name cannot be canonical: "
                + error.Message);
            return;
        }

        if (!string.Equals(job.Name, expectedName, StringComparison.Ordinal))
        {
            failures.Add(
                $"item {item} names '{identity.WorkflowPath}::{identity.JobId}', whose YAML name "
                + $"must be exactly '{expectedName}' but is '{job.Name ?? "<missing>"}'");
        }
    }

    internal static string ExpectedJobName(
        string workflowPath,
        string jobId,
        IReadOnlyList<MatrixCell>? cells)
    {
        if (cells is null)
        {
            return GateIdentityRenderer.Render(
                new CiGateIdentity(workflowPath, jobId, MatrixCell: null));
        }

        if (cells.Count == 0)
        {
            throw new InvalidDataException("A gated matrix has no expanded cells.");
        }

        var keys = cells[0].Dimensions.Select(dimension => dimension.Key).ToList();
        foreach (var cell in cells.Skip(1))
        {
            if (!keys.SequenceEqual(
                    cell.Dimensions.Select(dimension => dimension.Key),
                    StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    "expanded cells do not share one complete dimension-key set");
            }
        }

        var dimensions = string.Join(
            ",",
            keys.Select(key => key + "=${{ toJSON(matrix." + key + ") }}"));
        return $"ci:{workflowPath}::{jobId}[{dimensions}]";
    }
}
