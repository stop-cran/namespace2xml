using System.Text.Json;
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
}
