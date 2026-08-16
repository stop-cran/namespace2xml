namespace Namespace2Xml.Conformance;

/// <summary>One conformance case directory, as laid out by specification Appendix C.</summary>
public sealed class ConformanceCase
{
    private ConformanceCase(string name, string directory)
    {
        Name = name;
        Directory = directory;
    }

    /// <summary>Case name, which is its directory name.</summary>
    public string Name { get; }

    /// <summary>Absolute path of the case directory.</summary>
    public string Directory { get; }

    /// <summary>Token vector for run A, taken verbatim from <c>args.txt</c>.</summary>
    public IReadOnlyList<string> Arguments => ArgsFile.Read(Path.Combine(Directory, "args.txt"));

    /// <summary>Expected exit code from <c>expected-exit-code.txt</c>.</summary>
    public int ExpectedExitCode
    {
        get
        {
            var raw = File.ReadAllText(Path.Combine(Directory, "expected-exit-code.txt"));

            if (!raw.EndsWith('\n') || raw.Contains('\r'))
            {
                throw new ConformanceFormatException(
                    $"{Name}: expected-exit-code.txt must be one ASCII decimal code followed by LF.");
            }

            return int.Parse(raw.TrimEnd('\n'), System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Path to <c>expected-stdout.txt</c>, which need not exist. Appendix C.5 makes its absence
    /// the assertion that standard output is empty, so the path is returned rather than its
    /// content.
    /// </summary>
    public string ExpectedStandardOutput => Path.Combine(Directory, "expected-stdout.txt");

    /// <summary>Section 26 item numbers this case exercises.</summary>
    public IReadOnlyList<int> Requirements =>
        File.ReadAllLines(Path.Combine(Directory, "requirements.txt"))
            .Where(line => line.Length > 0)
            .Select(line => int.Parse(line, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

    /// <summary>Expected output-root tree, or <see langword="null"/> when no destination may be created.</summary>
    public string? ExpectedTree
    {
        get
        {
            var path = Path.Combine(Directory, "expected");
            return System.IO.Directory.Exists(path) ? path : null;
        }
    }

    /// <summary>
    /// Raw expected diagnostic stream, or <see langword="null"/> when the case writes no stream at
    /// all. Appendix C.4 distinguishes that from an empty array, which is a written stream.
    /// </summary>
    public byte[]? ExpectedDiagnostics
    {
        get
        {
            var path = Path.Combine(Directory, "expected-diagnostics.json");
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
    }

    /// <summary>
    /// Token vector for run B, which must select the JSON encoding. Appendix C.4 forbids the
    /// harness from appending tokens when <c>args.txt</c> contains a bare <c>--</c> or an existing
    /// <c>--diagnostics-format</c>, because appending would change what the case tests.
    /// </summary>
    public IReadOnlyList<string> DiagnosticArguments
    {
        get
        {
            var explicitPath = Path.Combine(Directory, "args-diagnostics.txt");

            if (File.Exists(explicitPath))
            {
                return ArgsFile.Read(explicitPath);
            }

            var arguments = Arguments;

            foreach (var token in arguments)
            {
                if (token == "--" || token == "--diagnostics-format" ||
                    token.StartsWith("--diagnostics-format=", StringComparison.Ordinal))
                {
                    throw new ConformanceFormatException(
                        $"{Name}: args.txt contains '{token}', so the case must supply args-diagnostics.txt.");
                }
            }

            return [.. arguments, "--diagnostics-format", "json"];
        }
    }

    /// <summary>Enumerates every case under a corpus root, in ordinal name order.</summary>
    /// <exception cref="ConformanceFormatException">
    /// A corpus root that does not exist, or a case directory without <c>args.txt</c>. Silently
    /// skipping either shrinks the suite without failing it, which is indistinguishable from
    /// success.
    /// </exception>
    public static IReadOnlyList<ConformanceCase> Discover(string corpusRoot)
    {
        if (!System.IO.Directory.Exists(corpusRoot))
        {
            throw new ConformanceFormatException($"the conformance corpus root '{corpusRoot}' does not exist.");
        }

        var directories = System.IO.Directory
            .EnumerateDirectories(corpusRoot)
            .OrderBy(directory => Path.GetFileName(directory), StringComparer.Ordinal)
            .ToList();

        var malformed = directories
            .Where(directory => !File.Exists(Path.Combine(directory, "args.txt")))
            .Select(directory => Path.GetFileName(directory)!)
            .ToList();

        if (malformed.Count > 0)
        {
            throw new ConformanceFormatException(
                $"case directories without args.txt: {string.Join(", ", malformed)}. " +
                "A case that cannot be discovered is a case that cannot fail.");
        }

        return directories
            .Select(directory => new ConformanceCase(Path.GetFileName(directory)!, directory))
            .ToList();
    }

    /// <inheritdoc />
    public override string ToString() => Name;
}
