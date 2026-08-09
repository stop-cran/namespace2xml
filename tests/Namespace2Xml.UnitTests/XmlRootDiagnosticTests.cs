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
/// The prose of the Section 14.1 <c>TYPE001</c> raised when an implicit root has more than one
/// top-level member.
/// </summary>
/// <remarks>
/// <para>
/// Section 6.4.3 makes <c>message</c> "human-readable prose … never compared", so the conformance
/// corpus cannot pin it and a unit test is the only place this can be asserted. It is worth
/// asserting because the condition it describes is reached by ordinary indented XML, where the
/// literal reading of the message — that the user's selection has several roots — is true but
/// unhelpful: the extra members are the whitespace the Section 11.7 default retained.
/// </para>
/// <para>
/// Every expectation here is authored from the specification clause named in the test.
/// </para>
/// </remarks>
[TestFixture]
public sealed class XmlRootDiagnosticTests
{
    private const string Remedy = "NormalizeFormattingWhitespace";

    private DiagnosticBuffer diagnostics = null!;

    [SetUp]
    public void SetUp() => diagnostics = new DiagnosticBuffer();

    private static OrdinaryPart Ordinary(string text) => new([new LiteralToken(text)]);

    private static OverlayNode Container(int source) =>
        OverlayNode.Empty(NodeMarks.At(StableOrderingKey.FromSource(source, 0)));

    private static OverlayNode Leaf(string text, int source) =>
        OverlayNode.OfPayload(ScalarPayload.OfString(text), StableOrderingKey.FromSource(source, 0));

    private string Message(OverlayNode view)
    {
        new XmlProjection(diagnostics, new DestinationRef("out.xml", 0))
            .Project(view, ImmutableArray<NamePart>.Empty);

        return diagnostics.Drain().Single().Message;
    }

    /// <summary>
    /// An indented document element: whitespace-only text before and after the one child element,
    /// which Section 11.4 addresses as content components and Section 11.7's default retains.
    /// </summary>
    private static OverlayNode Indented() =>
        Container(1)
            .WithChild(new ContentPart(0), Leaf("\n  ", 1))
            .WithChild(new ContentPart(1), Container(1).WithChild(Ordinary("b"), Leaf("1", 1)))
            .WithChild(new ContentPart(2), Leaf("\n", 1));

    /// <summary>
    /// Section 14.1 is still the clause being enforced, so the sentence it justifies must survive
    /// the addition of any guidance after it.
    /// </summary>
    [Test]
    public void TheRootRequirementIsStatedBeforeAnyGuidance() =>
        Message(Indented()).ShouldContain(
            "Section 14.1 requires an explicit 'root'", Case.Sensitive);

    /// <summary>
    /// Section 11.7's mode is what turns this input into one the clause is satisfied by, so the
    /// message names it rather than leaving the reader to find Section 11.7 unaided.
    /// </summary>
    [Test]
    public void FormattingWhitespaceIsNamedAsTheRemedy() =>
        Message(Indented()).ShouldContain(Remedy, Case.Sensitive);

    /// <summary>
    /// A view with two genuine element members is the case Section 19.5 is really about. Naming a
    /// whitespace mode there would send the reader to a setting that cannot help them.
    /// </summary>
    [Test]
    public void AGenuinelyMultiRootedViewIsNotSentToAWhitespaceMode()
    {
        var view = Container(1)
            .WithChild(Ordinary("a"), Leaf("1", 1))
            .WithChild(Ordinary("b"), Leaf("2", 2));

        Message(view).ShouldNotContain(Remedy, Case.Sensitive);
    }

    /// <summary>
    /// Content components holding real text are mixed content, not indentation, and Section 11.7's
    /// mode preserves them. Recommending it would be wrong.
    /// </summary>
    [Test]
    public void ContentThatIsNotWhitespaceIsNotIndentation()
    {
        var view = Container(1)
            .WithChild(new ContentPart(0), Leaf("text", 1))
            .WithChild(new ContentPart(1), Container(1).WithChild(Ordinary("b"), Leaf("1", 1)));

        Message(view).ShouldNotContain(Remedy, Case.Sensitive);
    }
}
