using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Namespace2Xml.Output;

/// <summary>
/// POSIX implementation of Section 21.1 handle-relative, no-follow publication opens.
/// </summary>
internal sealed partial class PosixSecureDirectory : ISecureReportDirectory
{
    private const int CreatedDirectoryMode = 0x1FF;
    private const UnixFileMode CreatedFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const int OpenReadOnly = 0;
    private const int OpenWriteOnly = 1;

    private readonly SafeFileHandle handle;

    private PosixSecureDirectory(SafeFileHandle handle)
    {
        this.handle = handle;
    }

    private FileStream CreateNewChildFileWithExclusiveOpen(string component)
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("exclusive managed creation is used only on Darwin arm64.");
        }

        // A fixed-arity P/Invoke cannot pass openat's variadic mode correctly on Darwin arm64.
        // Use the runtime's native open shim for exclusive creation through the held directory
        // descriptor; O_EXCL prevents following a leaf symlink, and the kernel applies the
        // restrictive mode together with the process umask and any inherited ACL mask.
        var path = Path.Combine(
            "/dev/fd",
            DirectoryFd.ToString(System.Globalization.CultureInfo.InvariantCulture),
            component);

        try
        {
            return new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.ReadWrite | FileShare.Delete,
                UnixCreateMode = CreatedFileMode,
            });
        }
        catch (IOException exception)
        {
            if (IsSymbolicLink(component))
            {
                throw new UncontainableDestinationException(
                    $"the final component '{component}' is a symbolic link, and Section 21.1 requires "
                    + "opening destinations without following symbolic links.",
                    exception);
            }

            throw;
        }
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
            OpenDirectory | OpenCloseOnExec);

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

    public ISecureReportDirectory OpenExistingChildDirectory(string component)
    {
        ValidateComponent(component);
        var fd = OpenChildDirectory(component);
        if (fd < 0)
        {
            ThrowOpenFailure(component, directory: true);
        }

        return new PosixSecureDirectory(new SafeFileHandle((IntPtr)fd, ownsHandle: true));
    }

    public Stream? OpenExistingChildFile(string component)
    {
        ValidateComponent(component);
        var fd = NativeMethods.openat(
            DirectoryFd, component, OpenReadOnly | OpenNoFollow | OpenCloseOnExec | OpenNonBlock);
        if (fd < 0)
        {
            if (Marshal.GetLastPInvokeError() == Errno.NoEntry)
            {
                return null;
            }

            ThrowOpenFailure(component, directory: false);
        }

        var stream = new FileStream(new SafeFileHandle((IntPtr)fd, ownsHandle: true), FileAccess.Read);
        if (!stream.CanSeek)
        {
            stream.Dispose();
            throw new IOException($"the destination '{component}' is not a seekable file.");
        }

        return stream;
    }

    /// <inheritdoc/>
    public Stream CreateOrTruncateChildFile(string component)
    {
        ValidateComponent(component);

        var existingFlags = OpenWriteOnly | OpenTruncate | OpenNoFollow | OpenCloseOnExec;
        var fd = NativeMethods.openat(DirectoryFd, component, existingFlags);
        if (fd >= 0)
        {
            return new FileStream(new SafeFileHandle((IntPtr)fd, ownsHandle: true), FileAccess.Write);
        }

        if (Marshal.GetLastPInvokeError() != Errno.NoEntry)
        {
            ThrowOpenFailure(component, directory: false);
        }

        if (UsesManagedCreateFor(OperatingSystem.IsMacOS(), RuntimeInformation.ProcessArchitecture))
        {
            return CreateNewChildFileWithExclusiveOpen(component);
        }

        fd = NativeMethods.openatWithMode(
            DirectoryFd,
            component,
            OpenWriteOnly
                | OpenCreate
                | OpenTruncate
                | OpenNoFollow
                | OpenCloseOnExec,
            (int)CreatedFileMode);

        if (fd < 0)
        {
            ThrowOpenFailure(component, directory: false);
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

    private static int OpenDirectory => NoFollowFlags.Directory;

    private static int OpenNoFollow => NoFollowFlags.NoFollow;

    private static int OpenCloseOnExec => OperatingSystem.IsMacOS() ? 0x1000000 : 0x80000;

    private static int OpenNonBlock => OperatingSystem.IsMacOS() ? 0x4 : 0x800;

    internal static bool UsesManagedCreateFor(bool macOS, Architecture architecture) =>
        macOS && architecture == Architecture.Arm64;

    private static (int Directory, int NoFollow) NoFollowFlags { get; } =
        NoFollowFlagsFor(OperatingSystem.IsMacOS(), RuntimeInformation.ProcessArchitecture);

    /// <summary>
    /// The <c>O_DIRECTORY</c> and <c>O_NOFOLLOW</c> bits for one host.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Linux does not give these two flags the same numbers on every processor architecture. Most
    /// take <c>include/uapi/asm-generic/fcntl.h</c>, where <c>O_DIRECTORY</c> is <c>00200000</c>
    /// and <c>O_NOFOLLOW</c> is <c>00400000</c>; arm, arm64 and powerpc override both in their own
    /// <c>arch/&lt;arch&gt;/include/uapi/asm/fcntl.h</c> with <c>040000</c> and <c>0100000</c>.
    /// </para>
    /// <para>
    /// Sending the generic numbers to an arm64 kernel therefore asks for <c>O_DIRECT</c> and
    /// <c>O_LARGEFILE</c> instead. Opening the output root fails with <c>EINVAL</c> and the
    /// invocation cannot publish anything; and had it not failed, the destination open would have
    /// requested <c>O_LARGEFILE</c> where it meant <c>O_NOFOLLOW</c>, leaving the containment
    /// Section 21.1 requires unasked for. macOS uses one set of numbers on both of its
    /// architectures. Every other platform is refused by
    /// <see cref="SecureRootFactory.SupportsSecureContainment"/> before reaching this type.
    /// </para>
    /// </remarks>
    /// <param name="macOS">Whether the host is macOS rather than Linux.</param>
    /// <param name="architecture">The process architecture.</param>
    /// <returns>The two flag values this host's kernel expects.</returns>
    internal static (int Directory, int NoFollow) NoFollowFlagsFor(bool macOS, Architecture architecture)
    {
        if (macOS)
        {
            return (0x100000, 0x100);
        }

        return architecture is Architecture.Arm
            or Architecture.Armv6
            or Architecture.Arm64
            or Architecture.Ppc64le
            ? (0x4000, 0x8000)
            : (0x10000, 0x20000);
    }

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
        NativeMethods.openat(DirectoryFd, component, DirectoryOpenFlags);

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
        internal static partial int open(string path, int flags);

        [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int openat(int directoryFd, string path, int flags);

        [LibraryImport(
            "libc",
            EntryPoint = "openat",
            SetLastError = true,
            StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int openatWithMode(int directoryFd, string path, int flags, int mode);

        [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int mkdirat(int directoryFd, string path, int mode);

        [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial nint readlinkat(int directoryFd, string path, IntPtr buffer, nuint bufferSize);

    }
}
