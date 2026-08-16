using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Namespace2Xml.Output;

/// <summary>
/// Windows implementation of Section 21.1 handle-relative, no-follow publication opens.
/// </summary>
internal sealed partial class WindowsSecureDirectory : ISecureDirectory
{
    private readonly SafeFileHandle handle;

    private WindowsSecureDirectory(SafeFileHandle handle)
    {
        this.handle = handle;
    }

    /// <summary>
    /// Gets whether this Windows host provides Section 21.1 secure containment primitives.
    /// </summary>
    internal static bool SupportsSecureContainment => true;

    /// <summary>
    /// Opens the configured output root as the Windows trust anchor.
    /// </summary>
    /// <param name="outputRootPath">The configured output root path.</param>
    /// <returns>The opened root directory.</returns>
    internal static WindowsSecureDirectory OpenRoot(string outputRootPath)
    {
        var full = Path.GetFullPath(outputRootPath);
        var ntRoot = @"\??\" + Path.TrimEndingDirectorySeparator(full);
        var root = OpenAbsolute(
            ntRoot,
            NativeMethods.DirectoryAccess,
            NativeMethods.FileShareAll,
            NativeMethods.FileOpen,
            NativeMethods.FileDirectoryFile | NativeMethods.FileSynchronousIoNonalert);

        return new WindowsSecureDirectory(root);
    }

    /// <inheritdoc/>
    public ISecureDirectory OpenOrCreateChildDirectory(string component)
    {
        ValidateComponent(component);

        var child = OpenRelative(
            component,
            NativeMethods.DirectoryAccess,
            NativeMethods.FileAttributeNormal,
            NativeMethods.FileShareAll,
            NativeMethods.FileOpenIf,
            NativeMethods.FileDirectoryFile
                | NativeMethods.FileOpenReparsePoint
                | NativeMethods.FileSynchronousIoNonalert);

        try
        {
            RefuseReparse(child, component, finalComponent: false);
            return new WindowsSecureDirectory(child);
        }
        catch
        {
            child.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public Stream CreateOrTruncateChildFile(string component)
    {
        ValidateComponent(component);
        RefuseExistingReparseLeaf(component);

        var file = OpenRelative(
            component,
            NativeMethods.FileAccess,
            NativeMethods.FileAttributeNormal,
            NativeMethods.FileShareRead | NativeMethods.FileShareDelete,
            NativeMethods.FileOverwriteIf,
            NativeMethods.FileNonDirectoryFile
                | NativeMethods.FileOpenReparsePoint
                | NativeMethods.FileSynchronousIoNonalert);

        return new FileStream(file, FileAccess.Write);
    }

    /// <inheritdoc/>
    public void Dispose() => handle.Dispose();

    private static void ValidateComponent(string component)
    {
        ArgumentException.ThrowIfNullOrEmpty(component);

        if (component.Contains('\\', StringComparison.Ordinal)
            || component.Contains('/', StringComparison.Ordinal)
            || component is "." or "..")
        {
            throw new UncontainableDestinationException(
                $"'{component}' is not a single canonical destination component.");
        }
    }

    private void RefuseExistingReparseLeaf(string component)
    {
        SafeFileHandle? probe = null;

        try
        {
            probe = OpenRelative(
                component,
                NativeMethods.FileReadAttributes | NativeMethods.Synchronize,
                NativeMethods.FileAttributeNormal,
                NativeMethods.FileShareAll,
                NativeMethods.FileOpen,
                NativeMethods.FileOpenReparsePoint | NativeMethods.FileSynchronousIoNonalert);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        using (probe)
        {
            RefuseReparse(probe, component, finalComponent: true);
        }
    }

    private static void RefuseReparse(SafeFileHandle target, string component, bool finalComponent)
    {
        var info = QueryAttributes(target);

        if ((info.FileAttributes & NativeMethods.FileAttributeReparsePoint) == 0)
        {
            return;
        }

        var place = finalComponent ? "final component" : "directory component";

        throw new UncontainableDestinationException(
            $"the {place} '{component}' is a reparse point, and Section 21.1 requires "
            + "opening destinations without following reparse points.");
    }

    private static NativeMethods.FileAttributeTagInformation QueryAttributes(SafeFileHandle target)
    {
        var length = Marshal.SizeOf<NativeMethods.FileAttributeTagInformation>();
        var buffer = Marshal.AllocHGlobal(length);

        try
        {
            var status = NativeMethods.NtQueryInformationFile(
                target.DangerousGetHandle(),
                out _,
                buffer,
                (uint)length,
                NativeMethods.FileAttributeTagInformationClass);

            ThrowIfFailed(status, "querying file attributes");
            return Marshal.PtrToStructure<NativeMethods.FileAttributeTagInformation>(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private SafeFileHandle OpenRelative(
        string component,
        uint access,
        uint fileAttributes,
        uint share,
        uint disposition,
        uint options) =>
        Open(component, handle.DangerousGetHandle(), access, fileAttributes, share, disposition, options);

    private static SafeFileHandle OpenAbsolute(
        string ntPath,
        uint access,
        uint share,
        uint disposition,
        uint options) =>
        Open(ntPath, IntPtr.Zero, access, NativeMethods.FileAttributeNormal, share, disposition, options);

    private static SafeFileHandle Open(
        string name,
        IntPtr rootDirectory,
        uint access,
        uint fileAttributes,
        uint share,
        uint disposition,
        uint options)
    {
        var nameBuffer = Marshal.StringToHGlobalUni(name);
        var unicodeBuffer = IntPtr.Zero;

        try
        {
            var unicode = new NativeMethods.UnicodeString
            {
                Length = checked((ushort)(name.Length * sizeof(char))),
                MaximumLength = checked((ushort)(name.Length * sizeof(char))),
                Buffer = nameBuffer,
            };

            unicodeBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.UnicodeString>());
            Marshal.StructureToPtr(unicode, unicodeBuffer, fDeleteOld: false);

            var attributes = new NativeMethods.ObjectAttributes
            {
                Length = Marshal.SizeOf<NativeMethods.ObjectAttributes>(),
                RootDirectory = rootDirectory,
                ObjectName = unicodeBuffer,
                Attributes = NativeMethods.ObjCaseInsensitive,
                SecurityDescriptor = IntPtr.Zero,
                SecurityQualityOfService = IntPtr.Zero,
            };

            var status = NativeMethods.NtCreateFile(
                out var raw,
                access,
                in attributes,
                out _,
                IntPtr.Zero,
                fileAttributes,
                share,
                disposition,
                options,
                IntPtr.Zero,
                0);

            ThrowIfFailed(status, $"opening '{name}'");
            return new SafeFileHandle(raw, ownsHandle: true);
        }
        finally
        {
            if (unicodeBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(unicodeBuffer);
            }

            Marshal.FreeHGlobal(nameBuffer);
        }
    }

    private static void ThrowIfFailed(int status, string operation)
    {
        if (status == NativeMethods.StatusSuccess)
        {
            return;
        }

        var unsigned = unchecked((uint)status);

        if (unsigned is NativeMethods.StatusReparsePointEncountered
            or NativeMethods.StatusObjectNameInvalid
            or NativeMethods.StatusObjectPathInvalid
            or NativeMethods.StatusObjectPathSyntaxBad)
        {
            throw new UncontainableDestinationException($"{operation} failed: {DescribeStatus(status)}");
        }

        var error = NativeMethods.RtlNtStatusToDosError(status);
        var exception = new Win32Exception((int)error);

        if (unsigned is NativeMethods.StatusObjectNameNotFound or NativeMethods.StatusObjectPathNotFound)
        {
            throw new FileNotFoundException($"{operation} failed: {DescribeStatus(status)}", exception);
        }

        if (unsigned == NativeMethods.StatusNotADirectory)
        {
            throw new DirectoryNotFoundException($"{operation} failed: {DescribeStatus(status)}", exception);
        }

        throw new IOException($"{operation} failed: {DescribeStatus(status)}", exception);
    }

    private static string DescribeStatus(int status)
    {
        var error = NativeMethods.RtlNtStatusToDosError(status);
        return $"0x{(uint)status:X8} ({new Win32Exception((int)error).Message})";
    }

    private static partial class NativeMethods
    {
        internal const int StatusSuccess = 0;
        internal const uint StatusObjectNameNotFound = 0xC0000034;
        internal const uint StatusObjectPathNotFound = 0xC000003A;
        internal const uint StatusNotADirectory = 0xC0000103;
        internal const uint StatusReparsePointEncountered = 0xC000050B;
        internal const uint StatusObjectNameInvalid = 0xC0000033;
        internal const uint StatusObjectPathInvalid = 0xC0000039;
        internal const uint StatusObjectPathSyntaxBad = 0xC000003B;

        internal const uint Synchronize = 0x00100000;
        internal const uint ReadControl = 0x00020000;
        internal const uint FileReadAttributes = 0x0080;
        internal const uint FileListDirectory = 0x0001;
        internal const uint FileTraverse = 0x0020;
        internal const uint FileAddFile = 0x0002;
        internal const uint FileAddSubdirectory = 0x0004;
        internal const uint GenericWrite = 0x40000000;

        internal const uint DirectoryAccess =
            FileListDirectory | FileTraverse | FileAddFile | FileAddSubdirectory
            | FileReadAttributes | ReadControl | Synchronize;

        internal const uint FileAccess = GenericWrite | Synchronize;

        internal const uint FileShareRead = 0x00000001;
        internal const uint FileShareWrite = 0x00000002;
        internal const uint FileShareDelete = 0x00000004;
        internal const uint FileShareAll = FileShareRead | FileShareWrite | FileShareDelete;

        internal const uint FileOpen = 0x00000001;
        internal const uint FileOpenIf = 0x00000003;
        internal const uint FileOverwriteIf = 0x00000005;

        internal const uint FileDirectoryFile = 0x00000001;
        internal const uint FileSynchronousIoNonalert = 0x00000020;
        internal const uint FileNonDirectoryFile = 0x00000040;
        internal const uint FileOpenReparsePoint = 0x00200000;

        internal const uint FileAttributeNormal = 0x00000080;
        internal const uint FileAttributeReparsePoint = 0x00000400;

        internal const uint ObjCaseInsensitive = 0x00000040;

        internal const int FileAttributeTagInformationClass = 35;

        [StructLayout(LayoutKind.Sequential)]
        internal struct UnicodeString
        {
            internal ushort Length;
            internal ushort MaximumLength;
            internal IntPtr Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ObjectAttributes
        {
            internal int Length;
            internal IntPtr RootDirectory;
            internal IntPtr ObjectName;
            internal uint Attributes;
            internal IntPtr SecurityDescriptor;
            internal IntPtr SecurityQualityOfService;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct IoStatusBlock
        {
            internal IntPtr StatusPointer;
            internal UIntPtr Information;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct FileAttributeTagInformation
        {
            internal uint FileAttributes;
            internal uint ReparseTag;
        }

        [LibraryImport("ntdll.dll")]
        internal static partial int NtCreateFile(
            out IntPtr fileHandle,
            uint desiredAccess,
            in ObjectAttributes objectAttributes,
            out IoStatusBlock ioStatusBlock,
            IntPtr allocationSize,
            uint fileAttributes,
            uint shareAccess,
            uint createDisposition,
            uint createOptions,
            IntPtr eaBuffer,
            uint eaLength);

        [LibraryImport("ntdll.dll")]
        internal static partial int NtQueryInformationFile(
            IntPtr fileHandle,
            out IoStatusBlock ioStatusBlock,
            IntPtr fileInformation,
            uint length,
            int fileInformationClass);

        [LibraryImport("ntdll.dll")]
        internal static partial uint RtlNtStatusToDosError(int status);
    }
}
