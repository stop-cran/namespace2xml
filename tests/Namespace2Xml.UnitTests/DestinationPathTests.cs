using Namespace2Xml.Output;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// The Section 16.2 portable segment algorithm and the Section 17.5 canonical destination path.
/// </summary>
/// <remarks>
/// Every expectation here is authored from the specification clause named in the test, never from
/// what the composer currently produces.
/// </remarks>
[TestFixture]
public class DestinationPathTests
{
    private static string Encode(string decoded)
    {
        PortableSegment.TryEncode(decoded, out var segment, out var violation).ShouldBeTrue();
        violation.ShouldBeNull();

        return segment!;
    }

    private static DestinationPath Compose(string written)
    {
        DestinationPathComposer.TryCompose(written, out var path, out var violation).ShouldBeTrue();
        violation.ShouldBeNull();

        return path!;
    }

    private static string Rejects(string written)
    {
        DestinationPathComposer.TryCompose(written, out var path, out var violation).ShouldBeFalse();
        path.ShouldBeNull();

        return violation.ShouldNotBeNull();
    }

    // ---- Section 16.2 step 5: the retained set --------------------------------------------------

    /// <summary>
    /// Section 16.2 step 5 retains "ASCII letters, digits, <c>-</c>, <c>_</c>, and <c>.</c>", so an
    /// ordinary file name survives unchanged and the encoding is invisible in the common case.
    /// </summary>
    [Test]
    public void AnOrdinaryNameIsUnchanged() => Encode("output-1_x.properties")
        .ShouldBe("output-1_x.properties");

    /// <summary>
    /// Section 16.2 step 5 encodes "every other UTF-8 byte—including <c>%</c>—as <c>%HH</c> using
    /// uppercase hexadecimal". Encoding <c>%</c> is what makes the encoding injective: without it,
    /// a literal <c>%2E</c> in the data and an encoded dot would be the same file name.
    /// </summary>
    [TestCase("a b", "a%20b")]
    [TestCase("a%b", "a%25b")]
    [TestCase("a/b", "a%2Fb")]
    [TestCase("a:b", "a%3Ab")]
    public void EveryOtherByteIsPercentEncoded(string decoded, string expected) =>
        Encode(decoded).ShouldBe(expected);

    /// <summary>
    /// Section 16.2 step 5 encodes UTF-8 bytes, not characters, so a non-ASCII character becomes one
    /// <c>%HH</c> per byte. That is what makes the portability key of Section 17.5 pure ASCII.
    /// </summary>
    [Test]
    public void NonAsciiIsEncodedByteByByte() => Encode("\u00e9").ShouldBe("%C3%A9");

    /// <summary>Section 16.2 step 5 requires uppercase hexadecimal, so the encoding is unique.</summary>
    [Test]
    public void HexadecimalIsUppercase() => Encode("\u00ff").ShouldBe("%C3%BF");

    // ---- Section 16.2 step 6: trailing dots and spaces ------------------------------------------

    /// <summary>
    /// Section 16.2 step 6 percent-encodes "every trailing dot" — every one of them, not just the
    /// last. Windows strips a trailing dot silently, which would merge two distinct destinations.
    /// </summary>
    [TestCase("a.", "a%2E")]
    [TestCase("a..", "a%2E%2E")]
    [TestCase("a.b.", "a.b%2E")]
    public void TrailingDotsAreEncoded(string decoded, string expected) =>
        Encode(decoded).ShouldBe(expected);

    /// <summary>An interior dot is ordinary and stays, so a file extension is still readable.</summary>
    [Test]
    public void AnInteriorDotIsRetained() => Encode("a.b.c").ShouldBe("a.b.c");

    /// <summary>
    /// Section 16.2 step 6 also names trailing spaces, which step 5 has already encoded because a
    /// space is not in the retained set. The rule is satisfied either way.
    /// </summary>
    [Test]
    public void ATrailingSpaceIsEncoded() => Encode("a ").ShouldBe("a%20");

    // ---- Section 16.2 steps 4 and 7: unsafe names -----------------------------------------------

    /// <summary>
    /// Section 16.2 step 7 prefixes <c>%5F</c> when the decoded segment was a dot segment. A capture
    /// holding <c>..</c> therefore becomes an ordinary name: "captured data cannot create traversal
    /// because it is encoded".
    /// </summary>
    [TestCase(".", "%5F%2E")]
    [TestCase("..", "%5F%2E%2E")]
    public void ADotSegmentIsRenamedRatherThanRejected(string decoded, string expected) =>
        Encode(decoded).ShouldBe(expected);

    /// <summary>
    /// Section 16.2: "reserved device names are deterministically renamed with the prefix rather
    /// than rejected", so a selector named <c>con</c> produces a file instead of an error.
    /// </summary>
    [TestCase("CON", "%5FCON")]
    [TestCase("con", "%5Fcon")]
    [TestCase("Com9", "%5FCom9")]
    [TestCase("LPT1.txt", "%5FLPT1.txt")]
    [TestCase("nul.a.b", "%5Fnul.a.b")]
    public void AReservedDeviceNameIsPrefixed(string decoded, string expected) =>
        Encode(decoded).ShouldBe(expected);

    /// <summary>
    /// The reserved list "is ASCII-only; <c>COM0</c>, superscript-digit variants, <c>CONIN$</c>, and
    /// <c>CONOUT$</c> are not included", so these names pass through as themselves.
    /// </summary>
    [TestCase("COM0", "COM0")]
    [TestCase("CONIN", "CONIN")]
    [TestCase("CONOUT", "CONOUT")]
    [TestCase("CONS", "CONS")]
    public void NamesOutsideTheReservedListAreNotPrefixed(string decoded, string expected) =>
        Encode(decoded).ShouldBe(expected);

    /// <summary>
    /// The device check reads "the portion before the first dot", so a name that merely starts with
    /// a device name is not one.
    /// </summary>
    [Test]
    public void ANameMerelyBeginningWithADeviceNameIsNotPrefixed() =>
        Encode("console").ShouldBe("console");

    /// <summary>Section 16.2 step 3 rejects an empty assembled segment.</summary>
    [Test]
    public void AnEmptySegmentIsRejected()
    {
        PortableSegment.TryEncode(string.Empty, out var segment, out var violation).ShouldBeFalse();

        segment.ShouldBeNull();
        violation.ShouldNotBeNullOrEmpty();
    }

    // ---- Section 16.2: composing a path ---------------------------------------------------------

    /// <summary>
    /// Section 16.2: "literal <c>/</c> or <c>\</c> separators in <c>filename</c> intentionally
    /// create subdirectories", and Section 17.5 canonicalizes them all to <c>/</c>.
    /// </summary>
    [TestCase("a/b/c.json")]
    [TestCase("a\\b\\c.json")]
    [TestCase("a/b\\c.json")]
    public void BothSeparatorsCreateHierarchyAndCanonicalizeToSlash(string written) =>
        Compose(written).Canonical.ShouldBe("a/b/c.json");

    /// <summary>
    /// Section 17.5 canonical paths have "no redundant separators", and Section 16.2 step 3 rejects
    /// the empty segment a doubled separator produces.
    /// </summary>
    [Test]
    public void ARedundantSeparatorIsRejected() => Rejects("a//b").ShouldNotBeEmpty();

    /// <summary>
    /// Section 16.2 prohibits statically written <c>.</c> and <c>..</c> segments, and Section 21.1
    /// rejects them "after filename expansion". Renaming them here would let a scheme reach outside
    /// the output root by writing what a capture is forbidden to supply.
    /// </summary>
    [TestCase("../x")]
    [TestCase("a/../x")]
    [TestCase("./x")]
    [TestCase("a/.")]
    public void AStaticallyWrittenDotSegmentIsRejected(string written) =>
        Rejects(written).ShouldNotBeEmpty();

    /// <summary>
    /// Section 21.1 rejects "rooted, UNC, device, and extended-length" forms on every platform,
    /// because Section 16.2 requires the algorithm to run "identically on every operating system".
    /// The reason is asserted, not merely the refusal: a backslash-rooted path also yields an empty
    /// leading segment, which step 3 rejects anyway, so a test that accepted any violation would
    /// still pass with the backslash dropped from the rooted test.
    /// </summary>
    [TestCase("/etc/passwd")]
    [TestCase("\\etc\\passwd")]
    [TestCase("\\\\server\\share")]
    [TestCase("\\\\?\\C:\\x")]
    [TestCase("\\\\.\\PhysicalDrive0")]
    public void ARootedPathIsRejectedForBeingRooted(string written) =>
        Rejects(written).ShouldContain("is rooted");

    /// <summary>
    /// Section 21.1 rejects drive-absolute and drive-relative forms everywhere, so that one scheme
    /// names one destination on every platform rather than a path on Linux and a drive on Windows.
    /// </summary>
    [TestCase("C:\\x")]
    [TestCase("C:x")]
    [TestCase("c:x")]
    public void ADriveQualifiedPathIsRejectedForNamingADrive(string written) =>
        Rejects(written).ShouldContain("drive-absolute or drive-relative");

    /// <summary>
    /// An empty <c>filename</c> names nothing, and is rejected before segmentation rather than
    /// composing to the output root itself.
    /// </summary>
    [Test]
    public void AnEmptyFilenameIsRejected() => Rejects("").ShouldContain("no destination");

    /// <summary>
    /// A colon inside a segment is data, not a drive marker, and step 5 encodes it. Only a colon in
    /// the second position of the whole path makes a drive form.
    /// </summary>
    [Test]
    public void AColonDeeperInThePathIsEncodedRatherThanRejected() =>
        Compose("a/b:c").Canonical.ShouldBe("a/b%3Ac");

    // ---- Section 17.5: the portability key -------------------------------------------------------

    /// <summary>
    /// Section 17.5 computes the portability key by "uppercasing ASCII letters in the canonical
    /// path", so two paths differing only in case share a key and are a blocking collision rather
    /// than a merge on one platform and two files on another.
    /// </summary>
    [Test]
    public void PathsDifferingOnlyInCaseShareAPortabilityKey()
    {
        var lower = Compose("a/b.json");
        var upper = Compose("A/B.json");

        lower.ShouldNotBe(upper);
        lower.PortabilityKey.ShouldBe(upper.PortabilityKey);
    }

    /// <summary>
    /// Paths that genuinely differ keep distinct keys; the check must not collapse everything to
    /// one bucket, which would make every second destination a collision.
    /// </summary>
    [Test]
    public void PathsDifferingInMoreThanCaseKeepDistinctKeys() =>
        Compose("a/b.json").PortabilityKey.ShouldNotBe(Compose("a/c.json").PortabilityKey);
}
