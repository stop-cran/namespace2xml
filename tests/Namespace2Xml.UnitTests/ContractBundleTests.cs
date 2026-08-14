using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Namespace2Xml.Contract;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Specification section 22: the binary must report a revision that covers both the specification
/// text and the registry, so a defect report can name the exact contract it was measured against.
/// </summary>
[TestFixture]
public class ContractBundleTests
{
    /// <summary>
    /// The revision syntax section 22 fixes: <c>r</c>, a decimal counter, <c>+</c>, and twelve
    /// lowercase hexadecimal characters.
    /// </summary>
    private static readonly Regex RevisionSyntax = new(
        @"^r[1-9][0-9]*\+[0-9a-f]{12}$",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant);

    private static JsonElement OnDisk =>
        JsonDocument.Parse(File.ReadAllText(RepositoryLayout.ContractBundle)).RootElement;

    [Test]
    public void EmbeddedBundleMatchesTheCheckedInBundle()
    {
        ContractBundle.Current.Revision.ShouldBe(OnDisk.GetProperty("revision").GetString());
        ContractBundle.Current.SpecificationSha256
            .ShouldBe(OnDisk.GetProperty("specification").GetProperty("sha256").GetString());
        ContractBundle.Current.RegistrySha256
            .ShouldBe(OnDisk.GetProperty("registry").GetProperty("sha256").GetString());
    }

    [Test]
    public void BundleCoversTheCurrentSpecificationBytes() =>
        ContractBundle.Current.SpecificationSha256
            .ShouldBe(RepositoryLayout.Sha256Of(RepositoryLayout.Specification));

    [Test]
    public void BundleCoversTheCurrentRegistryBytes() =>
        ContractBundle.Current.RegistrySha256
            .ShouldBe(RepositoryLayout.Sha256Of(RepositoryLayout.Registry));

    [Test]
    public void RevisionIsShortEnoughToQuoteInADefectReport() =>
        ContractBundle.Current.Revision.Length.ShouldBeLessThanOrEqualTo(32);

    /// <summary>
    /// Section 22 spells the identifier exactly, because a consumer that must parse it to compare
    /// the digest component cannot do so against a shape that varies.
    /// </summary>
    [Test]
    public void RevisionMatchesTheSyntaxSection22Fixes() =>
        RevisionSyntax.IsMatch(ContractBundle.Current.Revision).ShouldBeTrue(
            $"'{ContractBundle.Current.Revision}' is not 'r', a decimal counter, '+', and twelve "
                + "lowercase hexadecimal characters");

    /// <summary>
    /// The digest component is section 22's identity for the bundle, so it must be derived from the
    /// covered artifacts rather than assigned. An assigned identifier can be reused across changed
    /// artifacts, or differ between two builds of identical ones, and the string alone shows
    /// neither.
    /// </summary>
    [Test]
    public void RevisionDigestIsDerivedFromTheCoveredArtifacts()
    {
        var derived = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            ContractBundle.Current.SpecificationSha256
                + "\n"
                + ContractBundle.Current.RegistrySha256)))[..12];

        ContractBundle.Current.Revision.Split('+')[1].ShouldBe(derived);
    }

    /// <summary>
    /// Section 22 gives the registry and the bundle one file form so that two distributions
    /// producing the same facts produce the same bytes.
    /// </summary>
    /// <param name="relativePath">The distribution-relative path section 22 names.</param>
    [TestCase("spec/contract-bundle.json")]
    [TestCase("spec/diagnostics.registry.json")]
    public void ACoveredArtifactHasTheFileFormSection22Fixes(string relativePath)
    {
        var path = Path.Combine(RepositoryLayout.Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).ShouldBeTrue($"section 22 names {relativePath}");

        var bytes = File.ReadAllBytes(path);
        bytes.Take(3).ShouldNotBe([0xEF, 0xBB, 0xBF], "a UTF-8 byte-order mark is not permitted");
        bytes.ShouldNotContain((byte)'\r', "the terminator is LF");
        bytes[^1].ShouldBe((byte)'\n', "the document ends with one LF");
        bytes[^2].ShouldNotBe((byte)'\n', "the document ends with exactly one LF");

        foreach (var line in Encoding.UTF8.GetString(bytes).Split('\n'))
        {
            (line.Length - line.TrimStart(' ').Length).ShouldBe(
                (line.Length - line.TrimStart(' ').Length) / 2 * 2,
                $"indentation is two spaces per level, but a line is indented oddly: '{line}'");
        }
    }
}
