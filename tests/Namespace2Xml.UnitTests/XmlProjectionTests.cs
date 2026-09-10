using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Output;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using Namespace2Xml.Text;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>Direct Section 19.5 projection gates for XML document-envelope metadata.</summary>
[TestFixture]
public sealed class XmlProjectionTests
{
    private static OrdinaryPart Ordinary(string text) => new([new LiteralToken(text)]);

    private static XmlEnvelopeComment Envelope(
        string text, XmlEnvelopePlacement placement, int source, int item) =>
        new(text, placement, StableOrderingKey.FromSource(source, item));

    private static OverlayNode View() =>
        OverlayNode
            .Empty(NodeMarks.At(StableOrderingKey.FromSource(0, 1)))
            .WithChild(
                Ordinary("value"),
                OverlayNode.OfPayload(
                    ScalarPayload.OfString("1"),
                    StableOrderingKey.FromSource(0, 2)));

    /// <summary>
    /// Sections 16.3 and 19.5 keep envelope comments outside a configured root; the wrapper may
    /// change the document element but cannot address or move document-envelope metadata.
    /// </summary>
    [Test]
    public void EnvelopeCommentsStayOutsideAConfiguredRoot()
    {
        var diagnostics = new DiagnosticBuffer();
        ImmutableArray<XmlEnvelopeComment> envelope =
        [
            Envelope("before", XmlEnvelopePlacement.Leading, source: 0, item: 0),
            Envelope("after", XmlEnvelopePlacement.Trailing, source: 0, item: 9),
        ];

        var document = new XmlProjection(diagnostics, new DestinationRef("out.xml", 0))
            .Project(View(), [Ordinary("configured")], envelope)
            .ShouldNotBeNull();

        document.Element.Name.LocalName.ShouldBe("configured");
        document.Leading.Select(comment => comment.Value).ShouldBe(["before"]);
        document.Trailing.Select(comment => comment.Value).ShouldBe(["after"]);
        document.Element.DescendantNodes().OfType<System.Xml.Linq.XComment>().ShouldBeEmpty();
        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 19.5 orders ordinary document-position comments and XML envelope comments by their
    /// stable source occurrence within the same leading/trailing position.
    /// </summary>
    [Test]
    public void EnvelopeAndOrdinaryDocumentCommentsShareStableOrdering()
    {
        var diagnostics = new DiagnosticBuffer();
        var view = View()
            .WithComment(new BoundComment(
                "ordinary",
                CommentPlacement.Leading,
                StableOrderingKey.FromSource(1, 2)));
        ImmutableArray<XmlEnvelopeComment> envelope =
        [
            Envelope("envelope-first", XmlEnvelopePlacement.Leading, source: 0, item: 8),
            Envelope("envelope-last", XmlEnvelopePlacement.Leading, source: 2, item: 1),
        ];

        var document = new XmlProjection(diagnostics, new DestinationRef("out.xml", 0))
            .Project(view, ImmutableArray<NamePart>.Empty, envelope)
            .ShouldNotBeNull();

        document.Leading.Select(comment => comment.Value)
            .ShouldBe(["envelope-first", "ordinary", "envelope-last"]);
        diagnostics.Drain().ShouldBeEmpty();
    }
}
