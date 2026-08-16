using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Namespace2Xml.Output;

/// <summary>
/// POSIX implementation of Section 21.1 handle-relative, no-follow publication opens.
/// </summary>
internal sealed partial class PosixSecureDirectory : ISecureDirectory
{
    private const int ExistingDirectoryMode = 0;
    private const int CreatedDirectoryMode = 0x1FF;
    private const int CreatedFileMode = 0x1B6;
    private const int OpenReadOnly = 0;
    private const int OpenWriteOnly = 1;

    private readonly SafeFileHandle handle;

    private PosixSecureDirectory(SafeFileHandle handle)
    {
        this.handle = handle;
    }

    /// <summary>
    /// Opens the configured output root as the POSIX trust anchor.
    /// </summary>
    /// <param name="outputRootPath">The configured output root path.</param>
    /// <returns>The opened root directory.</returns>
    internal static PosixSecureDirectory OpenRoot(string outputRootPath)
    {
        var fd = NativeMethods.open(
            Path.GetFullPath(outputRootPath),
            OpenDirectory | OpenCloseOnExec,
            ExistingDirectoryMode);

        if (fd < 0)
        {
            ThrowLastIo("opening the output root");
        }

        return new PosixSecureDirectory(new SafeFileHandle((IntPtr)fd, ownsHandle: true));
    }

    /// <inheritdoc/>
    public ISecureDirectory OpenOrCreateChildDirectory(string component)
    {
        ValidateComponent(component);

        var fd = OpenChildDirectory(component);

        if (fd < 0 && Marshal.GetLastPInvokeError() == Errno.NoEntry)
        {
            var created = NativeMethods.mkdirat(DirectoryFd, component, CreatedDirectoryMode);

            if (created != 0 && Marshal.GetLastPInvokeError() != Errno.Exists)
            {
                ThrowLastIo($"creating directory '{component}'");
            }

            fd = OpenChildDirectory(component);
        }

        if (fd < 0)
        {
            ThrowOpenFailure(component, directory: true);
        }

        return new PosixSecureDirectory(new SafeFileHandle((IntPtr)fd, ownsHandle: true));
    }

    /// <inheritdoc/>
    public Stream CreateOrTruncateChildFile(string component)
    {
        ValidateComponent(component);

        var fd = NativeMethods.openat(
            DirectoryFd,
            component,
            OpenWriteOnly
                | OpenCreate
                | OpenTruncate
                | OpenNoFollow
                | OpenCloseOnExec,
            CreatedFileMode);

        if (fd < 0)
        {
            ThrowOpenFailure(component, directory: false);
        }

        // openat is variadic in C, and the Apple arm64 calling convention passes variadic
        // arguments on the stack while a fixed-arity P/Invoke passes them in registers, so the
        // creation mode can arrive as an arbitrary value there. A file created with a mode that
        // denies its own owner would fail the next read rather than this write, which makes the
        // defect surface far from its cause. Setting the mode explicitly through a non-variadic
        // call removes the question, and gives destinations the same permissions on every
        // platform -- which is the answer this tool should prefer anyway.
        if (NativeMethods.fchmod(fd, CreatedFileMode) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            NativeMethods.close(fd);

            throw new IOException(
                $"setting permissions on '{component}' failed: "
                + new System.ComponentModel.Win32Exception(error).Message);
        }

        return new FileStream(new SafeFileHandle((IntPtr)fd, ownsHandle: true), FileAccess.Write);
    }

    /// <inheritdoc/>
    public void Dispose() => handle.Dispose();

    private int DirectoryFd => (int)handle.DangerousGetHandle();

    private static int DirectoryOpenFlags =>
        OpenReadOnly | OpenDirectory | OpenNoFollow | OpenCloseOnExec;

    private static int OpenCreate => OperatingSystem.IsMacOS() ? 0x200 : 0x40;

    private static int OpenTruncate => OperatingSystem.IsMacOS() ? 0x400 : 0x200;

    private static int OpenDirectory => OperatingSystem.IsMacOS() ? 0x100000 : 0x10000;

    private static int OpenNoFollow => OperatingSystem.IsMacOS() ? 0x100 : 0x20000;

    private static int OpenCloseOnExec => OperatingSystem.IsMacOS() ? 0x1000000 : 0x80000;

    private static void ValidateComponent(string component)
    {
        ArgumentException.ThrowIfNullOrEmpty(component);

        if (component.Contains('/', StringComparison.Ordinal) || component is "." or "..")
        {
            throw new UncontainableDestinationException(
                $"'{component}' is not a single canonical destination component.");
        }
    }

    private int OpenChildDirectory(string component) =>
        NativeMethods.openat(DirectoryFd, component, DirectoryOpenFlags, ExistingDirectoryMode);

    private void ThrowOpenFailure(string component, bool directory)
    {
        var error = Marshal.GetLastPInvokeError();

        if (error == Errno.TooManySymbolicLinks || IsSymbolicLink(component))
        {
            throw new UncontainableDestinationException(
                $"the {(directory ? "directory component" : "final component")} '{component}' "
                + "is a symbolic link, and Section 21.1 requires opening destinations without "
                + "following symbolic links.");
        }

        throw new IOException(
            $"opening '{component}' failed: {new System.ComponentModel.Win32Exception(error).Message}");
    }

    private bool IsSymbolicLink(string component)
    {
        var buffer = Marshal.AllocHGlobal(1);

        try
        {
            var result = NativeMethods.readlinkat(DirectoryFd, component, buffer, 1);
            return result >= 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void ThrowLastIo(string operation)
    {
        var error = Marshal.GetLastPInvokeError();
        throw new IOException($"{operation} failed: {new System.ComponentModel.Win32Exception(error).Message}");
    }

    private static class Errno
    {
        internal const int Exists = 17;
        internal const int NoEntry = 2;
        internal static int TooManySymbolicLinks => OperatingSystem.IsMacOS() ? 62 : 40;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int open(string path, int flags, int mode);

        [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int openat(int directoryFd, string path, int flags, int mode);

        [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int mkdirat(int directoryFd, string path, int mode);

        [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial nint readlinkat(int directoryFd, string path, IntPtr buffer, nuint bufferSize);

        [LibraryImport("libc", SetLastError = true)]
        internal static partial int fchmod(int fd, int mode);

        [LibraryImport("libc", SetLastError = true)]
        internal static partial int close(int fd);
    }
}
