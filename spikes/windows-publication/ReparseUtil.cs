using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using static Namespace2Xml.Spikes.WindowsPublication.NativeMethods;

namespace Namespace2Xml.Spikes.WindowsPublication;

/// <summary>
/// Helpers that BUILD the adversarial filesystem corpus (they are not part of the secure writer).
/// Directory junctions are created with FSCTL_SET_REPARSE_POINT and require no elevation; symbolic
/// links require Developer Mode or elevation and are created via the managed API so we can detect and
/// report their unavailability instead of silently skipping.
/// </summary>
internal static unsafe class ReparseUtil
{
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    /// <summary>Create a directory junction (IO_REPARSE_TAG_MOUNT_POINT) at <paramref name="linkDir"/> → <paramref name="targetDir"/>.</summary>
    public static void CreateJunction(string linkDir, string targetDir)
    {
        Directory.CreateDirectory(linkDir);
        string fullTarget = Path.GetFullPath(targetDir).TrimEnd('\\');
        string subst = @"\??\" + fullTarget;
        string print = fullTarget;

        byte[] substB = Encoding.Unicode.GetBytes(subst);
        byte[] printB = Encoding.Unicode.GetBytes(print);
        ushort substLen = (ushort)substB.Length;
        ushort printLen = (ushort)printB.Length;

        int pathBufferLen = substLen + 2 + printLen + 2;         // each name NUL-terminated
        ushort reparseDataLen = (ushort)(8 + pathBufferLen);      // 4 USHORT name fields + path buffer
        int total = 8 + reparseDataLen;                           // tag(4)+len(2)+reserved(2) + data

        byte[] buf = new byte[total];
        int o = 0;
        BitConverter.GetBytes(IO_REPARSE_TAG_MOUNT_POINT).CopyTo(buf, o); o += 4;
        BitConverter.GetBytes(reparseDataLen).CopyTo(buf, o); o += 2;
        BitConverter.GetBytes((ushort)0).CopyTo(buf, o); o += 2;              // Reserved
        BitConverter.GetBytes((ushort)0).CopyTo(buf, o); o += 2;              // SubstituteNameOffset
        BitConverter.GetBytes(substLen).CopyTo(buf, o); o += 2;              // SubstituteNameLength
        BitConverter.GetBytes((ushort)(substLen + 2)).CopyTo(buf, o); o += 2; // PrintNameOffset
        BitConverter.GetBytes(printLen).CopyTo(buf, o); o += 2;              // PrintNameLength
        substB.CopyTo(buf, o); o += substLen; buf[o] = 0; buf[o + 1] = 0; o += 2;
        printB.CopyTo(buf, o); o += printLen; buf[o] = 0; buf[o + 1] = 0; o += 2;

        IntPtr h = CreateFileW(linkDir, GENERIC_WRITE_W32, 0, IntPtr.Zero, OPEN_EXISTING,
            FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (h == INVALID_HANDLE_VALUE)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"open junction dir '{linkDir}'");
        try
        {
            IntPtr p = Marshal.AllocHGlobal(total);
            try
            {
                Marshal.Copy(buf, 0, p, total);
                if (!DeviceIoControl(h, FSCTL_SET_REPARSE_POINT, p, (uint)total, IntPtr.Zero, 0, out _, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "FSCTL_SET_REPARSE_POINT");
            }
            finally { Marshal.FreeHGlobal(p); }
        }
        finally { CloseHandle(h); }
    }

    /// <summary>Try to create a directory symlink; returns false + reason when privilege is missing.</summary>
    public static bool TryCreateDirectorySymlink(string link, string target, out string error)
    {
        try { Directory.CreateSymbolicLink(link, target); error = ""; return true; }
        catch (Exception ex) { error = $"{ex.GetType().Name}: {ex.Message}"; return false; }
    }

    /// <summary>Try to create a file symlink; returns false + reason when privilege is missing.</summary>
    public static bool TryCreateFileSymlink(string link, string target, out string error)
    {
        try { File.CreateSymbolicLink(link, target); error = ""; return true; }
        catch (Exception ex) { error = $"{ex.GetType().Name}: {ex.Message}"; return false; }
    }

    public static void CreateHardLink(string link, string existingFile)
    {
        if (!CreateHardLinkW(link, existingFile, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"hardlink '{link}' -> '{existingFile}'");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_STANDARD_INFORMATION
    {
        public long AllocationSize;
        public long EndOfFile;
        public uint NumberOfLinks;
        public byte DeletePending;
        public byte Directory;
    }

    private const int FileStandardInformation = 5;

    /// <summary>Best-effort probe of the hard-link count for a path (defence-in-depth demonstration).</summary>
    public static uint? TryGetHardLinkCount(string path)
    {
        IntPtr h = CreateFileW(path, FILE_READ_ATTRIBUTES, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING,
            FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
        if (h == INVALID_HANDLE_VALUE) return null;
        try
        {
            FILE_STANDARD_INFORMATION info = default;
            int st = NtQueryInformationFile(h, out _, (IntPtr)(&info),
                (uint)sizeof(FILE_STANDARD_INFORMATION), FileStandardInformation);
            return st == STATUS_SUCCESS ? info.NumberOfLinks : (uint?)null;
        }
        finally { CloseHandle(h); }
    }

    /// <summary>Recursively delete a tree WITHOUT ever traversing into a reparse point (removes the link itself).</summary>
    public static void RobustDelete(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path)) return;
        var di = new DirectoryInfo(path);
        if (!di.Exists)
        {
            DeleteFileRobust(path);
            return;
        }
        foreach (var entry in di.EnumerateFileSystemInfos())
        {
            // Path.Combine (not the FileSystemInfo path helpers) keeps the raw on-disk leaf name,
            // including a trailing dot/space, so the extended-length delete targets it exactly.
            string childFull = Path.Combine(di.FullName, entry.Name);
            bool isReparse = (entry.Attributes & FileAttributes.ReparsePoint) != 0;
            if (isReparse)
            {
                if ((entry.Attributes & FileAttributes.Directory) != 0)
                    RemoveDirRobust(childFull);  // remove junction/dir-symlink, not its target
                else
                    DeleteFileRobust(childFull); // remove file symlink, not its target
            }
            else if ((entry.Attributes & FileAttributes.Directory) != 0)
            {
                RobustDelete(childFull);
            }
            else
            {
                DeleteFileRobust(childFull);
            }
        }
        RemoveDirRobust(di.FullName);
    }

    private static string Ext(string p) =>
        p.StartsWith(@"\\?\", StringComparison.Ordinal) ? p : @"\\?\" + p;

    private static void DeleteFileRobust(string full)
    {
        if (DeleteFileW(Ext(full))) return;
        try { File.SetAttributes(full, FileAttributes.Normal); } catch { /* best effort */ }
        if (!DeleteFileW(Ext(full)))
            try { File.Delete(full); } catch { /* leave to caller's warning */ }
    }

    private static void RemoveDirRobust(string full)
    {
        if (RemoveDirectoryW(Ext(full))) return;
        try { Directory.Delete(full, false); } catch { /* leave to caller's warning */ }
    }
}
