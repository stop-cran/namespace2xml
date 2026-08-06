using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Inputs;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 8 tests for reading a namespace profile into a Section 4.2 overlay.
/// </summary>
/// <remarks>
/// Every expectation here is authored from the specification clause named in the test, never from
/// what the reader currently produces.
/// </remarks>
[TestFixture]
public class NamespaceProfileReaderTests
{
    private static ImmutableArray<NamespaceRecord> Records(string document) =>
    [
        .. document
            .Split('\n')
            .Select((line, index) => NamespaceRecordClassifier.Classify(line, index + 1)),
    ];

    private static ProfileContribution Read(string document, DiagnosticBuffer diagnostics) =>
        NamespaceProfileReader.Read(Records(document), 3, ProfileSource.OfFile("p.txt"), diagnostics);

    private static ProfileContribution Read(string document) =>
        Read(document, new DiagnosticBuffer());

    private static OrdinaryPart Ordinary(string text) => new([new LiteralToken(text)]);

    private static OverlayNode Descend(OverlayNode node, params string[] path) =>
        path.Aggregate(node, (current, step) => current.Children[Ordinary(step)]);

    private static ImmutableArray<Diagnostic> Diagnose(string document)
    {
        var buffer = new DiagnosticBuffer();
        Read(document, buffer);

        return buffer.Drain();
    }

    // Section 8.1 rule 4, Section 4.2: an entry contributes a scalar payload at its path.

    [Test]
    public void AnEntryContributesItsValueAtItsPath() =>
        Descend(Read("a.b=1").Overlay, "a", "b").Payload!.ToCanonicalText().ShouldBe("1");

    /// <summary>
    /// Section 4.3 makes every namespace scalar untyped until inference runs at step 18, so the
    /// reader must not recognize an integer, a boolean, or a null it happens to see.
    /// </summary>
    [Test]
    public void EveryNamespaceScalarStartsUntyped()
    {
        var contribution = Read("a=1\nb=true\nc=1.5\nd=null");

        foreach (var name in new[] { "a", "b", "c", "d" })
        {
            Descend(contribution.Overlay, name).Payload!.Kind.ShouldBe(ScalarKind.UntypedString);
        }
    }

    /// <summary>Section 8.1: "Values may be empty."</summary>
    [Test]
    public void AValueMayBeEmpty() =>
        Descend(Read("a=").Overlay, "a").Payload!.ToCanonicalText().ShouldBe(string.Empty);

    /// <summary>Section 8.1: "The first separating `=` divides the name from the value."</summary>
    [Test]
    public void OnlyTheFirstSeparatingEqualsDividesNameFromValue() =>
        Descend(Read("a=b=c").Overlay, "a").Payload!.ToCanonicalText().ShouldBe("b=c");

    /// <summary>
    /// Section 8.1: "every other record is not trimmed: all characters before the first separating
    /// `=` are the name and all characters after it are the value, including leading and trailing
    /// spaces and tabs."
    /// </summary>
    [Test]
    public void AnEntryIsNotTrimmed() =>
        Read(" a = 1 ").Overlay.Children[Ordinary(" a ")].Payload!.ToCanonicalText().ShouldBe(" 1 ");

    /// <summary>Section 8.1 rule 1.</summary>
    [Test]
    public void AnEmptyOrBlankRecordContributesNothing() =>
        Read("\n   \n\t\na=1").Overlay.Children.Count.ShouldBe(1);

    /// <summary>Section 8.1 rule 5, reported at the record's first non-blank column.</summary>
    [Test]
    public void ARecordWithoutASeparatingEqualsIsParse001()
    {
        var occurrence = Diagnose("a.b").ShouldHaveSingleItem();

        occurrence.Code.ShouldBe("PARSE001");
        occurrence.Source.ShouldBe("p.txt");
        occurrence.Line.ShouldBe(1);
        occurrence.Column.ShouldBe(1);
    }

    [Test]
    public void AMalformedRecordContributesNothing() =>
        Read("a.b").Overlay.Children.ShouldBeEmpty();

    // Section 8.5: comment binding.

    [Test]
    public void ARunOfCommentsBindsToTheFollowingEntry()
    {
        var contribution = Read("#one\n#two\na=1\nb=2");

        Descend(contribution.Overlay, "a").Comments.Select(comment => comment.Text)
            .ShouldBe(["one", "two"]);
        Descend(contribution.Overlay, "b").Comments.ShouldBeEmpty();
    }

    /// <summary>Section 8.1 rule 2: "preceding spaces/tabs are not comment text".</summary>
    [Test]
    public void LeadingSpacesAreNotCommentText() =>
        Descend(Read("  \t#one\na=1").Overlay, "a").Comments.ShouldHaveSingleItem()
            .Text.ShouldBe("one");

    /// <summary>
    /// Section 8.5: "Trailing comments with no following entry remain document-trailing comments."
    /// </summary>
    [Test]
    public void TrailingCommentsWithNoFollowingEntryAreDocumentTrailing()
    {
        var contribution = Read("a=1\n#after");

        Descend(contribution.Overlay, "a").Comments.ShouldBeEmpty();
        contribution.TrailingComments.Select(comment => comment.Text).ShouldBe(["after"]);
    }

    /// <summary>
    /// Section 8.5: "Only an entry ends a run of comments." A mask leaves the run open, so comments
    /// on either side of one bind to the same entry rather than the mask silently splitting them.
    /// </summary>
    [Test]
    public void AMaskDoesNotInterruptARunOfComments()
    {
        var contribution = Read("#before\n!z.*\n#after\na=1");

        Descend(contribution.Overlay, "a").Comments.Select(comment => comment.Text)
            .ShouldBe(["before", "after"]);
        contribution.TrailingComments.ShouldBeEmpty();
    }

    /// <summary>Section 8.5, same clause: a Section 8.1 rule 1 record leaves the run open.</summary>
    [Test]
    public void ABlankRecordDoesNotInterruptARunOfComments() =>
        Descend(Read("#before\n\n#after\na=1").Overlay, "a").Comments
            .Select(comment => comment.Text).ShouldBe(["before", "after"]);

    /// <summary>Section 8.5, same clause: a `PARSE001` record leaves the run open.</summary>
    [Test]
    public void AMalformedRecordDoesNotInterruptARunOfComments() =>
        Descend(Read("#before\nbad\n#after\na=1").Overlay, "a").Comments
            .Select(comment => comment.Text).ShouldBe(["before", "after"]);

    /// <summary>Section 4.5 orders bound comments by source order.</summary>
    [Test]
    public void CommentsAreBoundInSourceOrder()
    {
        var comments = Descend(Read("#1\n#2\n#3\na=1").Overlay, "a").Comments;

        comments.Select(comment => comment.Text).ShouldBe(["1", "2", "3"]);
        comments[0].Order.CompareTo(comments[2].Order).ShouldBeLessThan(0);
    }

    /// <summary>
    /// Section 8.5 binds a run to "the following entry's logical qualified path", which is the leaf
    /// the entry names and not any container the entry brings into existence on the way to it.
    /// </summary>
    [Test]
    public void ACommentBindsToTheLeafNotToAnAncestor()
    {
        var contribution = Read("#c\na.b.c=1");

        Descend(contribution.Overlay, "a").Comments.ShouldBeEmpty();
        Descend(contribution.Overlay, "a", "b").Comments.ShouldBeEmpty();
        Descend(contribution.Overlay, "a", "b", "c").Comments.ShouldHaveSingleItem();
    }

    /// <summary>Section 8.1 rule 2: "a record beginning with `\#` is not a comment".</summary>
    [Test]
    public void ARecordBeginningWithAnEscapedNumberSignIsNotAComment()
    {
        var contribution = Read(@"\#a=1");

        contribution.TrailingComments.ShouldBeEmpty();
        Descend(contribution.Overlay, "#a").Payload!.ToCanonicalText().ShouldBe("1");
    }

    // Section 8.6: permanent masks.

    /// <summary>
    /// Section 15.1 separates masks from contributions: a mask is not an entry and creates no node.
    /// </summary>
    [Test]
    public void AMaskIsExtractedAndContributesNoNode()
    {
        var contribution = Read("!a.*\nb=1");

        contribution.Masks.ShouldHaveSingleItem().Pattern.Parts.Length.ShouldBe(2);
        contribution.Overlay.Children.Count.ShouldBe(1);
    }

    /// <summary>
    /// Section 8.6: "The legacy form with an ignored value remains accepted." Section 8.1 decides on
    /// the leading `!` before any `=` is considered, so this is a mask and not an entry.
    /// </summary>
    [Test]
    public void TheLegacyMaskFormWithAnIgnoredValueIsAccepted()
    {
        var contribution = Read("!a.b=ignored");

        contribution.Masks.ShouldHaveSingleItem().Pattern.Parts.Length.ShouldBe(2);
        contribution.Overlay.Children.ShouldBeEmpty();
    }

    /// <summary>Section 8.1 rule 3: "a record beginning with `\!` is not a mask".</summary>
    [Test]
    public void ARecordBeginningWithAnEscapedExclamationMarkIsNotAMask()
    {
        var contribution = Read(@"\!a=1");

        contribution.Masks.ShouldBeEmpty();
        Descend(contribution.Overlay, "!a").Payload!.ToCanonicalText().ShouldBe("1");
    }

    // Section 15.1 step 7: templates and unresolved values leave the concrete overlay.

    [Test]
    public void AWildcardNameBecomesATemplateAndNotAContribution()
    {
        var contribution = Read("a.*.c=1");

        contribution.Templates.ShouldHaveSingleItem().Name.Parts.Length.ShouldBe(3);
        contribution.Overlay.Children.ShouldBeEmpty();
    }

    /// <summary>
    /// Section 4.5: "comments on a wildcard template are cloned onto each generated contribution in
    /// match order", so the run must travel with the template and not fall through to the next
    /// ordinary entry, which would bind it to an unrelated value.
    /// </summary>
    [Test]
    public void ATemplateCarriesItsOwnComments()
    {
        var contribution = Read("#t\na.*=1\nb=2");

        contribution.Templates.ShouldHaveSingleItem()
            .Comments.Select(comment => comment.Text).ShouldBe(["t"]);
        Descend(contribution.Overlay, "b").Comments.ShouldBeEmpty();
        contribution.TrailingComments.ShouldBeEmpty();
    }

    /// <summary>
    /// Section 15.1 substitutes references at step 15, so a reference-bearing value has no payload
    /// at step 5 and cannot be contributed here.
    /// </summary>
    [Test]
    public void AValueWithAReferenceIsDeferredRatherThanContributed()
    {
        var contribution = Read("a=${b}");

        contribution.UnresolvedValues.ShouldHaveSingleItem().Name.Parts.Length.ShouldBe(1);
        contribution.Overlay.Children.ShouldBeEmpty();
    }

    /// <summary>Section 8.3: an escaped `${` is literal text, so nothing is deferred.</summary>
    [Test]
    public void AnEscapedReferenceIsAnOrdinaryValue()
    {
        var contribution = Read(@"a=\${b}");

        contribution.UnresolvedValues.ShouldBeEmpty();
        Descend(contribution.Overlay, "a").Payload!.ToCanonicalText().ShouldBe("${b}");
    }

    /// <summary>
    /// Section 8.4: "Invalid or unterminated reference-looking text is a blocking `REFERENCE001`."
    /// </summary>
    [Test]
    public void AnUnterminatedReferenceIsReference001() =>
        Diagnose("a=${b").ShouldHaveSingleItem().Code.ShouldBe("REFERENCE001");

    /// <summary>
    /// Section 22 requires a column, and Section 8.1 does not trim an entry, so a fault in the value
    /// is located within the whole record: the same value fault must move by exactly the difference
    /// in name length.
    /// </summary>
    [Test]
    public void AValueFaultColumnLocatesTheTextWithinTheWholeRecord()
    {
        var wide = Diagnose("abc=${x").ShouldHaveSingleItem().Column!.Value;
        var narrow = Diagnose("a=${x").ShouldHaveSingleItem().Column!.Value;

        (wide - narrow).ShouldBe(2);
        narrow.ShouldBeGreaterThanOrEqualTo(3);
    }

    /// <summary>
    /// Appendix B assigns the most specific code, so an invalid Section 12 capture in a name is
    /// `WILDCARD001` rather than the `PARSE001` an otherwise malformed name earns, and Section 22's
    /// column names the offending text: the fourth character of `a.*[x`.
    /// </summary>
    [Test]
    public void AnInvalidCaptureInANameIsWildcard001AtItsOwnColumn()
    {
        var occurrence = Diagnose("a.*[x=1").ShouldHaveSingleItem();

        occurrence.Code.ShouldBe("WILDCARD001");
        occurrence.Column.ShouldBe(4);
    }

    /// <summary>Section 8.2: an unescaped `$` is ordinary text in a name; only Section 8.4
    /// interpreted values contain references.</summary>
    [Test]
    public void ADollarSignInANameIsOrdinaryText()
    {
        var contribution = Read("a.${x}.c=1");

        Descend(contribution.Overlay, "a", "${x}", "c").Payload!.ToCanonicalText().ShouldBe("1");
    }

    // Section 5.2: reading must not disturb the order of what it reads.

    /// <summary>
    /// Section 5.2: a new child is a contribution at the child, not at the parent, so `a` keeps the
    /// position line 1 gave it even though line 3 adds to it.
    /// </summary>
    [Test]
    public void AddingAChildDoesNotMoveItsParent() =>
        Read("a.x=1\nb=2\na.y=3").Overlay.OrderedChildren.Select(pair => pair.Key)
            .ShouldBe([Ordinary("a"), Ordinary("b")]);

    /// <summary>
    /// Section 5.2: "the winning contribution determines the final output position of the logical
    /// path", so a later contribution at the same path moves it.
    /// </summary>
    [Test]
    public void ALaterContributionAtTheSamePathMovesIt()
    {
        var contribution = Read("a=1\nb=2\na=3");

        contribution.Overlay.OrderedChildren.Select(pair => pair.Key)
            .ShouldBe([Ordinary("b"), Ordinary("a")]);
        Descend(contribution.Overlay, "a").Payload!.ToCanonicalText().ShouldBe("3");
    }

    /// <summary>
    /// Section 4.2: mapping and scalar are projections of one node, not exclusive kinds, so both
    /// facts survive and the namespace output must be able to emit both.
    /// </summary>
    [Test]
    public void AScalarAndItsDescendantBothSurvive()
    {
        var node = Descend(Read("a=1\na.b=2").Overlay, "a");

        node.Payload!.ToCanonicalText().ShouldBe("1");
        Descend(node, "b").Payload!.ToCanonicalText().ShouldBe("2");
    }

    // Section 24: the diagnostic stream is ordered by the Section 4.7 key of the item concerned,
    // which is observable as emission order and is the only reason to carry a key at all.

    /// <summary>
    /// Section 24 orders by source ordering key, whose most significant part is the Section 4.7 CLI
    /// source ordinal, so a later source's diagnostics follow an earlier source's however the run
    /// happened to reach them.
    /// </summary>
    [Test]
    public void DiagnosticsFromAnEarlierSourceAreEmittedFirst()
    {
        var buffer = new DiagnosticBuffer();

        NamespaceProfileReader.Read(Records("bad"), 5, ProfileSource.OfFile("late.txt"), buffer);
        NamespaceProfileReader.Read(Records("bad"), 2, ProfileSource.OfFile("early.txt"), buffer);

        buffer.Drain().Select(diagnostic => diagnostic.Source)
            .ShouldBe(["early.txt", "late.txt"]);
    }

    /// <summary>
    /// Section 24 orders within a source by the item's key, which for a namespace record is its
    /// line, so a diagnostic raised later is still emitted in line order.
    /// </summary>
    [Test]
    public void DiagnosticsAreEmittedInLineOrderWithinOneSource()
    {
        var buffer = new DiagnosticBuffer();
        var late = ImmutableArray.Create(NamespaceRecordClassifier.Classify("bad", 9));
        var early = ImmutableArray.Create(NamespaceRecordClassifier.Classify("a=${x", 2));

        NamespaceProfileReader.Read(late, 1, ProfileSource.OfFile("p.txt"), buffer);
        NamespaceProfileReader.Read(early, 1, ProfileSource.OfFile("p.txt"), buffer);

        buffer.Drain().Select(diagnostic => diagnostic.Line).ShouldBe([2, 9]);
    }

    /// <summary>
    /// Section 22 scopes <c>PARSE001</c> "once per failing source", so a file with two malformed
    /// records reports one error, not one per record.
    /// </summary>
    /// <remarks>
    /// The retained occurrence is the Section 24 earliest, because a reader shown the second fault
    /// and not the first would start from the wrong place in the file.
    /// </remarks>
    [Test]
    public void TwoMalformedRecordsInOneSourceReportOneParseErrorAtTheEarliest()
    {
        var buffer = new DiagnosticBuffer();
        var records = ImmutableArray.Create(
            NamespaceRecordClassifier.Classify("bad", 2),
            NamespaceRecordClassifier.Classify("worse", 9));

        NamespaceProfileReader.Read(records, 1, ProfileSource.OfFile("p.txt"), buffer);

        var diagnostic = buffer.Drain().ShouldHaveSingleItem();
        diagnostic.Code.ShouldBe("PARSE001");
        diagnostic.Line.ShouldBe(2);
    }

    /// <summary>Two failing sources are two failing sources, so both are reported.</summary>
    [Test]
    public void MalformedRecordsInTwoSourcesReportOneParseErrorEach()
    {
        var buffer = new DiagnosticBuffer();
        var records = ImmutableArray.Create(NamespaceRecordClassifier.Classify("bad", 1));

        NamespaceProfileReader.Read(records, 1, ProfileSource.OfFile("first.txt"), buffer);
        NamespaceProfileReader.Read(records, 2, ProfileSource.OfFile("second.txt"), buffer);

        buffer.Drain().Select(diagnostic => diagnostic.Source)
            .ShouldBe(["first.txt", "second.txt"]);
    }

    [Test]
    public void EveryFaultingRecordIsReported() =>
        Diagnose("bad\na=${x\nb.*[c=1").Select(diagnostic => diagnostic.Line)
            .ShouldBe([1, 2, 3]);
    /// <summary>
    /// Section 12.1: wildcard recognition is "decided before the value is lexed, from the owning
    /// name's captures", so in an entry whose name defines none, "values such as pattern=*.txt
    /// require no escape".
    /// </summary>
    [Test]
    public void AnAsteriskIsLiteralWhereTheNameDefinesNoCaptures()
    {
        var payload = Descend(Read("a.pattern=*.txt").Overlay, "a", "pattern").Payload!;

        payload.ToCanonicalText().ShouldBe("*.txt");
    }

    /// <summary>
    /// Section 12.1: the bracketed form is covered by the same decision, so
    /// <c>path=/opt/x*[0-9]/y</c> "is a glob, not an undefined capture" and not a lexical error.
    /// </summary>
    [Test]
    public void ABracketedAsteriskIsLiteralWhereTheNameDefinesNoCaptures()
    {
        var buffer = new DiagnosticBuffer();
        var contribution = Read("a.path=/opt/x*[0-9]/y", buffer);

        Descend(contribution.Overlay, "a", "path").Payload!.ToCanonicalText()
            .ShouldBe("/opt/x*[0-9]/y");
        buffer.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 12.1: where the name defines an unnamed capture, a bare asterisk in the value
    /// substitutes it, so the value is no longer a literal scalar.
    /// </summary>
    [Test]
    public void AnAsteriskSubstitutesWhereTheNameDefinesAnUnnamedCapture()
    {
        var entry = Read("a.*.pattern=*.txt").Templates.ShouldHaveSingleItem();

        entry.Value.ContainsWildcard.ShouldBeTrue();
        entry.Value.LiteralText.ShouldBeNull();
    }

    /// <summary>
    /// Section 12.1: "Where the name defines explicit captures, a bare '*' in the value is likewise
    /// literal text, because the name defines no unnamed capture for it to substitute; that is what
    /// incompatibility means here, and it is not a mixed-capture error."
    /// </summary>
    [Test]
    public void ABareAsteriskIsLiteralWhereTheNameDefinesExplicitCaptures()
    {
        var buffer = new DiagnosticBuffer();
        var entry = Read("a.*[n].pattern=*.txt", buffer).Templates.ShouldHaveSingleItem();

        entry.Value.LiteralText.ShouldBe("*.txt");
        buffer.Drain().ShouldBeEmpty();
    }
    /// <summary>
    /// Section 22: "Column 1 is the first Unicode scalar value of a line, and each subsequent
    /// scalar advances the column by one, so a character outside the Basic Multilingual Plane
    /// occupies one column and a tab occupies one column."
    /// </summary>
    /// <remarks>
    /// Asserted as an equality between two records rather than as a literal column, because the
    /// clause under test fixes the unit a column is counted in and not where within an
    /// unterminated reference the lexer chooses to point. The two records differ only in which
    /// scalar sits in the name, so Section 22 requires the same column for both. U+1D11E is two
    /// UTF-16 code units and 'x' is one, so a column counted in code units differs by one.
    /// </remarks>
    [Test]
    public void AnAstralScalarInANameOccupiesOneColumn()
    {
        var astral = Diagnose("a\U0001D11Eb=${c").ShouldHaveSingleItem();
        var basicPlane = Diagnose("axb=${c").ShouldHaveSingleItem();

        astral.Code.ShouldBe("REFERENCE001");
        astral.Line.ShouldBe(1);
        astral.Column.ShouldBe(basicPlane.Column);
    }

    /// <summary>Section 22, for a scalar preceding the fault within the value.</summary>
    [Test]
    public void AnAstralScalarBeforeAValueFaultOccupiesOneColumn()
    {
        var astral = Diagnose("a=\U0001D11E${c").ShouldHaveSingleItem();
        var basicPlane = Diagnose("a=x${c").ShouldHaveSingleItem();

        astral.Code.ShouldBe("REFERENCE001");
        astral.Column.ShouldBe(basicPlane.Column);
    }
}
