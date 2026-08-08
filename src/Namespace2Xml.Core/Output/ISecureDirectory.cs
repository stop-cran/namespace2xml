namespace Namespace2Xml.Output;

/// <summary>
/// A directory opened as a Section 21.1 containment anchor.
/// </summary>
public interface ISecureDirectory : IDisposable
{
    /// <summary>
    /// Opens or creates a direct child directory without following a reparse point or symbolic link.
    /// </summary>
    /// <param name="component">The single path component to open relative to this directory.</param>
    /// <returns>The opened child directory.</returns>
    ISecureDirectory OpenOrCreateChildDirectory(string component);

    /// <summary>
    /// Creates or truncates a direct child file without following a reparse point or symbolic link.
    /// </summary>
    /// <param name="component">The single path component to open relative to this directory.</param>
    /// <returns>A writable stream over the opened child file.</returns>
    Stream CreateOrTruncateChildFile(string component);
}

/// <summary>
/// Opens Section 21.1 secure publication roots on hosts that provide no-follow relative opens.
/// </summary>
public interface ISecureRootFactory
{
    /// <summary>
    /// Gets whether this host can provide Section 21.1 secure containment primitives.
    /// </summary>
    /// <remarks>
    /// This is a platform-primitive check, not a filesystem allowlist. Windows relative
    /// <c>NtCreateFile</c> opens are kernel facilities, and POSIX <c>openat</c> opens are host
    /// facilities; filesystems without reparse-point or symbolic-link support are already
    /// contained because there is nothing to follow.
    /// </remarks>
    bool SupportsSecureContainment { get; }

    /// <summary>
    /// Opens the configured output root as the trust anchor for secure publication.
    /// </summary>
    /// <param name="outputRootPath">The configured output root path.</param>
    /// <returns>The opened root directory.</returns>
    ISecureDirectory OpenRoot(string outputRootPath);
}
