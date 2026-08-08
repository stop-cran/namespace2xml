using System.Collections.Immutable;
using Namespace2Xml.Budgets;
using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Inputs;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using Namespace2Xml.Text;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 11 tests for reading one XML document into the structured model.
/// </summary>
/// <remarks>
/// <para>
/// Every expectation here is authored from the specification clause named in the test, never from
/// what the reader currently produces. Section 11.3 and Section 11.4 give worked examples of the
/// canonical addresses, and <see cref="Paths"/> exists so those examples can be asserted in the
/// spelling the specification writes them in.
/// </para>
/// <para>
/// The speller <see cref="Spell"/> reimplements Appendix A.2 rather than calling the reader's own,
/// for the reason the conformance comparer does not delegate to the production writer: an oracle
/// that asks the implementation what a name looks like cannot notice the implementation spelling it
/// wrongly.
/// </para>
/// </remarks>
[TestFixture]
public class XmlInputReaderTests
{
    private static StructuredNode? Read(
        string document,
        DiagnosticBuffer diagnostics,
        ResourceLimits? limits = null,
        SourceBudget? budget = null,
        SourceEncoding encoding = SourceEncoding.Utf8,
        XmlInputOptions options = XmlInput.Default)
    {
        var effective = limits ?? ResourceLimits.Defaults;

        return XmlInputReader.Read(
            document,
            encoding,
            options,
            budget ?? new SourceBudget(effective, 0),
            ProfileSource.OfFile("d.xml"),
            DiagnosticPhase.Input,
            diagnostics,
            StableOrderingKey.FromSource(0, 1));
    }

    private static StructuredNode Read(string document)
    {
        var buffer = new DiagnosticBuffer();
        var node = Read(document, buffer);

        buffer.Drain().ShouldBeEmpty();

        return node.ShouldNotBeNull();
    }

    private static ImmutableArray<Diagnostic> Diagnose(
        string document, SourceEncoding encoding = SourceEncoding.Utf8)
    {
        var buffer = new DiagnosticBuffer();

        Read(document, buffer, encoding: encoding).ShouldBeNull();

        return buffer.Drain();
    }

    private static Diagnostic Refusal(
        string document, SourceEncoding encoding = SourceEncoding.Utf8) =>
        Diagnose(document, encoding).ShouldHaveSingleItem();

    /// <summary>
    /// The bound this reader crossed, if any.
    /// </summary>
    /// <param name="document">The document to read.</param>
    /// <param name="limits">The bounds to read it under.</param>
    /// <returns>The crossed bound, or <see langword="null"/> when none was.</returns>
    /// <remarks>
    /// A crossing is recorded on the budget rather than reported here, because Section 7.3 judges
    /// <c>--max-nodes</c> at the join. Asserting on the diagnostic buffer would assert on an empty
    /// buffer and pass for the wrong reason. Recording the fault is only half of the contract, so
    /// this also asserts the reader <em>stopped</em>: one that noted the crossing and kept walking
    /// would still report <c>LIMIT001</c> while doing the unbounded work the bound exists to
    /// prevent.
    /// </remarks>
    private static ResourceBound? Crossed(string document, ResourceLimits limits)
    {
        var budget = new SourceBudget(limits, 0);
        var buffer = new DiagnosticBuffer();

        var node = Read(document, buffer, limits, budget);
        buffer.Drain().ShouldBeEmpty();

        if (budget.Fault is null)
        {
            node.ShouldNotBeNull();
            return null;
        }

        node.ShouldBeNull();
        return budget.Fault.Value.Bound;
    }

    /// <summary>Everything this document charged against Section 23's bounds.</summary>
    /// <param name="document">The document to read.</param>
    private static SourceTally Charged(string document)
    {
        var budget = new SourceBudget(ResourceLimits.Defaults, 0);
        var buffer = new DiagnosticBuffer();

        Read(document, buffer, ResourceLimits.Defaults, budget).ShouldNotBeNull();
        buffer.Drain().ShouldBeEmpty();

        return budget.Tally;
    }

    private static long Nodes(string document)
    {
        var budget = new SourceBudget(ResourceLimits.Defaults, 0);
        var buffer = new DiagnosticBuffer();

        Read(document, buffer, ResourceLimits.Defaults, budget).ShouldNotBeNull();
        buffer.Drain().ShouldBeEmpty();

        return budget.Tally.Nodes;
    }

    /// <summary>The document element's node, which the document contributes one property for.</summary>
    /// <param name="document">The document to read.</param>
    private static StructuredNode Element(string document) =>
        Read(document).ShouldBeOfType<StructuredMapping>().Properties.Single().Value;

    /// <summary>The document element's name.</summary>
    /// <param name="document">The document to read.</param>
    private static NamePart Name(string document) =>
        Read(document).ShouldBeOfType<StructuredMapping>().Properties.Single().Name;

    /// <summary>
    /// Every canonical address the document projects, with its scalar, in document order.
    /// </summary>
    /// <param name="document">The document to read.</param>
    /// <remarks>
    /// Section 11.3 writes its expectations as a list of paths, so this writes the reader's result
    /// the same way and the two can be compared directly. A node with no scalar of its own
    /// contributes no line unless it is an explicit shape contribution, which is written
    /// <c>{}</c>.
    /// </remarks>
    private static ImmutableArray<string> Paths(string document)
    {
        var result = ImmutableArray.CreateBuilder<string>();

        Walk(Read(document), string.Empty, result);

        return result.ToImmutable();
    }

    /// <summary>The paths a document projects in Section 11.7's normalizing mode.</summary>
    /// <param name="document">The document to read.</param>
    /// <param name="warnings">The diagnostic codes the read emitted.</param>
    private static ImmutableArray<string> Normalized(
        string document, out ImmutableArray<string> warnings)
    {
        var buffer = new DiagnosticBuffer();
        var node = Read(
            document,
            buffer,
            options: XmlInputOptions.NormalizeFormattingWhitespace);

        warnings = [.. buffer.Drain().Select(entry => entry.Code)];

        var result = ImmutableArray.CreateBuilder<string>();

        Walk(node.ShouldNotBeNull(), string.Empty, result);

        return result.ToImmutable();
    }

    private static void Walk(
        StructuredNode node, string path, ImmutableArray<string>.Builder into)
    {
        switch (node)
        {
            case StructuredScalar scalar:
                into.Add($"{path}={Text(scalar)}");
                break;

            case StructuredMapping mapping:
                if (mapping.Scalar is { } own)
                {
                    into.Add($"{path}={Text(own)}");
                }
                else if (mapping.Properties.IsEmpty)
                {
                    into.Add($"{path}={{}}");
                }

                foreach (var property in mapping.Properties)
                {
                    Walk(property.Value, Join(path, Spell(property.Name)), into);
                }

                break;

            case StructuredSequence sequence:
                for (var index = 0; index < sequence.Items.Length; index++)
                {
                    Walk(sequence.Items[index], Join(path, index.ToString(Culture)), into);
                }

                break;

            default:
                throw new InvalidOperationException($"'{node.GetType().Name}' is not a shape.");
        }
    }

    private static System.Globalization.CultureInfo Culture =>
        System.Globalization.CultureInfo.InvariantCulture;

    private static string Join(string path, string part) =>
        path.Length == 0 ? part : path + "." + part;

    private static string Text(StructuredScalar scalar) =>
        scalar.NativeString ?? scalar.Payload!.ToCanonicalText();

    /// <summary>Spells a name as Appendix A.2 writes it.</summary>
    /// <param name="part">The name.</param>
    private static string Spell(NamePart part) => part switch
    {
        OrdinaryPart ordinary => ordinary.LiteralText!,
        QualifiedElementPart qualified =>
            "Q{" + qualified.Uri + "}" + ((LiteralToken)qualified.Local.Single()).Text,
        AttributePart attribute => "@" + Spell(attribute.Name),
        ContentPart content => "#" + content.Ordinal.ToString(Culture),
        _ => throw new InvalidOperationException($"'{part.GetType().Name}' is not a name part."),
    };

    // Section 11.1 -- the parser's posture.

    /// <summary>
    /// Section 11.1 prohibits DTDs, and Section 11.8 places them outside the preservation contract.
    /// Both an internal subset and an external identifier are declarations of one.
    /// </summary>
    [TestCase("<!DOCTYPE a [<!ENTITY e \"x\">]><a>y</a>")]
    [TestCase("<!DOCTYPE a SYSTEM \"a.dtd\"><a/>")]
    [TestCase("<!DOCTYPE a PUBLIC \"-//X//DTD//EN\" \"http://example.invalid/a.dtd\"><a/>")]
    [TestCase("<!DOCTYPE a><a/>")]
    public void ADocumentTypeDeclarationIsRefused(string document) =>
        Refusal(document).Code.ShouldBe("XML001");

    /// <summary>
    /// Section 11.1 requires that a DTD be "rejected, not partially processed". An entity defined
    /// in an internal subset must therefore never be expanded, which is what makes an exponential
    /// expansion unreachable rather than merely bounded.
    /// </summary>
    [Test]
    public void AnExponentialEntityExpansionIsNeverPerformed()
    {
        var document =
            "<!DOCTYPE l [<!ENTITY a \"aaaaaaaaaa\">"
            + "<!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;\">"
            + "<!ENTITY c \"&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;\">]><l>&c;</l>";

        Refusal(document).Code.ShouldBe("XML001");
    }

    /// <summary>
    /// Section 11.1 prohibits external entities and network retrieval. With no DTD there is no way
    /// to declare an entity, so the reference is simply undefined and the document is malformed.
    /// </summary>
    [Test]
    public void AnUndeclaredEntityReferenceIsMalformed() =>
        Refusal("<a>&secret;</a>").Code.ShouldBe("PARSE001");

    /// <summary>
    /// A markup declaration this reader does not recognize as a document type declaration reaches
    /// the host, whose refusal offers a remedy that does not exist here. The advice must not
    /// survive into the diagnostic.
    /// </summary>
    /// <param name="document">The document to read.</param>
    /// <remarks>
    /// The host answers every DTD refusal with "For security reasons DTD is prohibited in this XML
    /// document." followed by "To enable DTD processing set the DtdProcessing property on
    /// XmlReaderSettings to Parse and pass the settings into XmlReader.Create method." Nobody
    /// running this tool has an <c>XmlReaderSettings</c> to set, and Section 11.1 prohibits DTDs
    /// outright rather than behind a setting, so the second sentence describes a remedy that
    /// cannot be taken and implies a supported mode that does not exist. Only XML 1.0's
    /// case-sensitive <c>&lt;!DOCTYPE</c> is a document type declaration, so these spellings are
    /// malformed markup rather than a DTD and never reach this reader's own refusal.
    /// </remarks>
    [TestCase("<!doctype a><a/>")]
    [TestCase("<!DocType a><a/>")]
    [TestCase("<!FOO a><a/>")]
    public void AHostRemedyThisToolDoesNotOfferIsNotRepeated(string document)
    {
        var refusal = Refusal(document);

        refusal.Code.ShouldBe("PARSE001");
        refusal.Message.ShouldNotBeNull().ShouldNotContain("DtdProcessing", Case.Sensitive);
        refusal.Message.ShouldNotBeNull().ShouldNotContain("XmlReader", Case.Sensitive);
        refusal.Message.ShouldNotBeNull().ShouldBe(
            "For security reasons DTD is prohibited in this XML document.");
    }

    /// <summary>
    /// A document type declaration may follow whitespace, comments, and processing instructions,
    /// which are the whole of what an XML prolog may hold before it.
    /// </summary>
    [TestCase("<!-- c -->\n<!DOCTYPE a><a/>")]
    [TestCase("<?pi x?><!DOCTYPE a><a/>")]
    [TestCase("\n  <!DOCTYPE a><a/>")]
    [TestCase("<?xml version=\"1.0\"?><!DOCTYPE a><a/>")]
    public void ADocumentTypeDeclarationIsFoundAfterTheRestOfTheProlog(string document) =>
        Refusal(document).Code.ShouldBe("XML001");

    /// <summary>
    /// The position of the <c>&lt;!DOCTYPE</c> token is measured by Section 22's rules even when
    /// the prolog before it must be skipped, so what a comment or processing instruction contains
    /// cannot move the reported position.
    /// </summary>
    /// <param name="document">The document to refuse.</param>
    /// <param name="line">The Section 22 line the declaration starts on.</param>
    /// <param name="column">The Section 22 column it starts at.</param>
    /// <remarks>
    /// Section 22: "A line is terminated by LF, CRLF, or a lone CR, and by nothing else", and
    /// "a character outside the Basic Multilingual Plane occupies one column". Both rules apply
    /// inside a skipped span exactly as they do outside one. The emoji case is the discriminating
    /// one: it is stored as two UTF-16 code units, so a scanner counting storage rather than
    /// scalars reports column 10 for a declaration that stands in column 9.
    /// </remarks>
    [TestCase("<!DOCTYPE a><a/>", 1, 1)]
    [TestCase("  <!DOCTYPE a><a/>", 1, 3)]
    [TestCase("<!-- c -->\n<!DOCTYPE a><a/>", 2, 1)]
    [TestCase("<!-- c --><!DOCTYPE a><a/>", 1, 11)]
    [TestCase("<!--a\nb--><!DOCTYPE q><q/>", 2, 5)]
    [TestCase("<!--a\r\nb--><!DOCTYPE q><q/>", 2, 5)]
    [TestCase("<!--a\rb--><!DOCTYPE q><q/>", 2, 5)]
    [TestCase("<?pi\rx?><!DOCTYPE q><q/>", 2, 4)]
    [TestCase("<!--\U0001F600--><!DOCTYPE a><a/>", 1, 9)]
    [TestCase("<?pi \U0001F600?><!DOCTYPE a><a/>", 1, 9)]
    public void ADocumentTypeDeclarationNamesItsPosition(string document, int line, int column)
    {
        var refusal = Refusal(document);

        refusal.Code.ShouldBe("XML001");
        refusal.Line.ShouldBe(line);
        refusal.Column.ShouldBe(column);
    }

    /// <summary>
    /// Section 22: a character outside the Basic Multilingual Plane "occupies one column". Three
    /// documents differing only in which single scalar stands before the fault therefore report
    /// one position.
    /// </summary>
    /// <remarks>
    /// <see cref="System.Xml.IXmlLineInfo.LinePosition"/> measures a column in UTF-16 code units,
    /// where U+1F600 occupies two, so passing it through unconverted reports the emoji document
    /// one column further right than the other two. Comparing the three is what makes this a claim
    /// about the unit rather than about where a host parser chooses to point.
    /// </remarks>
    [Test]
    public void ASupplementaryScalarOccupiesOneColumn()
    {
        var ascii = Refusal("<a>x</b>");
        var basic = Refusal("<a>\u0436</b>");
        var supplementary = Refusal("<a>\U0001F600</b>");

        basic.Column.ShouldBe(ascii.Column);
        supplementary.Column.ShouldBe(ascii.Column);
        ascii.Column.ShouldNotBeNull();
    }

    /// <summary>
    /// Only a real declaration is one. Text that merely spells <c>&lt;!DOCTYPE</c> inside a comment
    /// or inside content declares nothing, and refusing it would reject a valid document.
    /// </summary>
    [TestCase("<!-- <!DOCTYPE a> --><a/>")]
    [TestCase("<a>&lt;!DOCTYPE a&gt;</a>")]
    [TestCase("<a><!-- <!DOCTYPE a> --></a>")]
    public void TextThatMerelyResemblesADeclarationIsNotOne(string document) =>
        Read(document).ShouldBeOfType<StructuredMapping>();

    /// <summary>
    /// Section 11.1 admits "the five predefined entities and numeric character references", which
    /// need no DTD.
    /// </summary>
    [TestCase("<a>&lt;&gt;&amp;&quot;&apos;</a>", "<>&\"'")]
    [TestCase("<a>&#65;&#x42;</a>", "AB")]
    public void ThePredefinedEntitiesAndCharacterReferencesExpand(string document, string text) =>
        Paths(document).ShouldBe(["a=" + text]);

    // Section 11.1 -- bounds.

    /// <summary>
    /// Section 11.1 bounds nesting with <c>--max-depth</c>, "counting the document element as depth
    /// 1". A document element alone therefore fits a bound of one, and one child does not.
    /// </summary>
    [TestCase("<a/>", 1, false)]
    [TestCase("<a><b/></a>", 1, true)]
    [TestCase("<a><b/></a>", 2, false)]
    [TestCase("<a><b><c/></b></a>", 2, true)]
    public void TheDocumentElementIsDepthOne(string document, int maxDepth, bool crossed) =>
        Crossed(document, ResourceLimits.Defaults with { MaxDepth = maxDepth })
            .ShouldBe(crossed ? ResourceBound.MaxDepth : null);

    /// <summary>
    /// An attribute and a text node are children of their element, so they sit one level below it
    /// and a bound that admits the element alone does not admit them.
    /// </summary>
    [TestCase("<a x=\"1\"/>", 1, true)]
    [TestCase("<a>t</a>", 1, true)]
    [TestCase("<a><!--c--></a>", 1, true)]
    [TestCase("<a><![CDATA[t]]></a>", 1, true)]
    [TestCase("<a x=\"1\">t</a>", 2, false)]
    public void AnElementsOwnContentSitsBelowIt(string document, int maxDepth, bool crossed) =>
        Crossed(document, ResourceLimits.Defaults with { MaxDepth = maxDepth })
            .ShouldBe(crossed ? ResourceBound.MaxDepth : null);

    /// <summary>
    /// The host parser's own nesting knob must never be the effective limit, so a document nested
    /// far past the bound still crosses <c>--max-depth</c> rather than failing to parse or
    /// overflowing the stack.
    /// </summary>
    [Test]
    public void TheHostDepthKnobNeverDecides()
    {
        var document = string.Concat(Enumerable.Repeat("<a>", 500))
            + "x"
            + string.Concat(Enumerable.Repeat("</a>", 500));

        Crossed(document, ResourceLimits.Defaults with { MaxDepth = 3 })
            .ShouldBe(ResourceBound.MaxDepth);
    }

    /// <summary>
    /// Section 11.1 bounds one element's attributes with <c>--max-xml-attributes</c> and counts
    /// namespace declarations among them, even though Section 11.3 projects no path for one.
    /// </summary>
    [TestCase("<a x=\"1\" y=\"2\"/>", 2, false)]
    [TestCase("<a x=\"1\" y=\"2\"/>", 1, true)]
    [TestCase("<a xmlns:p=\"urn:p\" x=\"1\"/>", 2, false)]
    [TestCase("<a xmlns:p=\"urn:p\" x=\"1\"/>", 1, true)]
    [TestCase("<a xmlns:p=\"urn:p\"/>", 0, true)]
    public void AttributesAreBoundedPerElement(string document, int maxAttributes, bool crossed) =>
        Crossed(document, ResourceLimits.Defaults with { MaxXmlAttributes = maxAttributes })
            .ShouldBe(crossed ? ResourceBound.MaxXmlAttributes : null);

    /// <summary>
    /// Section 11.1 makes <c>--max-xml-attributes</c> "per source and never cumulative", so many
    /// elements each within the bound do not add up to a crossing.
    /// </summary>
    [Test]
    public void TheAttributeBoundIsNotCumulative() =>
        Crossed(
            "<a x=\"1\"><b y=\"1\"/><c z=\"1\"/><d w=\"1\"/></a>",
            ResourceLimits.Defaults with { MaxXmlAttributes = 1 })
            .ShouldBeNull();

    /// <summary>
    /// Section 16.2 has "every element, attribute, text, comment, and CDATA overlay node" consume
    /// <c>--max-nodes</c>. Unlike the JSON and YAML roots, the document element is itself such a
    /// node, so it is charged too. The tally is judged by the Section 7.3 join rather than here, so
    /// this asserts the charge, not a diagnostic.
    /// </summary>
    [TestCase("<a/>", 1)]
    [TestCase("<a>t</a>", 2)]
    [TestCase("<a x=\"1\"/>", 2)]
    [TestCase("<a x=\"1\" y=\"2\"/>", 3)]
    [TestCase("<a><b/></a>", 2)]
    [TestCase("<a><!--c--></a>", 2)]
    [TestCase("<a><![CDATA[t]]></a>", 2)]
    [TestCase("<a>t1<b/>t2</a>", 4)]
    public void EveryNodeIsCharged(string document, long nodes) => Nodes(document).ShouldBe(nodes);

    /// <summary>
    /// Section 11.1 excludes namespace declarations from the projection, so one is not an overlay
    /// node and does not consume <c>--max-nodes</c>, even though it does consume the attribute
    /// bound.
    /// </summary>
    [Test]
    public void ANamespaceDeclarationIsNotAnOverlayNode() =>
        Nodes("<a xmlns:p=\"urn:p\"/>").ShouldBe(1);

    /// <summary>
    /// Section 11.6 coalesces adjacent text into one logical run, and Section 16.2 charges nodes,
    /// so a run is one node however many segments the parser reported.
    /// </summary>
    [Test]
    public void ACoalescedRunIsChargedOnce() =>
        Nodes("<a><![CDATA[x]]><![CDATA[y]]></a>").ShouldBe(2);

    // Section 11.2 -- the supported subset.

    /// <summary>
    /// Section 11.2 supports "one document element". A stream with none and a stream with two are
    /// both outside it.
    /// </summary>
    [TestCase("")]
    [TestCase("<a/><b/>")]
    [TestCase("<a><b></a>")]
    [TestCase("<a>")]
    public void ADocumentIsExactlyOneDocumentElement(string document) =>
        Refusal(document).Code.ShouldBe("PARSE001");

    /// <summary>
    /// Section 11.2: an encoding name in the declaration that disagrees with the encoding Section
    /// 7.4 selected is a blocking error. It describes a different document from the one being read.
    /// </summary>
    /// <remarks>
    /// The code is <c>PARSE002</c>, not <c>XML002</c>. Section 11.2 calls this "a blocking XML
    /// error", but Appendix B assigns "XML declaration encoding inconsistent with decoded input"
    /// to <c>PARSE002</c>, excludes "byte-encoding disagreement" from <c>XML002</c>, and states
    /// the split a third time in its disambiguation list.
    /// </remarks>
    [TestCase("<?xml version=\"1.0\" encoding=\"windows-1251\"?><a/>", SourceEncoding.Utf8)]
    [TestCase("<?xml version=\"1.0\" encoding=\"UTF-16\"?><a/>", SourceEncoding.Utf8)]
    [TestCase("<?xml version=\"1.0\" encoding=\"utf-8\"?><a/>", SourceEncoding.Utf16LittleEndian)]
    [TestCase("<?xml version=\"1.0\" encoding=\"UTF-16BE\"?><a/>", SourceEncoding.Utf16LittleEndian)]
    public void ADisagreeingEncodingDeclarationIsBlocking(
        string document, SourceEncoding encoding) =>
        Refusal(document, encoding).Code.ShouldBe("PARSE002");

    /// <summary>
    /// The refusal names the declaration, not the reader's token. An XML declaration may be
    /// preceded by nothing, so after Section 7.4 removes any byte-order mark its first scalar is
    /// line 1, column 1 -- the same construct-start convention the <c>XML001</c> for a document
    /// type declaration follows.
    /// </summary>
    [Test]
    public void ADisagreeingEncodingDeclarationNamesItsPosition()
    {
        var refusal = Refusal(
            "<?xml version=\"1.0\" encoding=\"windows-1251\"?><a/>", SourceEncoding.Utf8);

        refusal.Line.ShouldBe(1);
        refusal.Column.ShouldBe(1);
        refusal.Spec.ShouldBe("\u00A711.2");
    }

    /// <summary>
    /// A declaration that agrees, or names no encoding at all, decides nothing. A byte-order mark
    /// distinguishes the two UTF-16 orders and the declaration need not, so plain <c>UTF-16</c>
    /// agrees with either.
    /// </summary>
    [TestCase("<?xml version=\"1.0\" encoding=\"utf-8\"?><a/>", SourceEncoding.Utf8)]
    [TestCase("<?xml version=\"1.0\" encoding=\"UTF-8\"?><a/>", SourceEncoding.Utf8)]
    [TestCase("<?xml version=\"1.0\"?><a/>", SourceEncoding.Utf8)]
    [TestCase("<?xml version=\"1.0\" encoding=\"UTF-16\"?><a/>", SourceEncoding.Utf16LittleEndian)]
    [TestCase("<?xml version=\"1.0\" encoding=\"UTF-16\"?><a/>", SourceEncoding.Utf16BigEndian)]
    [TestCase("<?xml version=\"1.0\" encoding=\"utf-16le\"?><a/>", SourceEncoding.Utf16LittleEndian)]
    public void AnAgreeingEncodingDeclarationIsAccepted(string document, SourceEncoding encoding)
    {
        var buffer = new DiagnosticBuffer();

        Read(document, buffer, encoding: encoding).ShouldNotBeNull();
        buffer.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 11.2: "the XML declaration is not retained". It contributes no path and no node.
    /// </summary>
    [Test]
    public void TheDeclarationIsNotRetained()
    {
        Paths("<?xml version=\"1.0\" encoding=\"utf-8\"?><a>t</a>").ShouldBe(["a=t"]);
        Nodes("<?xml version=\"1.0\" encoding=\"utf-8\"?><a>t</a>").ShouldBe(2);
    }

    /// <summary>
    /// Section 11.2: processing instructions "are discarded with a summarized warning". Summarized
    /// means one warning for the document, not one per instruction, which is also what
    /// <c>WARN006</c>'s "once per input document" cardinality requires.
    /// </summary>
    [Test]
    public void ProcessingInstructionsAreDiscardedWithOneWarning()
    {
        var buffer = new DiagnosticBuffer();

        Read("<a><?one x?>t<?two y?><?three z?></a>", buffer).ShouldNotBeNull();

        var warning = buffer.Drain().ShouldHaveSingleItem();

        warning.Code.ShouldBe("WARN006");
        warning.Severity.ShouldBe(DiagnosticSeverity.Warning);
    }

    /// <summary>
    /// A discarded instruction contributes no path, and does not divide the text it sits between:
    /// Section 11.6 coalesces adjacent text, and an instruction that is not retained is not a node
    /// between them.
    /// </summary>
    [Test]
    public void ADiscardedInstructionContributesNoPath()
    {
        var buffer = new DiagnosticBuffer();
        var node = Read("<a>t1<?pi x?>t2</a>", buffer).ShouldNotBeNull();
        var result = ImmutableArray.CreateBuilder<string>();

        buffer.Drain().Select(diagnostic => diagnostic.Code).ShouldBe(["WARN006"]);
        Walk(node, string.Empty, result);

        result.ShouldBe(["a=t1t2"]);
    }

    /// <summary>
    /// A document with no instructions earns no warning, so the warning reports something that
    /// happened rather than something that might have.
    /// </summary>
    [Test]
    public void ADocumentWithoutInstructionsEarnsNoWarning()
    {
        var buffer = new DiagnosticBuffer();

        Read("<a>t</a>", buffer).ShouldNotBeNull();
        buffer.Drain().ShouldBeEmpty();
    }

    // Section 11.3 and Section 11.4 -- canonical addresses.

    /// <summary>
    /// Section 11.3's worked example, asserted in the spelling the specification writes it in:
    /// <c>&lt;a xmlns:p="urn:p"&gt;text&lt;p:b x="1"/&gt;&lt;b&gt;two&lt;/b&gt;&lt;/a&gt;</c>
    /// projects <c>a.#0</c>, <c>a.#1.Q{urn:p}b.@x</c>, and <c>a.#2.b</c>.
    /// </summary>
    [Test]
    public void MixedContentProjectsTheSectionElevenThreeExample() =>
        Paths("<a xmlns:p=\"urn:p\">text<p:b x=\"1\"/><b>two</b></a>")
            .ShouldBe(["a.#0=text", "a.#1.Q{urn:p}b.@x=1", "a.#2.b=two"]);

    /// <summary>
    /// Section 11.3: text before, between, and after child elements each take their own content
    /// token, and the tokens number across every kind of child.
    /// </summary>
    [Test]
    public void MixedContentNumbersEveryContentNode() =>
        Paths("<a>text1<b x=\"1\"/>text2</a>")
            .ShouldBe(["a.#0=text1", "a.#1.b.@x=1", "a.#2=text2"]);

    /// <summary>
    /// Section 11.4: "attribute and child-element names therefore never collide", because an
    /// attribute carries its <c>@</c> marker and a mixed element's child sits under a content
    /// token.
    /// </summary>
    [Test]
    public void AnAttributeAndAChildOfTheSameNameDoNotCollide() =>
        Paths("<a x=\"attr\">t<x>child</x></a>")
            .ShouldBe(["a.@x=attr", "a.#0=t", "a.#1.x=child"]);

    /// <summary>
    /// Section 11.4 assigns content-token ordering values "across all child elements, text, CDATA,
    /// and comments, including element-only parents", so a comment in
    /// <c>&lt;a&gt;&lt;b/&gt;&lt;!--c--&gt;&lt;d/&gt;&lt;/a&gt;</c> "is therefore addressed as
    /// <c>a.#1</c>" -- which is only true if it spent that value.
    /// </summary>
    [Test]
    public void ACommentSpendsAnOrderingValue() =>
        Paths("<a>t0<!--c-->t2</a>").ShouldBe(["a.#0=t0", "a.#2=t2"]);

    /// <summary>
    /// Section 11.4: "element-only children retain ordinary element-name addressing". A comment
    /// among them spends a value without turning its siblings into content tokens.
    /// </summary>
    [Test]
    public void ElementOnlyChildrenKeepNameAddressing() =>
        Paths("<a><b>1</b><!--c--><d>2</d></a>").ShouldBe(["a.b=1", "a.d=2"]);

    /// <summary>
    /// Section 11.4: for element-only repeated children the canonical child paths are
    /// <c>a.b.0</c> and <c>a.b.1</c>, "using the <c>a.b</c> sequence path's high-water allocator".
    /// </summary>
    [Test]
    public void RepeatedElementOnlyChildrenFormASequence() =>
        Paths("<a><b>1</b><b>2</b></a>").ShouldBe(["a.b.0=1", "a.b.1=2"]);

    /// <summary>
    /// Section 11.4: "a singleton <c>&lt;b&gt;</c> is addressed as <c>a.b</c>". Promotion to a
    /// sequence is what a repeat causes, so an element that repeats nothing must not be promoted.
    /// </summary>
    [Test]
    public void ASingletonChildIsNotASequence() =>
        Paths("<a><b>1</b><c>2</c></a>").ShouldBe(["a.b=1", "a.c=2"]);

    /// <summary>
    /// A sequence forms per name, so repeated and singleton children of one parent each get the
    /// address their own count earns.
    /// </summary>
    [Test]
    public void SequencesFormPerChildName() =>
        Paths("<a><b>1</b><c>2</c><b>3</b></a>").ShouldBe(["a.b.0=1", "a.b.1=3", "a.c=2"]);

    /// <summary>
    /// Section 11.4: "if the merged element is mixed, every content node uses its <c>#n</c>
    /// wrapper". Repeated names inside a mixed element are already distinguished by their content
    /// tokens, so they form no sequence.
    /// </summary>
    [Test]
    public void RepeatedChildrenOfAMixedElementUseContentTokens() =>
        Paths("<a>t<b>1</b><b>2</b></a>")
            .ShouldBe(["a.#0=t", "a.#1.b=1", "a.#2.b=2"]);

    /// <summary>
    /// Section 11.4: "an element with no child elements and exactly one non-comment text or CDATA
    /// node also exposes that scalar at the element path".
    /// </summary>
    [TestCase("<a>two</a>", "a=two")]
    [TestCase("<a><![CDATA[two]]></a>", "a=two")]
    [TestCase("<a><!--c-->two</a>", "a=two")]
    public void AnElementWithOneTextNodeOwnsItsScalar(string document, string path) =>
        Paths(document).ShouldBe([path]);

    /// <summary>
    /// The element-path scalar coexists with the element's attributes: Section 11.4 gives an
    /// attribute its own scalar at its own path, and Section 4.4 lets one node carry a payload and
    /// children at once.
    /// </summary>
    [Test]
    public void AnElementScalarCoexistsWithItsAttributes() =>
        Paths("<a x=\"1\" y=\"2\">two</a>").ShouldBe(["a=two", "a.@x=1", "a.@y=2"]);

    /// <summary>
    /// Section 11.4: "every other element has no scalar payload at its element path". An element
    /// with no content at all is Section 4.4's explicit mapping presence rather than a scalar.
    /// </summary>
    [TestCase("<a/>")]
    [TestCase("<a></a>")]
    public void AnEmptyElementIsAShapeRatherThanAScalar(string document) =>
        Paths(document).ShouldBe(["a={}"]);

    /// <summary>
    /// An element whose only children are elements has no scalar of its own, so it contributes only
    /// their paths.
    /// </summary>
    [Test]
    public void AnElementWithChildrenOwnsNoScalar() =>
        Paths("<a><b>1</b></a>").ShouldBe(["a.b=1"]);

    /// <summary>
    /// Section 11.4 spells a namespaced element <c>Q{uri}local</c> and an unqualified one by its
    /// name alone.
    /// </summary>
    [Test]
    public void AQualifiedElementCarriesItsNamespaceUri()
    {
        Name("<a/>").ShouldBe(new OrdinaryPart([new LiteralToken("a")]));
        Name("<p:a xmlns:p=\"urn:p\"/>")
            .ShouldBe(new QualifiedElementPart("urn:p", [new LiteralToken("a")]));
        Name("<a xmlns=\"urn:d\"/>")
            .ShouldBe(new QualifiedElementPart("urn:d", [new LiteralToken("a")]));
    }

    /// <summary>
    /// Section 11.8 places "exact prefix choice when several prefixes identify the same namespace
    /// URI" outside the preservation contract, so the URI decides the name and the prefix does not.
    /// </summary>
    [Test]
    public void TheNamespaceUriDecidesTheNameRatherThanThePrefix() =>
        Paths(
            "<a xmlns:p=\"urn:x\" xmlns:q=\"urn:x\"><p:b>1</p:b><q:b>2</q:b></a>")
            .ShouldBe(["a.Q{urn:x}b.0=1", "a.Q{urn:x}b.1=2"]);

    /// <summary>
    /// An unprefixed attribute is in no namespace even under a default namespace declaration, which
    /// is what keeps <c>@x</c> spelled plainly while a prefixed one carries its URI.
    /// </summary>
    [Test]
    public void AnAttributeCarriesANamespaceOnlyWhenItIsPrefixed() =>
        Paths("<a xmlns=\"urn:d\" xmlns:p=\"urn:p\" x=\"1\" p:y=\"2\"/>")
            .ShouldBe(["Q{urn:d}a.@x=1", "Q{urn:d}a.@Q{urn:p}y=2"]);

    /// <summary>
    /// Section 11.2 keeps <c>xml:space</c> in the supported subset, and it is an ordinary attribute
    /// in the reserved XML namespace.
    /// </summary>
    [Test]
    public void XmlSpaceIsAnOrdinaryQualifiedAttribute() =>
        Paths("<a xml:space=\"preserve\"> x </a>")
            .ShouldBe(["a= x ", "a.@Q{http://www.w3.org/XML/1998/namespace}space=preserve"]);

    /// <summary>
    /// Section 11.3 projects no path for a namespace declaration: Section 11.8 places "exact
    /// namespace declaration placement" outside the preservation contract, and the URI already
    /// reaches the model through every name that uses it.
    /// </summary>
    [Test]
    public void ANamespaceDeclarationProjectsNoPath() =>
        Paths("<a xmlns:p=\"urn:p\" xmlns=\"urn:d\">t</a>").ShouldBe(["Q{urn:d}a=t"]);

    // Section 11.6 -- CDATA.

    /// <summary>
    /// Section 11.6: "adjacent CDATA segments created solely by safe output splitting are coalesced
    /// into one logical CDATA run. Adjacent ordinary text is coalesced separately."
    /// </summary>
    [TestCase("<a><![CDATA[x]]><![CDATA[y]]></a>", "a=xy")]
    [TestCase("<a>x&amp;y</a>", "a=x&y")]
    public void AdjacentRunsOfOneKindAreCoalesced(string document, string path) =>
        Paths(document).ShouldBe([path]);

    /// <summary>
    /// Section 11.6: "CDATA and ordinary text are not coalesced with each other." Two runs of
    /// different kinds are two content nodes, which makes the element mixed.
    /// </summary>
    [Test]
    public void TextAndCDataAreNotCoalescedWithEachOther() =>
        Paths("<a>t<![CDATA[c]]></a>").ShouldBe(["a.#0=t", "a.#1=c"]);

    /// <summary>
    /// A comment sits between two text nodes as a node of its own, so it ends the run before it.
    /// Coalescing across it would move text past a node the specification orders it against.
    /// </summary>
    [Test]
    public void ACommentEndsATextRun() =>
        Paths("<a>t1<!--c-->t2</a>").ShouldBe(["a.#0=t1", "a.#2=t2"]);

    /// <summary>
    /// A child element ends the run before it for the same reason, and the run after it starts a
    /// new content token.
    /// </summary>
    [Test]
    public void AChildElementEndsATextRun() =>
        Paths("<a>t1<b/>t2</a>").ShouldBe(["a.#0=t1", "a.#1.b={}", "a.#2=t2"]);

    // Section 11.7 -- whitespace.

    /// <summary>
    /// Section 11.7: "the default XML input mode is <c>PreserveWhitespace</c>", and that option
    /// "retains every text node". Formatting indentation is therefore content, and an indented
    /// element is mixed -- which is exactly what <c>NormalizeFormattingWhitespace</c> exists to
    /// opt out of. See <c>KNOWN-LIMITS.md</c>: this preview declines that option.
    /// </summary>
    [Test]
    public void FormattingWhitespaceIsRetainedByDefault() =>
        Paths("<a>\n  <b>1</b>\n</a>")
            .ShouldBe(["a.#0=\n  ", "a.#1.b=1", "a.#2=\n"]);

    /// <summary>
    /// Whitespace inside an element that holds nothing else is its text, not indentation, and the
    /// default mode keeps it verbatim.
    /// </summary>
    [Test]
    public void WhitespaceOnlyContentIsPreserved() => Paths("<a>  </a>").ShouldBe(["a=  "]);

    /// <summary>
    /// Whitespace outside the document element belongs to no element and contributes nothing.
    /// </summary>
    [Test]
    public void WhitespaceOutsideTheDocumentElementContributesNothing()
    {
        Paths("\n<a>t</a>\n").ShouldBe(["a=t"]);
        Nodes("\n<a>t</a>\n").ShouldBe(2);
    }

    /// <summary>
    /// A comment outside the document element likewise belongs to no element, so it spends no
    /// ordering value and charges no node.
    /// </summary>
    [Test]
    public void ACommentOutsideTheDocumentElementContributesNothing()
    {
        Paths("<!--top--><a>t</a><!--tail-->").ShouldBe(["a=t"]);
        Nodes("<!--top--><a>t</a><!--tail-->").ShouldBe(2);
    }

    // Diagnostics.

    /// <summary>
    /// Section 22 counts lines and columns from one. The host parser reports a refusal it makes
    /// before reading any content at line zero, which is not a position this tool may report.
    /// </summary>
    [Test]
    public void ARefusalIsReportedAtAPositionSectionTwentyTwoAdmits()
    {
        foreach (var document in new[] { "", "<!DOCTYPE a><a/>", "<a>" })
        {
            var diagnostic = Refusal(document);

            diagnostic.Line.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(1);
            diagnostic.Column.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(1);
        }
    }

    /// <summary>
    /// A parse failure reports the position once. The host parser repeats it inside its own message,
    /// and two spellings of one position invite a reader to trust the wrong one.
    /// </summary>
    [Test]
    public void AParseFailureReportsItsPositionOnlyOnce()
    {
        var diagnostic = Refusal("<a/><b/>");

        diagnostic.Line.ShouldBe(1);
        diagnostic.Message.ShouldNotContain("position", Case.Sensitive);
        diagnostic.Message.ShouldNotContain("Line ", Case.Sensitive);
    }

    /// <summary>
    /// A message the host parser did not append a position trailer to must survive intact, so the
    /// trailer is removed by recognizing it rather than by trimming whatever is at the end.
    /// </summary>
    [Test]
    public void AMessageWithoutATrailerIsLeftAlone() =>
        Refusal("").Message.ShouldContain("Root element", Case.Sensitive);

    /// <summary>
    /// Every refusal names the clause it enforces, so a reader of the diagnostic stream can reach
    /// the rule from the failure.
    /// </summary>
    [TestCase("<!DOCTYPE a><a/>", "\u00A711.1")]
    [TestCase("<a/><b/>", "\u00A711.2")]
    public void ARefusalNamesItsClause(string document, string anchor) =>
        Refusal(document).Spec.ShouldBe(anchor);

    /// <summary>
    /// Section 11.1 has decoded character data "still consume <c>--max-nodes</c>,
    /// <c>--max-comments</c>, and <c>--max-comment-bytes</c> exactly as other formats do", and
    /// Section 23 lists comments among what consumes the corresponding global budget. This preview
    /// retains no comment, but the bound counts comments read, not comments kept -- otherwise the
    /// same document costs different budget depending on the format that spells it.
    /// </summary>
    /// <param name="document">The document to read.</param>
    /// <param name="comments">The comments it should charge.</param>
    /// <param name="bytes">The decoded comment bytes it should charge.</param>
    [TestCase("<a>t</a>", 0, 0)]
    [TestCase("<a>t<!--xy--></a>", 1, 2)]
    [TestCase("<a><!--xy--><!--z--></a>", 2, 3)]
    [TestCase("<!--ab--><a/>", 1, 2)]
    [TestCase("<a/><!--ab-->", 1, 2)]
    [TestCase("<a><!--\U0001F600--></a>", 1, 4)]
    public void ACommentConsumesTheCommentBudget(string document, int comments, int bytes)
    {
        var tally = Charged(document);

        tally.Comments.ShouldBe(comments);
        tally.CommentBytes.ShouldBe(bytes);
    }

    /// <summary>
    /// Section 11.7's default mode: "<c>PreserveWhitespace</c> retains every text node."
    /// </summary>
    [Test]
    public void EveryTextNodeSurvivesByDefault() =>
        Paths("<a>\n  <b>1</b>\n</a>").ShouldBe(["a.#0=\n  ", "a.#1.b=1", "a.#2=\n"]);

    /// <summary>
    /// Section 11.7 discards "whitespace-only text between element children" in the normalizing
    /// mode, which leaves an element-only parent addressed by name.
    /// </summary>
    [Test]
    public void FormattingWhitespaceIsDiscardedWhenNormalizing()
    {
        Normalized("<a>\n  <b>1</b>\n</a>", out var warnings).ShouldBe(["a.b=1"]);

        warnings.ShouldBe(["WARN007"]);
    }

    /// <summary>
    /// Section 11.7 preserves "whitespace in mixed content", so an element holding any
    /// non-whitespace text keeps every run it holds -- the indentation around a child element is
    /// part of what mixed content says.
    /// </summary>
    [Test]
    public void MixedContentKeepsItsWhitespaceWhenNormalizing()
    {
        Normalized("<a>t\n  <b>1</b>\n</a>", out var warnings)
            .ShouldBe(["a.#0=t\n  ", "a.#1.b=1", "a.#2=\n"]);

        warnings.ShouldBeEmpty();
    }

    /// <summary>
    /// Section 11.7 preserves "whitespace under <c>xml:space="preserve"</c>", including in the
    /// subtree the attribute governs. The attribute itself is an ordinary Section 11.3 attribute
    /// and is projected like any other.
    /// </summary>
    /// <param name="document">The document to read.</param>
    [TestCase("<a xml:space=\"preserve\">\n  <b>1</b>\n</a>")]
    [TestCase("<o xml:space=\"preserve\"><a>\n  <b>1</b>\n</a></o>")]
    public void PreservedWhitespaceSurvivesNormalizing(string document)
    {
        var paths = Normalized(document, out var warnings);

        paths.Where(path => path.Contains("#0", StringComparison.Ordinal)).ShouldNotBeEmpty();
        warnings.ShouldBeEmpty();
    }

    /// <summary>
    /// Section 11.7 discards whitespace "between element children", so an element with no element
    /// children keeps its text: <c>&lt;a&gt; &lt;/a&gt;</c> is a scalar whose value is a space,
    /// and no writer's indentation produced it.
    /// </summary>
    [Test]
    public void WhitespaceWithNoElementSiblingSurvivesNormalizing()
    {
        Normalized("<a>\n  <b> </b>\n</a>", out var warnings).ShouldBe(["a.b= "]);

        warnings.ShouldBe(["WARN007"]);
    }

    /// <summary>
    /// A whitespace-only CDATA section is not formatting indentation. An indenting writer emits a
    /// text node, so Section 11.6's distinct node kind is the evidence that a person wrote it.
    /// </summary>
    [Test]
    public void WhitespaceOnlyCDataSurvivesNormalizing()
    {
        Normalized("<a><![CDATA[ ]]><b>1</b></a>", out var warnings)
            .ShouldBe(["a.#0= ", "a.#1.b=1"]);

        warnings.ShouldBeEmpty();
    }

    /// <summary>
    /// Section 11.7 emits "one warning per input document when whitespace is discarded", so a
    /// document the mode changes nothing about is not reported.
    /// </summary>
    [Test]
    public void NormalizingWarnsOnlyWhenSomethingWasDiscarded()
    {
        Normalized("<a><b>1</b><c>2</c></a>", out var warnings).ShouldBe(["a.b=1", "a.c=2"]);

        warnings.ShouldBeEmpty();
    }

    /// <summary>
    /// A run that looks like indentation is still mixed-content whitespace when its element holds
    /// any non-whitespace text, and Section 11.7 preserves it. The decision is a property of the
    /// element, not of the run: the trailing newline of <c>&lt;b&gt;t&lt;c/&gt;\n&lt;/b&gt;</c>
    /// survives while the identical run around <c>&lt;b&gt;</c> itself does not.
    /// </summary>
    [Test]
    public void IndentationInsideMixedContentSurvivesNormalizing() =>
        Normalized("<a>\n  <b>t<c/>\n  </b>\n</a>", out _)
            .ShouldBe(["a.b.#0=t", "a.b.#1.c={}", "a.b.#2=\n  "]);

    /// <summary>
    /// Section 11.4 promotes repeated same-name children to a sequence, and discarding the
    /// formatting whitespace between them is what leaves them adjacent element-only children.
    /// </summary>
    [Test]
    public void RepeatedChildrenPromoteAfterNormalizing() =>
        Normalized("<a>\n  <b>1</b>\n  <b>2</b>\n</a>", out _).ShouldBe(["a.b.0=1", "a.b.1=2"]);
}
