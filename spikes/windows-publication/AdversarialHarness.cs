using System.Text;

namespace Namespace2Xml.Spikes.WindowsPublication;

/// <summary>
/// Builds an adversarial corpus under a private working directory and exercises both defence layers:
///   Suite A — <see cref="PathValidator"/> (string / planning layer, spec §16.2/§21.1/§17.5)
///   Suite B — <see cref="SecureWriter"/> (runtime handle-relative no-follow layer, spec §21.1/§21.3)
/// Every case asserts an EXPECTED outcome so the run itself is a pass/fail test, and Suite B
/// additionally verifies that no victim file outside the root was ever created or modified.
/// </summary>
internal static class AdversarialHarness
{
    private static int _pass;
    private static int _fail;
    private static readonly List<string> _failures = new();

    public static int Run()
    {
        string workRoot = Path.Combine(AppContext.BaseDirectory, "_corpus", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workRoot);
        Console.WriteLine($"corpus working directory: {workRoot}\n");

        try
        {
            SuiteA_Validator();
            SuiteB_SecureWriter(workRoot);
        }
        finally
        {
            try
            {
                ReparseUtil.RobustDelete(workRoot);
                // Remove the _corpus container too, if this was the last run in it.
                string? parent = Path.GetDirectoryName(workRoot);
                if (parent is not null && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
                    Directory.Delete(parent, false);
            }
            catch (Exception ex) { Console.WriteLine($"[cleanup warning] {ex.Message}"); }
        }

        Console.WriteLine();
        Console.WriteLine("======================================================================");
        Console.WriteLine($" RESULT: {_pass} passed, {_fail} failed");
        if (_fail > 0)
        {
            Console.WriteLine(" FAILURES:");
            foreach (string f in _failures) Console.WriteLine($"   - {f}");
        }
        Console.WriteLine("======================================================================");
        return _fail == 0 ? 0 : 1;
    }

    // =====================================================================================
    //  Suite A — string / planning layer
    // =====================================================================================
    private static void SuiteA_Validator()
    {
        Section("SUITE A — PathValidator (string layer): raw filename -> canonical or PATH001");

        // structural rejections (§21.1)
        ExpectReject("A01 rooted '\\rooted.txt'", @"\rooted.txt");
        ExpectReject("A02 drive-absolute 'C:\\Windows\\x.txt'", @"C:\Windows\x.txt");
        ExpectReject("A03 drive-relative 'C:x'", "C:x");
        ExpectReject("A04 UNC '\\\\server\\share\\x'", @"\\server\share\x");
        ExpectReject("A05 extended-length '\\\\?\\C:\\x'", @"\\?\C:\x");
        ExpectReject("A06 device '\\\\.\\PIPE\\x'", @"\\.\PIPE\x");
        ExpectReject("A07 parent traversal '..\\escape.txt'", @"..\escape.txt");
        ExpectReject("A08 interior traversal 'a/../b'", "a/../b");
        ExpectReject("A09 explicit '.' segment 'a/./b'", "a/./b");
        ExpectReject("A10 empty segment 'a//b'", "a//b");

        // portable-encoding transformations (§16.2) — accepted but neutralised
        ExpectCanonical("A11 device name 'CON'", "CON", "%5FCON");
        ExpectCanonical("A12 device name 'nul'", "nul", "%5Fnul");
        ExpectCanonical("A13 device name 'COM1'", "COM1", "%5FCOM1");
        ExpectCanonical("A14 device name 'LPT9.txt'", "LPT9.txt", "%5FLPT9.txt");
        ExpectCanonical("A15 non-device 'COM0'", "COM0", "COM0");            // COM0 is NOT reserved
        ExpectCanonical("A16 ADS suffix 'file.txt:stream'", "file.txt:stream", "file.txt%3Astream");
        ExpectCanonical("A17 trailing dot 'name.'", "name.", "name%2E");
        ExpectCanonical("A18 trailing space 'name '", "name ", "name%20");
        ExpectCanonical("A19 non-ASCII 'résumé.txt'", "résumé.txt", "r%C3%A9sum%C3%A9.txt");
        ExpectCanonical("A20 nested subdirs 'a/b/c.txt'", "a/b/c.txt", "a/b/c.txt");
        ExpectCanonical("A21 literal percent 'a%b'", "a%b", "a%25b");

        // §17.5 portability-key case-collision detection across a destination set
        Console.WriteLine();
        var set = new[] { "Foo.txt", "bar/Baz.txt", "FOO.txt", "bar/BAZ.txt" };
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        var collisions = new List<string>();
        foreach (string name in set)
        {
            var r = PathValidator.ValidateExplicitFilename(name);
            if (!r.Ok) continue;
            if (keys.TryGetValue(r.PortabilityKey!, out string? prior) && prior != r.Canonical)
                collisions.Add($"{name} (key {r.PortabilityKey}) collides with {prior}");
            else
                keys[r.PortabilityKey!] = r.Canonical!;
        }
        Check("A22 case-collision set yields 2 PATH001 collisions",
            collisions.Count == 2, $"detected {collisions.Count}: [{string.Join("; ", collisions)}]");
    }

    // =====================================================================================
    //  Suite B — runtime handle-relative no-follow layer
    // =====================================================================================
    private static void SuiteB_SecureWriter(string workRoot)
    {
        Section("SUITE B — SecureWriter (runtime layer): clean path vs adversarial FILESYSTEM");

        string outsideRoot = Path.Combine(workRoot, "outside");
        Directory.CreateDirectory(outsideRoot);
        string victim = Path.Combine(outsideRoot, "secret.txt");
        File.WriteAllText(victim, "TOP-SECRET");

        string NewRoot()
        {
            string r = Path.Combine(workRoot, "root_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(r);
            return r;
        }
        bool VictimIntact() => File.ReadAllText(victim) == "TOP-SECRET";

        // ---- B01 happy path -------------------------------------------------------------
        {
            string root = NewRoot();
            var res = SecureWriter.Publish(root, "a/b/c.txt", U8("hello"));
            bool onDisk = File.Exists(Path.Combine(root, "a", "b", "c.txt"))
                          && File.ReadAllText(Path.Combine(root, "a", "b", "c.txt")) == "hello";
            Report("B01 happy path 'a/b/c.txt'", res);
            Check("B01 written under root with correct content", res.Ok && onDisk, res.Detail);
        }

        // ---- B02 deep path (> MAX_PATH) -------------------------------------------------
        {
            string root = NewRoot();
            var parts = Enumerable.Range(0, 34).Select(i => $"segment{i:D2}xxxx");
            string deep = string.Join('/', parts) + "/leaf.txt";
            Console.WriteLine($"      (relative length = {deep.Length} chars, full path >> 260)");
            var res = SecureWriter.Publish(root, deep, U8("deep-ok"));
            Report("B02 deep path immune to MAX_PATH", res);
            Check("B02 deep write succeeded by construction (never formed a long string)", res.Ok, res.Detail);
        }

        // ---- B03 directory JUNCTION escape (no privilege needed) -------------------------
        {
            string root = NewRoot();
            ReparseUtil.CreateJunction(Path.Combine(root, "viajunction"), outsideRoot);
            var res = SecureWriter.Publish(root, "viajunction/pwned.txt", U8("PWNED"));
            bool noEscape = !File.Exists(Path.Combine(outsideRoot, "pwned.txt")) && VictimIntact();
            Report("B03 junction 'viajunction' -> outside, dest 'viajunction/pwned.txt'", res);
            Check("B03 refused PATH001 (reparse) and nothing escaped",
                res is { Status: PublishStatus.RejectedPath001 } && noEscape, res.Detail);
        }

        // ---- B04 directory SYMLINK escape (needs Developer Mode / elevation) --------------
        {
            string root = NewRoot();
            string link = Path.Combine(root, "viasymlink");
            if (ReparseUtil.TryCreateDirectorySymlink(link, outsideRoot, out string err))
            {
                var res = SecureWriter.Publish(root, "viasymlink/pwned2.txt", U8("PWNED"));
                bool noEscape = !File.Exists(Path.Combine(outsideRoot, "pwned2.txt")) && VictimIntact();
                Report("B04 dir symlink 'viasymlink' -> outside", res);
                Check("B04 refused PATH001 (reparse) and nothing escaped",
                    res is { Status: PublishStatus.RejectedPath001 } && noEscape, res.Detail);
            }
            else
            {
                Skip("B04 directory symlink escape", $"symlink creation unavailable ({err})");
            }
        }

        // ---- B05 file SYMLINK as final component (needs privilege) ------------------------
        {
            string root = NewRoot();
            string link = Path.Combine(root, "secret_link.txt");
            if (ReparseUtil.TryCreateFileSymlink(link, victim, out string err))
            {
                var res = SecureWriter.Publish(root, "secret_link.txt", U8("PWNED"));
                Report("B05 file symlink final 'secret_link.txt' -> outside/secret.txt", res);
                Check("B05 refused PATH001 (final reparse) and victim intact",
                    res is { Status: PublishStatus.RejectedPath001 } && VictimIntact(), res.Detail);
            }
            else
            {
                Skip("B05 file symlink final component", $"symlink creation unavailable ({err})");
            }
        }

        // ---- B06 DOS device name at the NT layer (fed RAW, bypassing the validator) -------
        {
            string root = NewRoot();
            var res = SecureWriter.Publish(root, "CON", U8("not-the-console"));
            bool realFile = File.Exists(Path.Combine(root, "CON"))
                            && File.ReadAllText(Path.Combine(root, "CON")) == "not-the-console";
            Report("B06 raw 'CON' via NtCreateFile", res);
            Check("B06 NtCreateFile made a REAL file named CON (no device magic)", res.Ok && realFile, res.Detail);
        }

        // ---- B07 trailing dot at the NT layer (fed RAW) ---------------------------------
        {
            string root = NewRoot();
            var r1 = SecureWriter.Publish(root, "name", U8("plain"));
            var r2 = SecureWriter.Publish(root, "name.", U8("dotted"));   // literal trailing dot
            // Enumerate to observe the two distinct on-disk names (Win32 would collapse them).
            string[] names = Directory.GetFiles(root).Select(Path.GetFileName).OrderBy(x => x).ToArray()!;
            Report("B07 raw 'name' and 'name.' via NtCreateFile", r2);
            Check("B07 NtCreateFile preserved trailing dot as a DISTINCT file",
                r1.Ok && r2.Ok && names.Length == 2, $"on-disk names = [{string.Join(", ", names)}]");
        }

        // ---- B08 ADS suffix at the NT layer (fed RAW) — the case the WRITER does NOT stop -
        {
            string root = NewRoot();
            var res = SecureWriter.Publish(root, "host.txt:evil", U8("stream-data"));
            bool adsMade = false;
            try { adsMade = File.ReadAllText(Path.Combine(root, "host.txt") + ":evil") == "stream-data"; }
            catch { /* ignore */ }
            Report("B08 raw 'host.txt:evil' via NtCreateFile", res);
            Check("B08 NT layer created an ADS (=> colon MUST be encoded by the validator, A16)",
                res.Ok && adsMade, $"ads-created={adsMade}");
        }

        // ---- B09 HARDLINK escape (residual risk, out-of-contract per §21.3) --------------
        {
            string root = NewRoot();
            File.WriteAllText(victim, "TOP-SECRET");   // reset
            string hard = Path.Combine(root, "hard.txt");
            ReparseUtil.CreateHardLink(hard, victim);
            uint? links = ReparseUtil.TryGetHardLinkCount(hard);
            var res = SecureWriter.Publish(root, "hard.txt", U8("PWNED"));
            bool escaped = File.ReadAllText(victim) == "PWNED";
            Report("B09 hardlink 'hard.txt' == outside/secret.txt", res);
            Console.WriteLine($"      NumberOfLinks probe on 'hard.txt' before write = {links?.ToString() ?? "n/a"} " +
                              "(a >1 result is the optional defence-in-depth signal)");
            Check("B09 DEMONSTRATES residual hardlink escape (expected: not caught by no-follow)",
                res.Ok && escaped && links is > 1,
                $"escaped={escaped}, links={links?.ToString() ?? "n/a"} — documented out-of-contract §21.3");
            File.WriteAllText(victim, "TOP-SECRET");   // reset for any later reads
        }

        // ---- B10 absolute path handed to the writer (defence-in-depth) -------------------
        {
            string root = NewRoot();
            var res = SecureWriter.Publish(root, "C:/Windows/system32/x.txt", U8("x"));
            // Split by '/', components include "C:" which is not a directory under root -> refused.
            Report("B10 absolute 'C:/Windows/system32/x.txt' at writer", res);
            Check("B10 writer also refuses PATH001 (never opens outside root)",
                res is { Status: PublishStatus.RejectedPath001 }, res.Detail);
        }

        // ---- B11 JUNCTION as the FINAL component (privilege-free coverage of final-reparse) --
        // Symbolic links need privilege we may lack, but the writer's reparse check is
        // tag-agnostic (it tests FILE_ATTRIBUTE_REPARSE_POINT, not a specific tag), so a
        // junction leaf exercises the identical final-component refusal path a file symlink would.
        {
            string root = NewRoot();
            File.WriteAllText(victim, "TOP-SECRET");
            ReparseUtil.CreateJunction(Path.Combine(root, "finaljunction"), outsideRoot);
            var res = SecureWriter.Publish(root, "finaljunction", U8("PWNED"));
            Report("B11 junction as final component 'finaljunction' -> outside", res);
            Check("B11 refused PATH001 (final reparse) via privilege-free junction; victim intact",
                res is { Status: PublishStatus.RejectedPath001 } && VictimIntact(), res.Detail);
        }
    }

    // =====================================================================================
    //  helpers
    // =====================================================================================
    private static byte[] U8(string s) => Encoding.UTF8.GetBytes(s);

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine("----------------------------------------------------------------------");
        Console.WriteLine(title);
        Console.WriteLine("----------------------------------------------------------------------");
    }

    private static void Report(string label, PublishResult res) => Console.WriteLine($"  {label}\n      -> {res}");

    private static void ExpectReject(string label, string filename)
    {
        var r = PathValidator.ValidateExplicitFilename(filename);
        Console.WriteLine($"  {label}\n      -> {(r.Ok ? $"ACCEPTED {r.Canonical}" : $"{r.Code}  {r.Detail}")}");
        Check(label + " -> PATH001", !r.Ok && r.Code == "PATH001", r.Detail);
    }

    private static void ExpectCanonical(string label, string filename, string expected)
    {
        var r = PathValidator.ValidateExplicitFilename(filename);
        Console.WriteLine($"  {label}\n      -> {(r.Ok ? $"canonical '{r.Canonical}'  key '{r.PortabilityKey}'" : $"{r.Code} {r.Detail}")}");
        Check(label + $" -> '{expected}'", r.Ok && r.Canonical == expected, $"got '{r.Canonical}'");
    }

    private static void Check(string label, bool ok, string detail)
    {
        if (ok) { _pass++; Console.WriteLine($"      [PASS] {label}"); }
        else { _fail++; _failures.Add(label + " :: " + detail); Console.WriteLine($"      [FAIL] {label} :: {detail}"); }
    }

    private static void Skip(string label, string why)
    {
        Console.WriteLine($"  {label}\n      -> [SKIP] {why}");
        Console.WriteLine($"      [SKIP] {label}");
    }
}
