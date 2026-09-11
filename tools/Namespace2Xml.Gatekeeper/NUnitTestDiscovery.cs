using System.Diagnostics;
using System.Text.Json;

namespace Namespace2Xml.Gatekeeper;

internal sealed class NUnitTestCatalog
{
    internal NUnitTestCatalog(IEnumerable<KeyValuePair<string, IEnumerable<string>>> assemblies)
    {
        var catalog =
            new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        foreach (var assembly in assemblies)
        {
            var leaves = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var fullyQualifiedName in assembly.Value)
            {
                leaves.TryGetValue(fullyQualifiedName, out var count);
                leaves[fullyQualifiedName] = count + 1;
            }

            if (!catalog.TryAdd(assembly.Key, leaves))
            {
                throw new InvalidDataException(
                    $"NUnit assembly '{assembly.Key}' occurs more than once.");
            }

            if (catalog[assembly.Key].Count == 0)
            {
                throw new InvalidDataException(
                    $"NUnit assembly '{assembly.Key}' discovered no leaf tests.");
            }
        }

        if (catalog.Count == 0)
        {
            throw new InvalidDataException("The NUnit test catalog cannot be empty.");
        }

        Assemblies = catalog;
    }

    internal IReadOnlyDictionary<string, Dictionary<string, int>> Assemblies { get; }

    internal bool Contains(string assemblyName, string fullyQualifiedName) =>
        ResolutionCount(assemblyName, fullyQualifiedName) == 1;

    internal int ResolutionCount(string assemblyName, string fullyQualifiedName) =>
        Assemblies.TryGetValue(assemblyName, out var tests)
        && tests.TryGetValue(fullyQualifiedName, out var count)
            ? count
            : 0;
}

internal static class NUnitListTestsParser
{
    private const string Header = "The following Tests are available:";

    internal static IReadOnlyList<string> Parse(string output, string? diagnosticPath = null)
    {
        var lines = output.ReplaceLineEndings("\n").Split('\n');
        var headerIndexes = lines
            .Select((line, index) => new { Line = line, Index = index })
            .Where(entry => string.Equals(entry.Line.Trim(), Header, StringComparison.Ordinal))
            .Select(entry => entry.Index)
            .ToList();

        if (headerIndexes.Count != 1)
        {
            throw new InvalidDataException(
                $"Pinned test-adapter output must contain exactly one '{Header}' header.");
        }

        var tests = new List<string>();

        foreach (var line in lines.Skip(headerIndexes[0] + 1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (diagnosticPath is not null
                && string.Equals(
                    line,
                    "Logging Vstest Diagnostics in file: " + diagnosticPath,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (!char.IsWhiteSpace(line[0]))
            {
                throw new InvalidDataException(
                    $"Unrecognized pinned test-adapter list line '{line}'.");
            }

            var test = line.Trim();
            tests.Add(test);
        }

        if (tests.Count == 0)
        {
            throw new InvalidDataException(
                "Pinned test-adapter output contains no leaf tests.");
        }

        return tests;
    }
}

internal static class NUnitDiscoveryProtocolParser
{
    private const string Marker =
        "TestRequestSender.OnDiscoveryMessageReceived: Received message: ";

    internal static IReadOnlyList<string> Parse(
        IEnumerable<string> diagnosticLines,
        string expectedSource)
    {
        var tests = new List<string>();
        var completed = 0;
        int? reportedTotal = null;

        foreach (var line in diagnosticLines)
        {
            var marker = line.IndexOf(Marker, StringComparison.Ordinal);
            if (marker < 0)
            {
                continue;
            }

            var json = line[(marker + Marker.Length)..];
            using var message = JsonDocument.Parse(json);
            if (message.RootElement.GetProperty("Version").GetInt32() != 7)
            {
                throw new InvalidDataException(
                    "Pinned test-adapter discovery used an unrecognized protocol version.");
            }

            var messageType = message.RootElement.GetProperty("MessageType").GetString();
            switch (messageType)
            {
                case "TestDiscovery.TestFound":
                    AddTests(message.RootElement.GetProperty("Payload"), expectedSource, tests);
                    break;

                case "TestDiscovery.Completed":
                    completed++;
                    var payload = message.RootElement.GetProperty("Payload");
                    reportedTotal = payload.GetProperty("TotalTests").GetInt32();
                    var last = payload.GetProperty("LastDiscoveredTests");
                    if (last.ValueKind == JsonValueKind.Array)
                    {
                        AddTests(last, expectedSource, tests);
                    }

                    break;
            }
        }

        if (completed != 1 || reportedTotal is null)
        {
            throw new InvalidDataException(
                "Pinned test-adapter discovery did not report exactly one completed catalog.");
        }

        if (reportedTotal.Value != tests.Count || tests.Count == 0)
        {
            throw new InvalidDataException(
                $"Pinned test-adapter discovery reported {reportedTotal.Value} tests but supplied "
                + $"{tests.Count} fully-qualified leaves.");
        }

        return tests;
    }

    private static void AddTests(
        JsonElement payload,
        string expectedSource,
        List<string> tests)
    {
        if (payload.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "Pinned test-adapter discovery supplied a non-array test batch.");
        }

        foreach (var test in payload.EnumerateArray())
        {
            var source = test.GetProperty("Source").GetString();
            if (!string.Equals(source, expectedSource, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Pinned test-adapter discovery returned source '{source}' while discovering "
                    + $"'{expectedSource}'.");
            }

            var fullyQualifiedName = test.GetProperty("FullyQualifiedName").GetString();
            if (string.IsNullOrWhiteSpace(fullyQualifiedName))
            {
                throw new InvalidDataException(
                    "Pinned test-adapter discovery returned a blank fully-qualified test name.");
            }

            tests.Add(fullyQualifiedName);
        }
    }
}

internal static class NUnitTestDiscovery
{
    private static readonly TestProject[] Projects =
    [
        new(
            Path.Combine("tests", "Namespace2Xml.UnitTests", "Namespace2Xml.UnitTests.csproj"),
            "Namespace2Xml.UnitTests"),
        new(
            Path.Combine(
                "tests",
                "Namespace2Xml.Conformance",
                "Namespace2Xml.Conformance.csproj"),
            "Namespace2Xml.Conformance"),
    ];

    internal static NUnitTestCatalog Discover(string repositoryRoot, string executingAssemblyPath)
    {
        var frameworkDirectory = Directory.GetParent(executingAssemblyPath)
            ?? throw new InvalidDataException(
                $"Cannot infer the target framework from '{executingAssemblyPath}'.");
        var configurationDirectory = frameworkDirectory.Parent
            ?? throw new InvalidDataException(
                $"Cannot infer the build configuration from '{executingAssemblyPath}'.");

        var framework = frameworkDirectory.Name;
        var configuration = configurationDirectory.Name;
        var discovered = new List<KeyValuePair<string, IEnumerable<string>>>();

        foreach (var project in Projects)
        {
            var projectPath = Path.Combine(repositoryRoot, project.RelativePath);
            var binaryPath = Path.Combine(
                Path.GetDirectoryName(projectPath)!,
                "bin",
                configuration,
                framework,
                project.AssemblyName + ".dll");

            if (!File.Exists(binaryPath))
            {
                throw new InvalidDataException(
                    $"Built test assembly '{binaryPath}' is missing. Build the solution before "
                    + "running gate discovery.");
            }

            var tests = ListTests(
                repositoryRoot,
                projectPath,
                binaryPath,
                configuration,
                framework,
                project.AssemblyName);
            discovered.Add(new KeyValuePair<string, IEnumerable<string>>(
                project.AssemblyName,
                tests));
        }

        return new NUnitTestCatalog(discovered);
    }

    private static IReadOnlyList<string> ListTests(
        string repositoryRoot,
        string projectPath,
        string binaryPath,
        string configuration,
        string framework,
        string assemblyName)
    {
        var diagnosticPath = Path.Combine(
            Path.GetDirectoryName(binaryPath)!,
            "gatekeeper-discovery-" + Guid.NewGuid().ToString("N") + ".log");
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in new[]
                 {
                     "test",
                     projectPath,
                     "--configuration",
                     configuration,
                     "--framework",
                     framework,
                     "--no-build",
                     "--no-restore",
                     "--list-tests",
                     "--nologo",
                     "--verbosity",
                     "quiet",
                     "--diag:" + diagnosticPath,
                 })
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
        start.Environment["VSLANG"] = "1033";

        try
        {
            using var process = Process.Start(start)
                ?? throw new InvalidDataException(
                    $"Failed to start pinned test-adapter discovery for '{assemblyName}'.");
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            Task.WaitAll(standardOutput, standardError);

            if (process.ExitCode != 0)
            {
                throw new InvalidDataException(
                    $"Pinned test-adapter discovery for '{assemblyName}' exited {process.ExitCode}."
                    + Environment.NewLine
                    + standardError.Result
                    + standardOutput.Result);
            }

            var displayNames = NUnitListTestsParser.Parse(standardOutput.Result, diagnosticPath);
            var fullyQualifiedNames = NUnitDiscoveryProtocolParser.Parse(
                File.ReadLines(diagnosticPath),
                binaryPath);
            if (displayNames.Count != fullyQualifiedNames.Count)
            {
                throw new InvalidDataException(
                    $"Pinned test-adapter discovery for '{assemblyName}' listed "
                    + $"{displayNames.Count} display names but returned "
                    + $"{fullyQualifiedNames.Count} fully-qualified leaves.");
            }

            return fullyQualifiedNames;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                $"Pinned test-adapter discovery for '{assemblyName}' returned malformed protocol JSON.",
                error);
        }
        finally
        {
            File.Delete(diagnosticPath);
        }
    }

    private sealed record TestProject(string RelativePath, string AssemblyName);
}
