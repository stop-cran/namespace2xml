using Namespace2Xml.Diagnostics;
using Namespace2Xml.Inputs;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 11.4's <c>WARN011</c>: a later unmarked component whose simple alias is an XML
/// component already at the node "adds a second, ordinary component; it does not override the
/// existing one".
/// </summary>
/// <remarks>
/// <para>
/// The conformance corpus pins the code, the path, and the two withholding conditions end to end.
/// What it cannot pin is the prose, because Section 6.4.3 makes <c>message</c> "human-readable
/// prose … never compared" — and the prose is the whole value of this diagnostic, since the reader
/// needs the canonical address to act on it. It also cannot pin the direction cheaply, which the
/// clause states and which a symmetric implementation would get wrong in one place only.
/// </para>
/// <para>
/// Every expectation here is authored from Section 11.4, never from what the merger produces.
/// </para>
/// </remarks>
[TestFixture]
public sealed class AliasedComponentWarningTests
{
    private DiagnosticBuffer diagnostics = null!;

    [SetUp]
    public void SetUp() => diagnostics = new DiagnosticBuffer();

    private static OverlayNode Source(string document, int ordinal) =>
        NamespaceProfileReader.Read(
            [
                .. document
                    .Split('\n')
                    .Select((line, index) => NamespaceRecordClassifier.Classify(line, index + 1)),
            ],
            ordinal,
            ProfileSource.OfFile($"p{ordinal}.txt"),
            new DiagnosticBuffer())
        .Overlay;

    private IReadOnlyList<Diagnostic> Merge(params string[] documents)
    {
        new OverlayMerger(MergeStrategyMap.Default, diagnostics)
            .MergeAll(documents.Select((document, index) => Source(document, index + 1)));

        return [.. diagnostics.Drain()];
    }

    private Diagnostic Single(params string[] documents) =>
        Merge(documents).ShouldHaveSingleItem();

    /// <summary>
    /// The clause names an attribute as the component an unmarked contribution fails to override.
    /// </summary>
    [Test]
    public void AnUnmarkedComponentFollowingAnAttributeIsReported() =>
        Single("a.@x=1", "a.x=2").Code.ShouldBe("WARN011");

    /// <summary>
    /// Section 22 gives the code a <c>path</c>, and the occurrence is the component that was
    /// added: the one already there is named in the prose, and is not the news.
    /// </summary>
    [Test]
    public void TheReportedPathIsTheComponentThatWasAdded() =>
        Single("a.@x=1", "a.x=2").Path.ShouldBe("a.x");

    /// <summary>
    /// Section 11.4: "writing the contribution canonically — <c>@x</c> for the attribute,
    /// <c>Q{}x</c> for the element — is what expresses the override". A reader who is not told
    /// the address has been told only that something is wrong.
    /// </summary>
    [Test]
    public void TheReportNamesTheCanonicalAddressThatWouldOverride() =>
        Single("a.@x=1", "a.x=2").Message.ShouldContain("a.@x", Case.Sensitive);

    /// <summary>
    /// Section 13.1 "replaces every <c>Q{uri}local</c> … part with <c>local</c>", so a qualified
    /// element competes for the same simple alias as an attribute does.
    /// </summary>
    [Test]
    public void AQualifiedElementIsAliasedToo() =>
        Single("a.Q{urn:p}x=1", "a.x=2").Message.ShouldContain("Q{urn:p}x", Case.Sensitive);

    /// <summary>
    /// Section 11.4: "components arriving together in one contribution never warn, since a single
    /// XML document may legitimately carry an attribute and a child element of the same name".
    /// </summary>
    [Test]
    public void ComponentsArrivingInOneContributionAreNotReported() =>
        Merge("a.@x=1\na.x=2").ShouldBeEmpty();

    /// <summary>
    /// Section 11.4 has a marked component "bypass that index and name one canonical component
    /// outright", so <c>Q{}x</c> has said which of the two it means and is not a mistake.
    /// </summary>
    [Test]
    public void AnExplicitlyCanonicalComponentIsNotReported() =>
        Merge("a.@x=1", "a.Q{}x=2").ShouldBeEmpty();

    /// <summary>
    /// An unmarked component landing on an unmarked component of the same name is an ordinary
    /// Section 17.1 override, which is the outcome the reader wanted.
    /// </summary>
    [Test]
    public void AnOrdinaryComponentOverridingItsOwnKindIsNotReported() =>
        Merge("a.x=1", "a.x=2").ShouldBeEmpty();

    /// <summary>
    /// The clause is directional: it reports "a later contribution that writes an unmarked
    /// component where an earlier contribution already placed an XML component". A later marked
    /// component has named what it wants and cannot have meant the other one.
    /// </summary>
    [Test]
    public void AnAttributeArrivingAfterAnOrdinaryComponentIsNotReported() =>
        Merge("a.x=1", "a.@x=2").ShouldBeEmpty();

    /// <summary>
    /// Section 5.2 lists the sibling kinds as "ordinary component, qualified element, typed
    /// attribute, typed content". A node can carry two XML components of one alias, and Section 24
    /// forbids the reported string depending on how the store happened to enumerate them, so the
    /// smallest under that order is the one named.
    /// </summary>
    /// <remarks>
    /// Several names are asserted rather than one because the children are held in a hash trie:
    /// for any single name its enumeration order may agree with Section 5.2 by coincidence, and an
    /// implementation that simply took the first candidate would pass. Every case here is the
    /// outcome Section 5.2 specifies, so none of them pins a coincidence.
    /// </remarks>
    [Test]
    public void TheSmallestAliasingComponentUnderSectionFiveTwoIsReported()
    {
        foreach (var local in new[] { "x", "y", "z", "p", "q", "r", "s", "t", "u", "v", "w", "n" })
        {
            diagnostics = new DiagnosticBuffer();

            Single($"a.@{local}=1\na.Q{{urn:p}}{local}=1", $"a.{local}=2")
                .Message.ShouldContain($"a.Q{{urn:p}}{local}", Case.Sensitive);
        }
    }

    /// <summary>
    /// The warning "reports and never changes that model", so both components survive it.
    /// </summary>
    [Test]
    public void TheReportedContributionStillAddsItsComponent()
    {
        var merged = new OverlayMerger(MergeStrategyMap.Default, diagnostics)
            .MergeAll([Source("a.@x=1", 1), Source("a.x=2", 2)]);

        merged.Children.Values.Single().Children.Count.ShouldBe(2);
    }
}
