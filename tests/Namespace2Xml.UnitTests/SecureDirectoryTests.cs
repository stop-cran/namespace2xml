using System.Runtime.InteropServices;
using Namespace2Xml.Output;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>Section 21.1 secure filesystem publication opens.</summary>
[TestFixture]
public sealed class SecureDirectoryTests
{
    /// <summary>Section 21.1 ordinary no-follow opens still create and truncate a destination.</summary>
    [Test]
    public void AnOrdinaryWriteSucceeds()
    {
        var work = NewScratchDirectory();
        var root = Path.Combine(work, "out");
        Directory.CreateDirectory(root);

        try
        {
            using var secureRoot = new SecureRootFactory().OpenRoot(root);
            using (var stream = secureRoot.CreateOrTruncateChildFile("a.txt"))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write("first");
            }

            using (var stream = secureRoot.CreateOrTruncateChildFile("a.txt"))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write("x");
            }

            File.ReadAllText(Path.Combine(root, "a.txt")).ShouldBe("x");
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    /// <summary>Section 21.1 refuses a junction before traversing an intermediate component.</summary>
    [Test]
    public void ARefusedJunctionIsReported()
    {
        var work = NewScratchDirectory();
        var root = Path.Combine(work, "out");
        var outside = Path.Combine(work, "outside");
        var junction = Path.Combine(root, "link");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);

        try
        {
            CreateJunction(junction, outside);

            using var secureRoot = new SecureRootFactory().OpenRoot(root);
            var exception = Should.Throw<UncontainableDestinationException>(
                () => secureRoot.OpenOrCreateChildDirectory("link"));

            exception.Message.ShouldContain("reparse point", Case.Sensitive);
            Directory.Exists(Path.Combine(outside, "child")).ShouldBeFalse();
        }
        finally
        {
            DeleteScratchDirectory(work);
        }
    }

    /// <summary>Section 21.1 refuses a junction at the final component before publishing.</summary>
    [Test]
    public void AFinalJunctionIsReported()
    {
        var work = NewScratchDirectory();
        var root = Path.Combine(work, "out");
        var outside = Path.Combine(work, "outside");
        var junction = Path.Combine(root, "target.txt");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);

        try
        {
            CreateJunction(junction, outside);

            using var secureRoot = new SecureRootFactory().OpenRoot(root);
            var exception = Should.Throw<UncontainableDestinationException>(
                () => secureRoot.CreateOrTruncateChildFile("target.txt"));

            exception.Message.ShouldContain("reparse point", Case.Sensitive);
            Directory.EnumerateFileSystemEntries(outside).ShouldBeEmpty();
        }
        finally
        {
            DeleteScratchDirectory(work);
        }
    }

    /// <summary>Section 21.3 can create missing nested parent directories through secure handles.</summary>
    [Test]
    public void NestedDirectoryCreationWorks()
    {
        var work = NewScratchDirectory();
        var root = Path.Combine(work, "out");
        Directory.CreateDirectory(root);

        try
        {
            using var secureRoot = new SecureRootFactory().OpenRoot(root);
            using var a = secureRoot.OpenOrCreateChildDirectory("a");
            using var b = a.OpenOrCreateChildDirectory("b");
            using (var stream = b.CreateOrTruncateChildFile("c.txt"))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write("nested");
            }

            File.ReadAllText(Path.Combine(root, "a", "b", "c.txt")).ShouldBe("nested");
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    /// <summary>A root opened once remains the trust anchor even if the root name is replaced.</summary>
    [Test]
    public void TheRootHandleIsATrustAnchor()
    {
        var work = NewScratchDirectory();
        var root = Path.Combine(work, "out");
        var moved = Path.Combine(work, "moved");
        Directory.CreateDirectory(root);

        try
        {
            using var secureRoot = new SecureRootFactory().OpenRoot(root);

            Directory.Move(root, moved);
            Directory.CreateDirectory(root);

            using (var stream = secureRoot.CreateOrTruncateChildFile("anchor.txt"))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write("anchored");
            }

            File.ReadAllText(Path.Combine(moved, "anchor.txt")).ShouldBe("anchored");
            File.Exists(Path.Combine(root, "anchor.txt")).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    /// <summary>The factory reports secure containment support on this Windows test host.</summary>
    [Test]
    public void TheFactoryReportsContainmentSupport()
    {
        new SecureRootFactory().SupportsSecureContainment.ShouldBeTrue();
    }

    /// <summary>
    /// Section 21.1's no-follow opens need each kernel's own <c>O_DIRECTORY</c> and
    /// <c>O_NOFOLLOW</c> numbers, which Linux varies by processor architecture.
    /// </summary>
    /// <remarks>
    /// The expected values are the kernel's, not the tool's: <c>00200000</c> and <c>00400000</c>
    /// from <c>include/uapi/asm-generic/fcntl.h</c>, overridden to <c>040000</c> and <c>0100000</c>
    /// by <c>arch/arm</c>, <c>arch/arm64</c> and <c>arch/powerpc</c>. Getting arm64 wrong made the
    /// root open ask for <c>O_DIRECT</c>, which fails with <c>EINVAL</c> on a directory, so no
    /// invocation on that architecture could publish anything.
    /// </remarks>
    /// <param name="architecture">The process architecture.</param>
    /// <param name="directory">The <c>O_DIRECTORY</c> that architecture's kernel expects.</param>
    /// <param name="noFollow">The <c>O_NOFOLLOW</c> that architecture's kernel expects.</param>
    [TestCase(Architecture.X86, 0x10000, 0x20000)]
    [TestCase(Architecture.X64, 0x10000, 0x20000)]
    [TestCase(Architecture.S390x, 0x10000, 0x20000)]
    [TestCase(Architecture.LoongArch64, 0x10000, 0x20000)]
    [TestCase(Architecture.RiscV64, 0x10000, 0x20000)]
    [TestCase(Architecture.Arm, 0x4000, 0x8000)]
    [TestCase(Architecture.Armv6, 0x4000, 0x8000)]
    [TestCase(Architecture.Arm64, 0x4000, 0x8000)]
    [TestCase(Architecture.Ppc64le, 0x4000, 0x8000)]
    public void LinuxNoFollowFlagsFollowTheArchitecture(
        Architecture architecture, int directory, int noFollow)
    {
        PosixSecureDirectory.NoFollowFlagsFor(macOS: false, architecture)
            .ShouldBe((directory, noFollow));
    }

    /// <summary>Section 21.1 on macOS, whose numbers do not vary by architecture.</summary>
    /// <param name="architecture">The process architecture.</param>
    [TestCase(Architecture.X64)]
    [TestCase(Architecture.Arm64)]
    public void MacOsNoFollowFlagsDoNotVary(Architecture architecture)
    {
        PosixSecureDirectory.NoFollowFlagsFor(macOS: true, architecture)
            .ShouldBe((0x100000, 0x100));
    }

    private static string NewScratchDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"n2x-secure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CreateJunction(string junction, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("junction creation is a Windows-specific reparse-point test.");
        }

        using var process = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", junction, target])
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

        process.ShouldNotBeNull();
        process.WaitForExit();

        if (process.ExitCode != 0 || !Directory.Exists(junction))
        {
            Assert.Fail(
                "failed to create a directory junction: "
                + process.StandardOutput.ReadToEnd()
                + process.StandardError.ReadToEnd());
        }
    }

    private static void DeleteScratchDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var attributes = File.GetAttributes(entry);

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    Directory.Delete(entry);
                }
                else
                {
                    File.Delete(entry);
                }
            }
            else if ((attributes & FileAttributes.Directory) != 0)
            {
                DeleteScratchDirectory(entry);
            }
            else
            {
                File.Delete(entry);
            }
        }

        Directory.Delete(path);
    }
}
