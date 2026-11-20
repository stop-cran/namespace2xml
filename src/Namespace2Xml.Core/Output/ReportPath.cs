using System.Buffers;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Namespace2Xml.Output;

internal sealed partial class ReportPath
{
    private const string DefaultStream = "::$DATA";
    private readonly Dictionary<string, string> resolved = new(StringComparer.Ordinal);

    internal static string Normalize(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return OperatingSystem.IsWindows() && full.EndsWith(DefaultStream, StringComparison.OrdinalIgnoreCase)
            ? full[..^DefaultStream.Length]
            : full;
    }

    internal static string ResolveParent(string path) =>
        TryResolve(path) ?? throw new DirectoryNotFoundException("the report parent does not exist.");

    internal string Resolve(string path)
    {
        if (resolved.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var suffix = new Stack<string>();
        var current = path;
        string? prefix;
        while (!resolved.TryGetValue(current, out prefix) && (prefix = TryResolve(current)) is null)
        {
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent))
            {
                prefix = current;
                break;
            }

            suffix.Push(Path.GetFileName(current));
            current = parent;
        }

        return resolved[path] = suffix.Aggregate(prefix!, Path.Combine);
    }

    private static string? TryResolve(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return TryResolveWindows(path);
        }

        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw new UncontainableDestinationException("this host cannot securely open the report path.");
        }

        var pointer = NativeMethods.realpath(path, IntPtr.Zero);
        if (pointer == IntPtr.Zero)
        {
            var error = Marshal.GetLastPInvokeError();
            return error is 2 or 20 // ENOENT or ENOTDIR
                ? null
                : throw new IOException("resolving a report comparison path failed.", new Win32Exception(error));
        }

        try
        {
            return Marshal.PtrToStringUTF8(pointer)!;
        }
        finally
        {
            NativeMethods.free(pointer);
        }
    }

    private static string? TryResolveWindows(string path)
    {
        // Resolve the underlying file, but retain named streams as distinct paths.
        var streamIndex = path.IndexOf(':', Path.GetPathRoot(path)!.Length);
        var basePath = streamIndex < 0 ? path : path[..streamIndex];
        using var handle = NativeMethods.CreateFileW(
            basePath, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero); // OPEN_EXISTING, BACKUP_SEMANTICS
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            return error is 2 or 3 or 123 or 267 // missing, invalid name, or non-directory prefix
                ? null
                : throw new IOException("resolving a report comparison path failed.", new Win32Exception(error));
        }

        var result = GetFinalWindowsPath(handle);
        return streamIndex < 0 ? result : result + path[streamIndex..];
    }

    private static unsafe string GetFinalWindowsPath(SafeFileHandle handle)
    {
        const string UncPrefix = @"\\?\UNC\";
        const string ExtendedPrefix = @"\\?\";
        var buffer = ArrayPool<char>.Shared.Rent(32768);
        try
        {
            fixed (char* pointer = buffer)
            {
                var length = NativeMethods.GetFinalPathNameByHandleW(handle, pointer, (uint)buffer.Length, 0);
                if (length == 0 || length >= buffer.Length)
                {
                    throw new IOException("resolving a report comparison handle failed.",
                        new Win32Exception(Marshal.GetLastPInvokeError()));
                }

                var path = new string(pointer, 0, (int)length);
                return path.StartsWith(UncPrefix, StringComparison.Ordinal)
                    ? @"\\" + path[UncPrefix.Length..]
                    : path.StartsWith(ExtendedPrefix, StringComparison.Ordinal) ? path[ExtendedPrefix.Length..] : path;
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private static partial class NativeMethods
    {
        [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial IntPtr realpath(string path, IntPtr destination);

        [LibraryImport("libc")]
        internal static partial void free(IntPtr pointer);

        [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        internal static partial SafeFileHandle CreateFileW(
            string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        internal static unsafe partial uint GetFinalPathNameByHandleW(
            SafeFileHandle handle, char* path, uint length, uint flags);
    }
}
