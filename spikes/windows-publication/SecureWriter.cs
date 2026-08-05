using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using static Namespace2Xml.Spikes.WindowsPublication.NativeMethods;

namespace Namespace2Xml.Spikes.WindowsPublication;

public enum PublishStatus
{
    Written,
    RejectedPath001, // invalid / escaping / insecure / uncontainable destination (spec §21.1, §22 PATH001)
    FailedPath002    // open / create / write / flush / close failure after publication started (PATH002)
}

/// <summary>Outcome of a single secure publication attempt.</summary>
public sealed record PublishResult(PublishStatus Status, string? Code, string Detail, string? RealizedPath = null)
{
    public bool Ok => Status == PublishStatus.Written;
    public override string ToString() =>
        Status == PublishStatus.Written
            ? $"WRITTEN  -> {RealizedPath}"
            : $"{Code}  {Detail}";
}

/// <summary>
/// TOCTOU-safe secure publication for Windows, implemented with the NT native API.
///
/// Design (mirrors POSIX <c>openat(dirfd, comp, O_NOFOLLOW)</c>):
///   1. Open the output root once, by absolute NT path, to obtain the trust-anchor
///      directory handle. The root is user-configured and trusted, so following any
///      reparse points *in the root path itself* is acceptable.
///   2. Walk the relative destination ONE component at a time. Each component is opened
///      *relative to the handle of its already-verified parent* via
///      <c>OBJECT_ATTRIBUTES.RootDirectory = parentHandle</c> and
///      <c>ObjectName = "&lt;bareComponent&gt;"</c> (no leading backslash).
///   3. Every open passes <c>FILE_OPEN_REPARSE_POINT</c>, so the kernel never follows a
///      reparse point during that single atomic resolution. After opening an intermediate
///      directory we query its reparse tag; if it is a reparse point we refuse (PATH001).
///   4. The final component is created/truncated with <c>FILE_OVERWRITE_IF</c> +
///      <c>FILE_OPEN_REPARSE_POINT</c>. Even if it is (or becomes) a symlink, we replace the
///      link object rather than writing through it, so the link target is never touched.
///
/// Why this is TOCTOU-safe *by construction*: every step is anchored to a HANDLE, not a
/// re-parsed path string. Once we hold a handle to real directory D, opening "c" relative
/// to D resolves "c" *inside the object D references*; renaming D's own entry afterwards
/// cannot redirect us, because a handle is a stable reference to a file object, not a name.
/// No full path string is ever handed to the API for re-resolution, so there is no window
/// in which an attacker can substitute a component we have already validated.
/// </summary>
public static unsafe class SecureWriter
{
    public static PublishResult Publish(string outputRoot, string canonicalRelativePath, byte[] content)
    {
        // Defence-in-depth: the planning-stage validator (PathValidator) already guarantees a
        // clean canonical relative path, but the writer re-checks the cheap invariants so it is
        // safe to call in isolation.
        string[] components = canonicalRelativePath.Split('/', StringSplitOptions.None);
        if (components.Length == 0 || Array.Exists(components, c => c.Length == 0))
            return new PublishResult(PublishStatus.RejectedPath001, "PATH001",
                $"empty or redundant path segment in '{canonicalRelativePath}'");
        if (Array.Exists(components, c => c is "." or ".."))
            return new PublishResult(PublishStatus.RejectedPath001, "PATH001",
                $"dot/dot-dot segment reached the writer in '{canonicalRelativePath}'");

        IntPtr rootHandle = IntPtr.Zero;
        var openHandles = new List<IntPtr>();
        try
        {
            // ---- 1. Open the output root (trust anchor) ----------------------------------
            string ntRoot = @"\??\" + Path.GetFullPath(outputRoot).TrimEnd('\\');
            int rootStatus = AbsoluteOpen(ntRoot,
                DIR_ACCESS, FILE_SHARE_ALL, FILE_OPEN,
                FILE_DIRECTORY_FILE | FILE_SYNCHRONOUS_IO_NONALERT,
                out rootHandle);
            if (rootStatus != STATUS_SUCCESS)
            {
                // An existing non-directory root is PATH001 per §21.1; a genuinely missing root
                // would have been created by the plan, so treat unexpected failure as PATH001
                // (uncontainable root) rather than a post-publication I/O fault.
                if ((uint)rootStatus == STATUS_NOT_A_DIRECTORY)
                    return new PublishResult(PublishStatus.RejectedPath001, "PATH001",
                        $"output root is not a directory: {DescribeStatus(rootStatus)}");
                return new PublishResult(PublishStatus.RejectedPath001, "PATH001",
                    $"cannot open output root '{outputRoot}': {DescribeStatus(rootStatus)}");
            }

            IntPtr parent = rootHandle;

            // ---- 2. Walk & create intermediate directories, refusing reparse points ------
            for (int i = 0; i < components.Length - 1; i++)
            {
                int st = RelativeOpen(parent, components[i],
                    DIR_ACCESS, FILE_ATTRIBUTE_NORMAL, FILE_SHARE_ALL, FILE_OPEN_IF,
                    FILE_DIRECTORY_FILE | FILE_OPEN_REPARSE_POINT | FILE_SYNCHRONOUS_IO_NONALERT,
                    out IntPtr child);

                if (st != STATUS_SUCCESS)
                {
                    if ((uint)st == STATUS_NOT_A_DIRECTORY)
                        return new PublishResult(PublishStatus.RejectedPath001, "PATH001",
                            $"component '{components[i]}' exists but is not a directory: {DescribeStatus(st)}");
                    if (IsInvalidName((uint)st))
                        return new PublishResult(PublishStatus.RejectedPath001, "PATH001",
                            $"invalid path component '{components[i]}': {DescribeStatus(st)}");
                    return new PublishResult(PublishStatus.FailedPath002, "PATH002",
                        $"opening/creating directory '{components[i]}': {DescribeStatus(st)}");
                }

                openHandles.Add(child);

                // Reparse containment check: we must never traverse INTO a reparse point.
                if (IsReparsePoint(child, out uint tag))
                    return new PublishResult(PublishStatus.RejectedPath001, "PATH001",
                        $"reparse point in path at component '{components[i]}' " +
                        $"(tag 0x{tag:X8} = {DescribeTag(tag)}); refusing to traverse");

                parent = child;
            }

            // ---- 3. Probe the final component no-follow, for a clean PATH001 report -------
            string leaf = components[^1];
            int probe = RelativeOpen(parent, leaf,
                FILE_READ_ATTRIBUTES | SYNCHRONIZE, 0, FILE_SHARE_ALL, FILE_OPEN,
                FILE_OPEN_REPARSE_POINT | FILE_SYNCHRONOUS_IO_NONALERT,
                out IntPtr probeHandle);
            if (probe == STATUS_SUCCESS)
            {
                openHandles.Add(probeHandle);
                if (IsReparsePoint(probeHandle, out uint ltag))
                    return new PublishResult(PublishStatus.RejectedPath001, "PATH001",
                        $"final component '{leaf}' is a reparse point " +
                        $"(tag 0x{ltag:X8} = {DescribeTag(ltag)}); refusing to publish through it");
            }
            // STATUS_OBJECT_NAME_NOT_FOUND / PATH_NOT_FOUND simply means "does not exist yet".

            // ---- 4. Create or truncate the destination (no-follow) and write -------------
            // FILE_OPEN_REPARSE_POINT makes this race-immune even if a symlink is planted
            // between the probe and here: we would replace the link, not follow it.
            int fst = RelativeOpen(parent, leaf,
                FILE_ACCESS, FILE_ATTRIBUTE_NORMAL, FILE_SHARE_READ | FILE_SHARE_DELETE, FILE_OVERWRITE_IF,
                FILE_NON_DIRECTORY_FILE | FILE_OPEN_REPARSE_POINT | FILE_SYNCHRONOUS_IO_NONALERT,
                out IntPtr fileHandle);

            if (fst != STATUS_SUCCESS)
            {
                if ((uint)fst == STATUS_FILE_IS_A_DIRECTORY)
                    return new PublishResult(PublishStatus.FailedPath002, "PATH002",
                        $"destination '{leaf}' exists as a directory: {DescribeStatus(fst)}");
                if (IsInvalidName((uint)fst))
                    return new PublishResult(PublishStatus.RejectedPath001, "PATH001",
                        $"invalid destination name '{leaf}': {DescribeStatus(fst)}");
                return new PublishResult(PublishStatus.FailedPath002, "PATH002",
                    $"opening destination '{leaf}': {DescribeStatus(fst)}");
            }

            // ---- 5. Hand the kernel handle to ordinary managed I/O ----------------------
            string? realized;
            try
            {
                var safe = new SafeFileHandle(fileHandle, ownsHandle: true);
                realized = TryGetFinalPath(safe); // display only; NOT a security decision
                using (var fs = new FileStream(safe, FileAccess.Write))
                {
                    fs.Write(content, 0, content.Length);
                    fs.Flush();
                }
            }
            catch (Exception ex)
            {
                return new PublishResult(PublishStatus.FailedPath002, "PATH002",
                    $"write/flush failed for '{leaf}': {ex.GetType().Name}: {ex.Message}");
            }

            return new PublishResult(PublishStatus.Written, null, "ok", realized);
        }
        finally
        {
            foreach (IntPtr h in openHandles)
                if (h != IntPtr.Zero) CloseHandle(h);
            if (rootHandle != IntPtr.Zero) CloseHandle(rootHandle);
        }
    }

    // ---------------------------------------------------------------------------------------

    /// <summary>Open a bare <paramref name="name"/> relative to <paramref name="parent"/>'s handle.</summary>
    private static int RelativeOpen(
        IntPtr parent, string name, uint access, uint fileAttributes, uint share,
        uint disposition, uint options, out IntPtr handle)
    {
        fixed (char* p = name)
        {
            var us = new UNICODE_STRING
            {
                Length = (ushort)(name.Length * sizeof(char)),
                MaximumLength = (ushort)(name.Length * sizeof(char)),
                Buffer = (IntPtr)p
            };
            UNICODE_STRING* pus = &us;
            var oa = new OBJECT_ATTRIBUTES
            {
                Length = sizeof(OBJECT_ATTRIBUTES),
                RootDirectory = parent,          // <-- the crux: relative to a HANDLE
                ObjectName = (IntPtr)pus,         // <-- bare component, NO leading backslash
                Attributes = OBJ_CASE_INSENSITIVE,
                SecurityDescriptor = IntPtr.Zero,
                SecurityQualityOfService = IntPtr.Zero
            };
            return NtCreateFile(out handle, access, in oa, out _, IntPtr.Zero,
                fileAttributes, share, disposition, options, IntPtr.Zero, 0);
        }
    }

    /// <summary>Open an absolute NT path (RootDirectory = NULL). Used only for the root anchor.</summary>
    private static int AbsoluteOpen(
        string ntPath, uint access, uint share, uint disposition, uint options, out IntPtr handle)
    {
        fixed (char* p = ntPath)
        {
            var us = new UNICODE_STRING
            {
                Length = (ushort)(ntPath.Length * sizeof(char)),
                MaximumLength = (ushort)(ntPath.Length * sizeof(char)),
                Buffer = (IntPtr)p
            };
            UNICODE_STRING* pus = &us;
            var oa = new OBJECT_ATTRIBUTES
            {
                Length = sizeof(OBJECT_ATTRIBUTES),
                RootDirectory = IntPtr.Zero,
                ObjectName = (IntPtr)pus,
                Attributes = OBJ_CASE_INSENSITIVE,
                SecurityDescriptor = IntPtr.Zero,
                SecurityQualityOfService = IntPtr.Zero
            };
            return NtCreateFile(out handle, access, in oa, out _, IntPtr.Zero,
                FILE_ATTRIBUTE_NORMAL, share, disposition, options, IntPtr.Zero, 0);
        }
    }

    private static bool IsReparsePoint(IntPtr handle, out uint reparseTag)
    {
        FILE_ATTRIBUTE_TAG_INFORMATION info = default;
        int st = NtQueryInformationFile(handle, out _, (IntPtr)(&info),
            (uint)sizeof(FILE_ATTRIBUTE_TAG_INFORMATION), FileAttributeTagInformation);
        if (st != STATUS_SUCCESS)
        {
            reparseTag = 0;
            return false; // query failed => treat as non-reparse; the no-follow open already protected us
        }
        reparseTag = info.ReparseTag;
        return (info.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0;
    }

    private static string DescribeTag(uint tag) => tag switch
    {
        IO_REPARSE_TAG_MOUNT_POINT => "junction",
        IO_REPARSE_TAG_SYMLINK => "symlink",
        _ => "reparse"
    };

    private static string? TryGetFinalPath(SafeFileHandle handle)
    {
        var buf = new char[1024];
        fixed (char* p = buf)
        {
            uint n = GetFinalPathNameByHandleW(handle.DangerousGetHandle(), p, (uint)buf.Length, 0);
            if (n == 0 || n >= buf.Length) return null;
            string s = new string(buf, 0, (int)n);
            return s.StartsWith(@"\\?\", StringComparison.Ordinal) ? s.Substring(4) : s;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetFinalPathNameByHandleW")]
    private static extern uint GetFinalPathNameByHandleW(IntPtr hFile, char* lpszFilePath, uint cchFilePath, uint dwFlags);
}
