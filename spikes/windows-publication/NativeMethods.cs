using System.Runtime.InteropServices;

namespace Namespace2Xml.Spikes.WindowsPublication;

/// <summary>
/// P/Invoke surface for the NT native filesystem API used by <see cref="SecureWriter"/>.
///
/// The whole point of using <c>NtCreateFile</c> instead of <c>CreateFileW</c> is that
/// <c>OBJECT_ATTRIBUTES.RootDirectory</c> lets us open a path component *relative to a
/// directory HANDLE we already hold*, passing only the bare component name in
/// <c>ObjectName</c>. This is the Win32 equivalent of POSIX <c>openat(dirfd, name, O_NOFOLLOW)</c>:
/// the parent is pinned by handle (an attacker cannot swap it), and no full path string is ever
/// re-parsed, so the classic path-string TOCTOU is eliminated by construction.
/// </summary>
internal static unsafe class NativeMethods
{
    // ---- NTSTATUS ---------------------------------------------------------
    public const int STATUS_SUCCESS = 0;
    public const uint STATUS_OBJECT_NAME_NOT_FOUND = 0xC0000034;
    public const uint STATUS_OBJECT_PATH_NOT_FOUND = 0xC000003A;
    public const uint STATUS_OBJECT_NAME_COLLISION = 0xC0000035;
    public const uint STATUS_NOT_A_DIRECTORY = 0xC0000103;
    public const uint STATUS_FILE_IS_A_DIRECTORY = 0xC00000BA;
    public const uint STATUS_ACCESS_DENIED = 0xC0000022;
    public const uint STATUS_REPARSE_POINT_ENCOUNTERED = 0xC000050B;
    public const uint STATUS_OBJECT_NAME_INVALID = 0xC0000033;
    public const uint STATUS_OBJECT_PATH_INVALID = 0xC0000039;
    public const uint STATUS_OBJECT_PATH_SYNTAX_BAD = 0xC000003B;

    /// <summary>An NTSTATUS that means "the destination path itself is malformed" (=> PATH001, not PATH002).</summary>
    public static bool IsInvalidName(uint status) =>
        status is STATUS_OBJECT_NAME_INVALID or STATUS_OBJECT_PATH_INVALID or STATUS_OBJECT_PATH_SYNTAX_BAD;

    // ---- ACCESS_MASK ------------------------------------------------------
    public const uint SYNCHRONIZE = 0x00100000;
    public const uint READ_CONTROL = 0x00020000;
    public const uint FILE_READ_ATTRIBUTES = 0x0080;
    public const uint FILE_LIST_DIRECTORY = 0x0001;
    public const uint FILE_TRAVERSE = 0x0020;
    public const uint FILE_ADD_FILE = 0x0002;
    public const uint FILE_ADD_SUBDIRECTORY = 0x0004;
    public const uint GENERIC_WRITE = 0x40000000;

    // Rights we ask for on an intermediate directory handle: enough to traverse it,
    // enumerate it, read its attributes (reparse detection), and create children under it.
    public const uint DIR_ACCESS =
        FILE_LIST_DIRECTORY | FILE_TRAVERSE | FILE_ADD_FILE | FILE_ADD_SUBDIRECTORY |
        FILE_READ_ATTRIBUTES | READ_CONTROL | SYNCHRONIZE;

    public const uint FILE_ACCESS = GENERIC_WRITE | SYNCHRONIZE;

    // ---- ShareAccess ------------------------------------------------------
    public const uint FILE_SHARE_READ = 0x00000001;
    public const uint FILE_SHARE_WRITE = 0x00000002;
    public const uint FILE_SHARE_DELETE = 0x00000004;
    public const uint FILE_SHARE_ALL = FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE;

    // ---- CreateDisposition -----------------------------------------------
    public const uint FILE_OPEN = 0x00000001;       // fail if it does not exist
    public const uint FILE_CREATE = 0x00000002;     // fail if it exists
    public const uint FILE_OPEN_IF = 0x00000003;    // open, else create
    public const uint FILE_OVERWRITE_IF = 0x00000005; // create, else truncate

    // ---- CreateOptions ----------------------------------------------------
    public const uint FILE_DIRECTORY_FILE = 0x00000001;
    public const uint FILE_SYNCHRONOUS_IO_NONALERT = 0x00000020;
    public const uint FILE_NON_DIRECTORY_FILE = 0x00000040;
    public const uint FILE_OPEN_REPARSE_POINT = 0x00200000; // == "O_NOFOLLOW" for the final name

    // ---- FileAttributes ---------------------------------------------------
    public const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    public const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
    public const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400;

    // ---- OBJECT_ATTRIBUTES.Attributes ------------------------------------
    public const uint OBJ_INHERIT = 0x00000002;
    public const uint OBJ_CASE_INSENSITIVE = 0x00000040;

    // ---- Reparse tags -----------------------------------------------------
    public const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003; // directory junction
    public const uint IO_REPARSE_TAG_SYMLINK = 0xA000000C;     // symbolic link

    // ---- Win32 (test-corpus construction only) ---------------------------
    public const uint GENERIC_WRITE_W32 = 0x40000000;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    public const uint FSCTL_SET_REPARSE_POINT = 0x000900A4;

    [StructLayout(LayoutKind.Sequential)]
    public struct UNICODE_STRING
    {
        public ushort Length;        // bytes, excluding any terminator
        public ushort MaximumLength; // bytes
        public IntPtr Buffer;        // PWSTR
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OBJECT_ATTRIBUTES
    {
        public int Length;           // = sizeof(OBJECT_ATTRIBUTES)
        public IntPtr RootDirectory; // parent handle for a RELATIVE open, or NULL for absolute
        public IntPtr ObjectName;    // PUNICODE_STRING
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IO_STATUS_BLOCK
    {
        public IntPtr StatusPointer;   // union { NTSTATUS Status; PVOID Pointer; }
        public UIntPtr Information;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FILE_ATTRIBUTE_TAG_INFORMATION
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }

    public const int FileAttributeTagInformation = 35;

    [DllImport("ntdll.dll", ExactSpelling = true)]
    public static extern int NtCreateFile(
        out IntPtr FileHandle,
        uint DesiredAccess,
        in OBJECT_ATTRIBUTES ObjectAttributes,
        out IO_STATUS_BLOCK IoStatusBlock,
        IntPtr AllocationSize,
        uint FileAttributes,
        uint ShareAccess,
        uint CreateDisposition,
        uint CreateOptions,
        IntPtr EaBuffer,
        uint EaLength);

    [DllImport("ntdll.dll", ExactSpelling = true)]
    public static extern int NtOpenFile(
        out IntPtr FileHandle,
        uint DesiredAccess,
        in OBJECT_ATTRIBUTES ObjectAttributes,
        out IO_STATUS_BLOCK IoStatusBlock,
        uint ShareAccess,
        uint OpenOptions);

    [DllImport("ntdll.dll", ExactSpelling = true)]
    public static extern int NtQueryInformationFile(
        IntPtr FileHandle,
        out IO_STATUS_BLOCK IoStatusBlock,
        IntPtr FileInformation,
        uint Length,
        int FileInformationClass);

    [DllImport("ntdll.dll", ExactSpelling = true)]
    public static extern uint RtlNtStatusToDosError(int status);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    // ---- Win32 helpers used ONLY to build the adversarial corpus ----------
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
    public static extern IntPtr CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeviceIoControl(
        IntPtr hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateHardLinkW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    // Extended-length ("\\?\") deletes so trailing-dot / device-name / ADS corpus files can be
    // removed without Win32 path normalisation silently retargeting a different name.
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "DeleteFileW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteFileW(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "RemoveDirectoryW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RemoveDirectoryW(string lpPathName);

    /// <summary>Render an NTSTATUS as its symbolic-ish hex plus the mapped Win32 message.</summary>
    public static string DescribeStatus(int status)
    {
        uint dosError = RtlNtStatusToDosError(status);
        string win32 = new System.ComponentModel.Win32Exception((int)dosError).Message;
        return $"0x{(uint)status:X8} ({win32})";
    }
}
