using System.Collections.Immutable;
using Namespace2Xml.Cli;

namespace Namespace2Xml.Output;

internal static class ReportFile
{
    internal static Stream Prepare(CommandLine command, ImmutableArray<PlannedOutput> outputs)
    {
        var (resolvedParent, leaf) = Validate(command, outputs);
        var root = Path.GetPathRoot(resolvedParent)!;
        var components = Parts(resolvedParent[root.Length..]);
        using var anchor = OpenRoot(root);
        var opened = new List<ISecureReportDirectory>();
        try
        {
            var current = anchor;
            foreach (var component in components)
            {
                current = current.OpenExistingChildDirectory(component, "21.5");
                opened.Add(current);
            }

            return current.CreateOrTruncateReportFile(leaf);
        }
        finally
        {
            for (var index = opened.Count - 1; index >= 0; index--)
            {
                opened[index].Dispose();
            }
        }
    }

    internal static (string Parent, string Leaf) Validate(CommandLine command, ImmutableArray<PlannedOutput> outputs)
    {
        var path = ReportPath.Normalize(command.ReportFile!);
        var parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent))
        {
            throw new UncontainableDestinationException("the report path must name a file.");
        }

        var outputRoot = ReportPath.Normalize(command.OutputRoot);
        var sources = command.Inputs.Concat(command.Schemes).Select(ReportPath.Normalize).ToArray();
        var destinations = outputs.Select(output => ReportPath.Normalize(Path.Combine(
            outputRoot, output.Path.Canonical.Replace('/', Path.DirectorySeparatorChar)))).ToArray();
        CheckCollisions(path, outputRoot, sources, destinations);

        var resolvedParent = ReportPath.ResolveParent(parent);
        var comparison = new ReportPath();
        var leaf = Path.GetFileName(path);
        CheckCollisions(comparison.Resolve(Path.Combine(resolvedParent, leaf)), comparison.Resolve(outputRoot),
            sources.Select(comparison.Resolve), destinations.Select(comparison.Resolve));
        return (resolvedParent, leaf);
    }

    private static void CheckCollisions(
        string path, string outputRoot, IEnumerable<string> sources, IEnumerable<string> destinations)
    {
        if (IsSameOrAncestor(path, outputRoot))
        {
            throw new UncontainableDestinationException("the report conflicts with the output root.");
        }

        foreach (var source in sources)
        {
            if (SamePath(path, source))
            {
                throw new UncontainableDestinationException("the report conflicts with an input or scheme file.");
            }
        }

        foreach (var destination in destinations)
        {
            if (IsSameOrAncestor(path, destination) || IsSameOrAncestor(destination, path))
            {
                throw new UncontainableDestinationException("the report conflicts with a configuration destination.");
            }
        }
    }

    private static ISecureReportDirectory OpenRoot(string root)
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsSecureDirectory.OpenReportRoot(root);
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return PosixSecureDirectory.OpenRoot(root);
        }

        throw new UncontainableDestinationException("this host cannot securely open the report path.");
    }

    private static bool SamePath(string first, string second) =>
        IsSameOrAncestor(first, second) && IsSameOrAncestor(second, first);

    private static bool IsSameOrAncestor(string first, string second)
    {
        var a = Parts(first);
        var b = Parts(second);
        if (a.Length > b.Length)
        {
            return false;
        }

        var hostComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var hostMatch = true;
        var asciiMatch = true;
        for (var index = 0; index < a.Length; index++)
        {
            hostMatch &= string.Equals(a[index], b[index], hostComparison);
            asciiMatch &= string.Equals(AsciiFold(a[index]), AsciiFold(b[index]), StringComparison.Ordinal);
        }

        return hostMatch || asciiMatch;
    }

    private static string[] Parts(string path) =>
        path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

    private static string AsciiFold(string text) => string.Create(text.Length, text, static (target, source) =>
    {
        for (var index = 0; index < source.Length; index++)
        {
            var value = source[index];
            target[index] = value is >= 'A' and <= 'Z' ? (char)(value + ('a' - 'A')) : value;
        }
    });
}
