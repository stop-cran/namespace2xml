namespace Namespace2Xml.Output;

/// <summary>
/// Selects the platform implementation for Section 21.1 secure publication roots.
/// </summary>
public sealed class SecureRootFactory : ISecureRootFactory
{
    /// <summary>
    /// Gets whether this host has a Section 21.1 secure directory implementation.
    /// </summary>
    /// <remarks>
    /// This deliberately does not probe the filesystem. Windows relative <c>NtCreateFile</c> opens
    /// are kernel facilities, POSIX <c>openat</c> opens are host facilities, and filesystems without
    /// reparse-point or symbolic-link support are already contained because there is nothing to
    /// follow.
    /// </remarks>
    public bool SupportsSecureContainment =>
        OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    /// <inheritdoc/>
    public ISecureDirectory OpenRoot(string outputRootPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputRootPath);

        if (OperatingSystem.IsWindows())
        {
            return WindowsSecureDirectory.OpenRoot(outputRootPath);
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return PosixSecureDirectory.OpenRoot(outputRootPath);
        }

        throw new UncontainableDestinationException(
            "this platform does not provide Section 21.1 no-follow relative filesystem opens.");
    }
}
