using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Namespace2Xml.Gatekeeper;

internal sealed record WorkflowJob(string Id, string? Name, YamlNode? Matrix);

internal sealed record WorkflowDefinition(
    string Path,
    IReadOnlyDictionary<string, WorkflowJob> Jobs,
    bool HasTrigger);

internal sealed record WorkflowSource(string FullPath, string RelativePath);

internal sealed class WorkflowCatalog
{
    private WorkflowCatalog(Dictionary<string, WorkflowDefinition> workflows)
    {
        Workflows = workflows;
    }

    internal IReadOnlyDictionary<string, WorkflowDefinition> Workflows { get; }

    internal static WorkflowCatalog Load(string repositoryRoot)
    {
        var workflowDirectory = Path.Combine(repositoryRoot, ".github", "workflows");
        if (!Directory.Exists(workflowDirectory))
        {
            throw new InvalidDataException(
                $"Workflow directory '{workflowDirectory}' does not exist.");
        }

        var sources = Directory.EnumerateFiles(workflowDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path =>
                path.EndsWith(".yml", StringComparison.Ordinal)
                || path.EndsWith(".yaml", StringComparison.Ordinal))
            .Select(path => new WorkflowSource(
                path,
                Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/')))
            .OrderBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToList();

        return Load(sources);
    }

    internal static WorkflowCatalog Load(IEnumerable<WorkflowSource> sources)
    {
        var workflows = new Dictionary<string, WorkflowDefinition>(StringComparer.Ordinal);

        foreach (var source in sources)
        {
            if (!workflows.TryAdd(source.RelativePath, Parse(source)))
            {
                throw new InvalidDataException(
                    $"Workflow path '{source.RelativePath}' occurs more than once.");
            }
        }

        if (workflows.Count == 0)
        {
            throw new InvalidDataException(
                "No .yml or .yaml workflow files were found; the catalog cannot be empty.");
        }

        return new WorkflowCatalog(workflows);
    }

    private static WorkflowDefinition Parse(WorkflowSource source)
    {
        try
        {
            var stream = new YamlStream();
            using var reader = File.OpenText(source.FullPath);
            stream.Load(reader);

            if (stream.Documents.Count != 1
                || stream.Documents[0].RootNode is not YamlMappingNode root)
            {
                throw new InvalidDataException(
                    $"Workflow '{source.RelativePath}' must contain one mapping document.");
            }

            var rootFields = Mapping(root, $"workflow '{source.RelativePath}'");
            if (!rootFields.TryGetValue("on", out var triggerNode))
            {
                throw new InvalidDataException(
                    $"Workflow '{source.RelativePath}' declares no 'on' trigger.");
            }

            ValidateTrigger(triggerNode, source.RelativePath);

            if (!rootFields.TryGetValue("jobs", out var jobsNode)
                || jobsNode is not YamlMappingNode jobsMapping)
            {
                throw new InvalidDataException(
                    $"Workflow '{source.RelativePath}' has no mapping 'jobs' catalog.");
            }

            var jobs = new Dictionary<string, WorkflowJob>(StringComparer.Ordinal);
            foreach (var jobField in Mapping(jobsMapping, $"jobs in '{source.RelativePath}'"))
            {
                if (!GateIdentityParser.IsIdentifier(jobField.Key))
                {
                    throw new InvalidDataException(
                        $"Workflow '{source.RelativePath}' has invalid job ID '{jobField.Key}'.");
                }

                if (jobField.Value is not YamlMappingNode jobMapping)
                {
                    throw new InvalidDataException(
                        $"Job '{jobField.Key}' in '{source.RelativePath}' is not a mapping.");
                }

                var fields = Mapping(
                    jobMapping,
                    $"job '{jobField.Key}' in '{source.RelativePath}'");
                ValidateExecutableJob(fields, jobField.Key, source.RelativePath);

                string? name = null;
                if (fields.TryGetValue("name", out var nameNode))
                {
                    if (nameNode is not YamlScalarNode nameScalar)
                    {
                        throw new InvalidDataException(
                            $"Job '{jobField.Key}' in '{source.RelativePath}' has a non-scalar name.");
                    }

                    name = nameScalar.Value ?? string.Empty;
                }

                YamlNode? matrix = null;
                if (fields.TryGetValue("strategy", out var strategyNode))
                {
                    if (strategyNode is not YamlMappingNode strategyMapping)
                    {
                        throw new InvalidDataException(
                            $"Job '{jobField.Key}' in '{source.RelativePath}' has a non-mapping strategy.");
                    }

                    Mapping(
                        strategyMapping,
                        $"strategy for '{jobField.Key}' in '{source.RelativePath}'")
                        .TryGetValue("matrix", out matrix);
                }

                jobs.Add(jobField.Key, new WorkflowJob(jobField.Key, name, matrix));
            }

            if (jobs.Count == 0)
            {
                throw new InvalidDataException(
                    $"Workflow '{source.RelativePath}' has an empty jobs catalog.");
            }

            return new WorkflowDefinition(source.RelativePath, jobs, HasTrigger: true);
        }
        catch (YamlException error)
        {
            throw new InvalidDataException(
                $"Workflow '{source.RelativePath}' is malformed YAML.",
                error);
        }
        catch (InvalidOperationException error)
        {
            throw new InvalidDataException(
                $"Workflow '{source.RelativePath}' is malformed YAML.",
                error);
        }
    }

    private static void ValidateTrigger(YamlNode node, string path)
    {
        switch (node)
        {
            case YamlScalarNode scalar:
                ValidateEventName(scalar, path, []);
                return;

            case YamlSequenceNode sequence:
                {
                    if (sequence.Children.Count == 0)
                    {
                        throw new InvalidDataException(
                            $"Workflow '{path}' declares an empty 'on' trigger sequence.");
                    }

                    var events = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var child in sequence.Children)
                    {
                        if (child is not YamlScalarNode eventScalar)
                        {
                            throw new InvalidDataException(
                                $"Workflow '{path}' has a non-scalar event in its 'on' trigger sequence.");
                        }

                        ValidateEventName(eventScalar, path, events);
                    }

                    return;
                }

            case YamlMappingNode mapping:
                {
                    var events = Mapping(mapping, $"'on' trigger in workflow '{path}'");
                    if (events.Count == 0)
                    {
                        throw new InvalidDataException(
                            $"Workflow '{path}' declares an empty 'on' trigger mapping.");
                    }

                    foreach (var trigger in events)
                    {
                        ValidateEventName(trigger.Key, path);
                        ValidateEventConfiguration(trigger.Key, trigger.Value, path);
                    }

                    return;
                }

            default:
                throw new InvalidDataException(
                    $"Workflow '{path}' has an unsupported 'on' trigger shape.");
        }
    }

    private static void ValidateEventName(
        YamlScalarNode scalar,
        string path,
        HashSet<string> events)
    {
        if (string.IsNullOrEmpty(scalar.Value))
        {
            throw new InvalidDataException(
                $"Workflow '{path}' declares an empty event in its 'on' trigger.");
        }

        ValidateEventName(scalar.Value, path);
        if (scalar.Value == "schedule")
        {
            throw new InvalidDataException(
                $"Workflow '{path}' must configure event 'schedule' with at least one cron expression.");
        }

        if (!events.Add(scalar.Value))
        {
            throw new InvalidDataException(
                $"Workflow '{path}' declares event '{scalar.Value}' more than once.");
        }
    }

    private static void ValidateEventName(string eventName, string path)
    {
        if (!IsSupportedEvent(eventName))
        {
            throw new InvalidDataException(
                $"Workflow '{path}' declares unsupported event '{eventName}'.");
        }
    }

    private static void ValidateEventConfiguration(
        string eventName,
        YamlNode configuration,
        string path)
    {
        if (eventName == "schedule")
        {
            ValidateSchedule(configuration, path);
            return;
        }

        if (configuration is YamlScalarNode scalar
            && (scalar.Value is null
                || (scalar.Style == ScalarStyle.Plain && scalar.Value.Length == 0)))
        {
            return;
        }

        if (configuration is not YamlMappingNode mapping)
        {
            throw new InvalidDataException(
                $"Event '{eventName}' in workflow '{path}' must have a mapping configuration or none.");
        }

        ValidateConfigurationMapping(
            mapping,
            $"configuration for event '{eventName}' in workflow '{path}'");
    }

    private static void ValidateSchedule(YamlNode configuration, string path)
    {
        if (configuration is not YamlSequenceNode { Children.Count: > 0 } schedules)
        {
            throw new InvalidDataException(
                $"Event 'schedule' in workflow '{path}' must contain at least one cron schedule.");
        }

        foreach (var schedule in schedules.Children)
        {
            if (schedule is not YamlMappingNode scheduleMapping)
            {
                throw new InvalidDataException(
                    $"Event 'schedule' in workflow '{path}' contains a non-mapping schedule.");
            }

            var fields = Mapping(
                scheduleMapping,
                $"schedule configuration in workflow '{path}'");
            if (fields.Count != 1
                || !fields.TryGetValue("cron", out var cronNode)
                || cronNode is not YamlScalarNode cron
                || string.IsNullOrWhiteSpace(cron.Value)
                || !CronExpression.IsValid(cron.Value))
            {
                throw new InvalidDataException(
                    $"Event 'schedule' in workflow '{path}' must contain only a valid five-field scalar 'cron'.");
            }
        }
    }

    private static void ValidateExecutableJob(
        Dictionary<string, YamlNode> fields,
        string jobId,
        string path)
    {
        var description = $"Job '{jobId}' in '{path}'";
        if (fields.TryGetValue("if", out var jobCondition)
            && IsStaticallyFalseCondition(jobCondition, $"{description} condition"))
        {
            throw new InvalidDataException(
                $"{description} has a statically false condition and cannot execute.");
        }

        var hasReusableWorkflow = fields.TryGetValue("uses", out var uses);
        var hasRunsOn = fields.TryGetValue("runs-on", out var runsOn);
        var hasSteps = fields.TryGetValue("steps", out var steps);

        if (hasReusableWorkflow)
        {
            ValidateNonemptyScalar(uses!, $"{description} reusable-workflow 'uses'");
            if (hasRunsOn || hasSteps)
            {
                throw new InvalidDataException(
                    $"{description} mixes reusable-workflow 'uses' with 'runs-on' or 'steps'.");
            }

            return;
        }

        if (!hasRunsOn)
        {
            throw new InvalidDataException($"{description} has no 'runs-on' execution target.");
        }

        ValidateRunsOn(runsOn!, description);
        if (!hasSteps || steps is not YamlSequenceNode { Children.Count: > 0 } stepSequence)
        {
            throw new InvalidDataException(
                $"{description} must contain a nonempty 'steps' sequence.");
        }

        var runnableSteps = 0;
        for (var index = 0; index < stepSequence.Children.Count; index++)
        {
            if (stepSequence.Children[index] is not YamlMappingNode step)
            {
                throw new InvalidDataException(
                    $"{description} step {index} is not a mapping.");
            }

            var stepFields = Mapping(step, $"{description} step {index}");
            var hasRun = stepFields.TryGetValue("run", out var run);
            var hasUses = stepFields.TryGetValue("uses", out var stepUses);
            if (hasRun == hasUses)
            {
                throw new InvalidDataException(
                    $"{description} step {index} must contain exactly one executable 'run' or 'uses'.");
            }

            ValidateNonemptyScalar(
                hasRun ? run! : stepUses!,
                $"{description} step {index} executable command");

            if (!stepFields.TryGetValue("if", out var stepCondition)
                || !IsStaticallyFalseCondition(
                    stepCondition,
                    $"{description} step {index} condition"))
            {
                runnableSteps++;
            }
        }

        if (runnableSteps == 0)
        {
            throw new InvalidDataException(
                $"{description} has no potentially executable step because every step "
                + "condition is statically false.");
        }
    }

    private static bool IsStaticallyFalseCondition(YamlNode node, string description)
    {
        if (node is not YamlScalarNode scalar)
        {
            throw new InvalidDataException($"{description} is not a scalar.");
        }

        const string stringTag = "tag:yaml.org,2002:str";
        const string booleanTag = "tag:yaml.org,2002:bool";
        var tag = scalar.Tag.IsEmpty ? null : scalar.Tag.Value;
        if (tag is not null and not stringTag and not booleanTag)
        {
            throw new InvalidDataException(
                $"{description} has unsupported explicit tag '{tag}'.");
        }

        if (tag == booleanTag
            && scalar.Style is not ScalarStyle.Plain and not ScalarStyle.Any)
        {
            throw new InvalidDataException(
                $"{description} uses non-plain style with Boolean tag '{tag}'.");
        }

        var value = scalar.Value ?? string.Empty;
        if (tag == booleanTag
            && value is not "true" and not "True" and not "TRUE"
                and not "false" and not "False" and not "FALSE")
        {
            throw new InvalidDataException(
                $"{description} value '{value}' is invalid for Boolean tag '{tag}'.");
        }

        if (value == "false")
        {
            return true;
        }

        if (tag != stringTag
            && scalar.Style is ScalarStyle.Plain or ScalarStyle.Any
            && value is "False" or "FALSE")
        {
            return true;
        }

        var condition = value.Trim();
        if (!condition.StartsWith("${{", StringComparison.Ordinal)
            || !condition.EndsWith("}}", StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(condition[3..^2].Trim(), "false", StringComparison.Ordinal);
    }

    private static void ValidateRunsOn(YamlNode node, string description)
    {
        switch (node)
        {
            case YamlScalarNode:
                ValidateNonemptyScalar(node, $"{description} 'runs-on'");
                return;

            case YamlSequenceNode { Children.Count: > 0 } sequence:
                for (var index = 0; index < sequence.Children.Count; index++)
                {
                    ValidateNonemptyScalar(
                        sequence.Children[index],
                        $"{description} 'runs-on' label {index}");
                }

                return;

            case YamlMappingNode mapping:
                {
                    var fields = Mapping(mapping, $"{description} 'runs-on'");
                    if (fields.Count == 0
                        || fields.Keys.Any(key => key is not "group" and not "labels")
                        || (!fields.ContainsKey("group") && !fields.ContainsKey("labels")))
                    {
                        throw new InvalidDataException(
                            $"{description} has an invalid 'runs-on' mapping.");
                    }

                    if (fields.TryGetValue("group", out var group))
                    {
                        ValidateNonemptyScalar(group, $"{description} 'runs-on' group");
                    }

                    if (fields.TryGetValue("labels", out var labels))
                    {
                        ValidateRunsOnLabels(labels, description);
                    }

                    return;
                }

            default:
                throw new InvalidDataException($"{description} has an invalid 'runs-on' value.");
        }
    }

    private static void ValidateRunsOnLabels(YamlNode node, string description)
    {
        if (node is YamlSequenceNode { Children.Count: > 0 } labels)
        {
            for (var index = 0; index < labels.Children.Count; index++)
            {
                ValidateNonemptyScalar(
                    labels.Children[index],
                    $"{description} 'runs-on' label {index}");
            }

            return;
        }

        ValidateNonemptyScalar(node, $"{description} 'runs-on' labels");
    }

    private static void ValidateNonemptyScalar(YamlNode node, string description)
    {
        if (node is not YamlScalarNode scalar || string.IsNullOrWhiteSpace(scalar.Value))
        {
            throw new InvalidDataException($"{description} must be a nonempty scalar.");
        }
    }

    private static void ValidateConfigurationMapping(
        YamlMappingNode mapping,
        string description)
    {
        foreach (var field in Mapping(mapping, description))
        {
            ValidateConfigurationNode(field.Value, $"{description}, field '{field.Key}'");
        }
    }

    private static void ValidateConfigurationNode(YamlNode node, string description)
    {
        switch (node)
        {
            case YamlScalarNode:
                return;

            case YamlSequenceNode sequence:
                foreach (var child in sequence.Children)
                {
                    ValidateConfigurationNode(child, description);
                }

                return;

            case YamlMappingNode mapping:
                ValidateConfigurationMapping(mapping, description);
                return;

            default:
                throw new InvalidDataException($"{description} has an unsupported YAML shape.");
        }
    }

    private static bool IsSupportedEvent(string eventName) =>
        eventName is
            "branch_protection_rule"
            or "check_run"
            or "check_suite"
            or "create"
            or "delete"
            or "deployment"
            or "deployment_status"
            or "discussion"
            or "discussion_comment"
            or "fork"
            or "gollum"
            or "issue_comment"
            or "issues"
            or "label"
            or "merge_group"
            or "milestone"
            or "page_build"
            or "project"
            or "project_card"
            or "project_column"
            or "public"
            or "pull_request"
            or "pull_request_review"
            or "pull_request_review_comment"
            or "pull_request_target"
            or "push"
            or "registry_package"
            or "release"
            or "repository_dispatch"
            or "schedule"
            or "status"
            or "watch"
            or "workflow_call"
            or "workflow_dispatch"
            or "workflow_run";

    private static class CronExpression
    {
        private static readonly string[] Months =
            ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];

        private static readonly string[] Days =
            ["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"];

        internal static bool IsValid(string expression)
        {
            var fields = expression.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return fields.Length == 5
                && IsField(fields[0], 0, 59, [])
                && IsField(fields[1], 0, 23, [])
                && IsField(fields[2], 1, 31, [])
                && IsField(fields[3], 1, 12, Months)
                && IsField(fields[4], 0, 6, Days);
        }

        private static bool IsField(
            string field,
            int minimum,
            int maximum,
            IReadOnlyList<string> names) =>
            field.Split(',').All(part => IsPart(part, minimum, maximum, names));

        private static bool IsPart(
            string part,
            int minimum,
            int maximum,
            IReadOnlyList<string> names)
        {
            var stepParts = part.Split('/');
            if (stepParts.Length > 2
                || (stepParts.Length == 2
                    && (!int.TryParse(stepParts[1], out var step) || step <= 0)))
            {
                return false;
            }

            var range = stepParts[0];
            if (range == "*")
            {
                return true;
            }

            var endpoints = range.Split('-');
            if (endpoints.Length == 1)
            {
                return TryValue(endpoints[0], minimum, maximum, names, out _);
            }

            return endpoints.Length == 2
                && TryValue(endpoints[0], minimum, maximum, names, out var start)
                && TryValue(endpoints[1], minimum, maximum, names, out var end)
                && start <= end;
        }

        private static bool TryValue(
            string text,
            int minimum,
            int maximum,
            IReadOnlyList<string> names,
            out int value)
        {
            if (int.TryParse(text, out value))
            {
                return value >= minimum && value <= maximum;
            }

            for (var index = 0; index < names.Count; index++)
            {
                if (string.Equals(text, names[index], StringComparison.OrdinalIgnoreCase))
                {
                    value = minimum + index;
                    return true;
                }
            }

            return false;
        }
    }

    private static Dictionary<string, YamlNode> Mapping(
        YamlMappingNode mapping,
        string description)
    {
        var result = new Dictionary<string, YamlNode>(StringComparer.Ordinal);

        foreach (var field in mapping.Children)
        {
            if (field.Key is not YamlScalarNode key || key.Value is null)
            {
                throw new InvalidDataException($"{description} contains a non-scalar key.");
            }

            if (!result.TryAdd(key.Value, field.Value))
            {
                throw new InvalidDataException(
                    $"{description} contains duplicate key '{key.Value}'.");
            }
        }

        return result;
    }
}
