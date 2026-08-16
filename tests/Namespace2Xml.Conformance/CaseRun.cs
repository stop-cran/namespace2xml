namespace Namespace2Xml.Conformance;

/// <summary>
/// A fresh working copy of a conformance case, deleted when the run finishes. Both the corpus
/// harness and the Appendix C.6 differential harness run out of one of these, so a case cannot be
/// observed differently depending on which binary is under test.
/// </summary>
internal sealed class CaseRun : IDisposable
{
    internal CaseRun(ConformanceCase conformanceCase)
    {
        Directory = Path.Combine(Path.GetTempPath(), "n2x-case-" + Guid.NewGuid().ToString("n"));
        CopyDirectory(conformanceCase.Directory, Directory);
    }

    internal string Directory { get; }

    /// <summary>Hashes every fixture-owned entry so a mutation can be detected after the run.</summary>
    internal Dictionary<string, string> FixtureSnapshot(HashSet<string> reserved)
    {
        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in System.IO.Directory.EnumerateFileSystemEntries(Directory))
        {
            if (!reserved.Contains(Path.GetFileName(entry)))
            {
                continue;
            }

            if (System.IO.Directory.Exists(entry))
            {
                foreach (var file in System.IO.Directory.EnumerateFiles(entry, "*", SearchOption.AllDirectories))
                {
                    snapshot[Relative(file)] = Hash(file);
                }

                foreach (var child in System.IO.Directory.EnumerateDirectories(entry, "*", SearchOption.AllDirectories))
                {
                    snapshot[Relative(child) + "/"] = string.Empty;
                }
            }
            else
            {
                snapshot[Relative(entry)] = Hash(entry);
            }
        }

        return snapshot;
    }

    private string Relative(string path) =>
        Path.GetRelativePath(Directory, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string Hash(string path) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

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

        // Directories are created explicitly so that an empty one survives the copy. An empty
        // directory the tool created is a created destination and must be visible to the
        // comparer.
        foreach (var directory in System.IO.Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            System.IO.Directory.CreateDirectory(
                Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in System.IO.Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
