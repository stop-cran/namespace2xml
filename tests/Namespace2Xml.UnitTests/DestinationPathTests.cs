using Namespace2Xml.Output;
using Namespace2Xml.Profiles;
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

    private static InterpretedValue Template(string written)
    {
        var lexed = ValueLexer.Lex(written, ValueSyntax.Profile(WildcardSyntax.Unnamed));

        lexed.Value.ShouldNotBeNull();

        return lexed.Value;
    }

    private static WildcardCaptures Captures(string[] positional) =>
        new([.. positional], System.Collections.Immutable.ImmutableDictionary<string, string>.Empty);

    private static DestinationPath Compose(string written, params string[] captures)
    {
        DestinationPathComposer.TryCompose(
            Template(written),
            Captures(captures),
            out var path,
            out var violation).ShouldBeTrue();
        violation.ShouldBeNull();

        return path!;
    }

    private static string Rejects(string written, params string[] captures)
    {
        DestinationPathComposer.TryCompose(
            Template(written),
            Captures(captures),
            out var path,
            out var violation).ShouldBeFalse();
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

    // ---- Section 16.2 steps 1 and 2: captured text is data, not path syntax ----------------------

    /// <summary>
    /// Section 16.2: "only separators written literally in the scheme create directory hierarchy;
    /// separators originating inside captured data are encoded", and "captured data cannot create
    /// traversal because it is encoded".
    /// </summary>
    /// <remarks>
    /// This is step ordering, not a separate rule. Step 1 splits "the scheme-written path", step 2
    /// substitutes "inside the segment". Composing the substituted text instead applies them in the
    /// other order, and the capture's separator then becomes hierarchy.
    /// </remarks>
    [TestCase("/")]
    [TestCase("\\")]
    public void ASeparatorInsideACaptureIsEncodedRatherThanSplit(string separator) =>
        Compose($"out/*.conf", $"p{separator}q").Canonical.ShouldBe(
            separator == "/" ? "out/p%2Fq.conf" : "out/p%5Cq.conf");

    /// <summary>
    /// A capture holding a traversal sequence composes to one ordinary file name, so the whole
    /// sequence lands inside the output root rather than above it.
    /// </summary>
    /// <remarks>
    /// The dots survive step 5, which retains <c>.</c>, and that is not a weakness: what makes a
    /// <c>..</c> traversal is a <i>segment</i> equal to <c>..</c>, and the encoded separators mean
    /// this text can never be split into one.
    /// </remarks>
    [Test]
    public void ACaptureHoldingATraversalSequenceCannotEscape() =>
        Compose("out/*", "../../etc/passwd").Canonical
            .ShouldBe("out/..%2F..%2Fetc%2Fpasswd");

    /// <summary>
    /// Section 16.2 step 4 records a dot-segment condition and step 7 renames it with <c>%5F</c>.
    /// Only a <i>statically written</i> dot segment is "prohibited", so a captured one is renamed
    /// deterministically, exactly as a captured device name is.
    /// </summary>
    [TestCase(".", "out/%5F%2E")]
    [TestCase("..", "out/%5F%2E%2E")]
    public void ACapturedDotSegmentIsRenamedRatherThanRejected(string capture, string expected) =>
        Compose("out/*", capture).Canonical.ShouldBe(expected);

    /// <summary>
    /// The distinction is between written and captured text, not between the strings themselves: the
    /// same <c>..</c> is rejected when the scheme wrote it and renamed when a capture supplied it.
    /// </summary>
    [Test]
    public void TheSameDotSegmentIsRejectedWrittenAndRenamedCaptured()
    {
        Rejects("out/../x").ShouldContain("statically written");
        Compose("out/*/x", "..").Canonical.ShouldBe("out/%5F%2E%2E/x");
    }

    /// <summary>
    /// Section 21.1's rejected forms are properties of the scheme-written path. A capture is opaque
    /// data, so text that would name a drive when written composes to an ordinary segment when
    /// captured -- and cannot silently become a path outside the output root either way.
    /// </summary>
    [Test]
    public void ADriveFormInsideACaptureIsEncodedRatherThanRejected()
    {
        Rejects("C:/x").ShouldContain("drive-absolute");
        Compose("*/x", "C:").Canonical.ShouldBe("C%3A/x");
    }

    /// <summary>
    /// A rooted capture is data for the same reason, so it does not make the whole path rooted and
    /// does not produce an empty leading segment.
    /// </summary>
    [Test]
    public void ARootedCaptureDoesNotMakeThePathRooted() =>
        Compose("out/*", "/etc").Canonical.ShouldBe("out/%2Fetc");

    /// <summary>
    /// Section 12.1's legacy clamp is a property of the whole value: "if a legacy value contains
    /// more wildcard substitutions than the name produced, the last capture is repeated". Counting
    /// per segment would restart the clamp at every separator and bind the wrong capture.
    /// </summary>
    [Test]
    public void PositionalCapturesAreNumberedAcrossSeparators() =>
        Compose("*/*/*.conf", "one", "two").Canonical.ShouldBe("one/two/two.conf");

    /// <summary>
    /// A capture is substituted "as decoded opaque text inside the segment", so the literal text
    /// around it joins it into one segment rather than forming segments of its own.
    /// </summary>
    [Test]
    public void ACaptureJoinsTheLiteralTextAroundItIntoOneSegment() =>
        Compose("out/pre-*-post.conf", "x/y").Canonical.ShouldBe("out/pre-x%2Fy-post.conf");

    /// <summary>
    /// Section 16.2's device check reads the assembled segment, so a capture that is not itself a
    /// device name still triggers the rename when the extension around it completes one.
    /// </summary>
    [Test]
    public void TheDeviceCheckReadsTheAssembledSegmentNotTheCapture() =>
        Compose("out/*.conf", "NUL").Canonical.ShouldBe("out/%5FNUL.conf");
}
