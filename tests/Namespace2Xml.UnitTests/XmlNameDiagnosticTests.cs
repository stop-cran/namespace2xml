using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Output;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// The prose of the Section 11.2 <c>XML002</c> raised when a name component cannot be written as
/// an XML name.
/// </summary>
/// <remarks>
/// <para>
/// Section 6.4.3 makes <c>message</c> "human-readable prose … never compared", so the conformance
/// corpus cannot pin it and a unit test is the only place this can be asserted. It is worth
/// asserting because Section 11.2 selects <c>NCName</c> rather than <c>Name</c>, and the colon is
/// the sole character the two productions disagree about: a report that told a reader
/// <c>a:b</c> was not a valid XML name would be telling them the opposite of what XML 1.0 says.
/// </para>
/// <para>
/// Every expectation here is authored from the specification clause named in the test.
/// </para>
/// </remarks>
[TestFixture]
public sealed class XmlNameDiagnosticTests
{
    private DiagnosticBuffer diagnostics = null!;

    [SetUp]
    public void SetUp() => diagnostics = new DiagnosticBuffer();

    /// <summary>
    /// Projects a document whose single top-level member carries <paramref name="name"/>, which
    /// Section 14.1 promotes to the document element, and returns the <c>XML002</c> prose.
    /// </summary>
    /// <param name="name">The name component text to project.</param>
    private string Message(string name)
    {
        var leaf = OverlayNode.OfPayload(ScalarPayload.OfString("1"), StableOrderingKey.FromSource(1, 0));
        var view = OverlayNode
            .Empty(NodeMarks.At(StableOrderingKey.FromSource(1, 0)))
            .WithChild(new OrdinaryPart([new LiteralToken(name)]), leaf);

        new XmlProjection(diagnostics, new DestinationRef("out.xml", 0))
            .Project(view, ImmutableArray<NamePart>.Empty);

        return diagnostics.Drain().Single(d => d.Code == "XML002").Message;
    }

    /// <summary>
    /// A colon is refused, and the report says so rather than claiming the name is not an XML
    /// name, which Section 11.2 turns on the distinction between 'NCName' and 'Name'.
    /// </summary>
    [Test]
    public void AColonIsRefusedAsPrefixedRatherThanAsInvalid()
    {
        var message = Message("a:b");

        message.ShouldContain("NCName", Case.Sensitive);
        message.ShouldContain("except the colon", Case.Sensitive);
    }

    /// <summary>
    /// The colon report explains the consequence Section 11.2 gives — that the emitted name would
    /// read back in a namespace the model never mentions — rather than only restating the rule.
    /// </summary>
    [Test]
    public void TheColonReportNamesTheRoundTripConsequence()
    {
        Message("a:b").ShouldContain("namespace this model never mentions", Case.Sensitive);
    }

    /// <summary>
    /// A name refused for any other reason cites the same production, and does not offer the
    /// colon explanation, which would not apply to it.
    /// </summary>
    [Test]
    public void ANameRefusedForAnotherReasonDoesNotMentionTheColon()
    {
        var message = Message("1abc");

        message.ShouldContain("NCName", Case.Sensitive);
        message.ShouldNotContain("colon", Case.Sensitive);
    }

    /// <summary>
    /// The report quotes the offending name, so a run over many inputs identifies which component
    /// was refused.
    /// </summary>
    [Test]
    public void TheReportQuotesTheRefusedName()
    {
        Message("x y").ShouldContain("'x y'", Case.Sensitive);
    }
}
