using System.Collections.Immutable;
using Namespace2Xml.Cli;

namespace Namespace2Xml.Output;

internal static class ReportFile
{
    internal static Stream Prepare(CommandLine command, ImmutableArray<PlannedOutput> outputs)
    {
        var path = Path.GetFullPath(command.ReportFile!);
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root) || Path.TrimEndingDirectorySeparator(path) == Path.TrimEndingDirectorySeparator(root))
        {
            throw new UncontainableDestinationException("the report path must name a file.");
        }

        var outputRoot = Path.GetFullPath(command.OutputRoot);
        if (IsSameOrAncestor(path, outputRoot))
        {
            throw new UncontainableDestinationException("the report conflicts with the output root.");
        }

        foreach (var source in command.Inputs.Concat(command.Schemes))
        {
            if (SamePath(path, Path.GetFullPath(source)))
            {
                throw new UncontainableDestinationException("the report conflicts with an input or scheme file.");
            }
        }

        foreach (var output in outputs)
        {
            var destination = Path.GetFullPath(Path.Combine(
                outputRoot, output.Path.Canonical.Replace('/', Path.DirectorySeparatorChar)));
            if (IsSameOrAncestor(path, destination) || IsSameOrAncestor(destination, path))
            {
                throw new UncontainableDestinationException("the report conflicts with a configuration destination.");
            }
        }

        var components = path[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        if (components.Length == 0)
        {
            throw new UncontainableDestinationException("the report path must name a file.");
        }

        using var anchor = OpenRoot(root);
        var opened = new List<ISecureReportDirectory>();
        try
        {
            var current = anchor;
            foreach (var component in components[..^1])
            {
                current = current.OpenExistingChildDirectory(component);
                opened.Add(current);
            }

            return current.CreateOrTruncateChildFile(components[^1]);
        }
        finally
        {
            for (var index = opened.Count - 1; index >= 0; index--)
            {
                opened[index].Dispose();
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
