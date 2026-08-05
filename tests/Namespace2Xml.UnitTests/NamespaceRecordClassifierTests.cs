using Namespace2Xml.Profiles;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// The Section 8.1 classification rules, in the order that section fixes, and the separating
/// <c>=</c> it defines.
/// </summary>
[TestFixture]
public sealed class NamespaceRecordClassifierTests
{
    private static NamespaceRecord Classify(string text) =>
        NamespaceRecordClassifier.Classify(text, line: 1);

    // "1. an empty or space/tab-only record is ignored"

    [Test]
    public void AnEmptyRecordIsIgnored() =>
        Classify(string.Empty).Kind.ShouldBe(NamespaceRecordKind.Ignored);

    [Test]
    public void ARecordOfSpacesAndTabsIsIgnored() =>
        Classify("  \t \t").Kind.ShouldBe(NamespaceRecordKind.Ignored);

    [Test]
    public void OtherWhitespaceDoesNotMakeARecordIgnored()
    {
        // Only spaces and tabs are listed. A no-break space is an ordinary scalar, so this record
        // reaches rule 4 and has no separating "=".
        Classify("\u00a0").Kind.ShouldBe(NamespaceRecordKind.Malformed);
    }

    // "2. if the first non-space/tab character is '#', the record is a comment and preceding
    //  spaces/tabs are not comment text; a record beginning with '\#' is not a comment"

    [Test]
    public void ARecordWhoseFirstNonBlankScalarIsANumberSignIsAComment()
    {
        var record = Classify("# hello");

        record.Kind.ShouldBe(NamespaceRecordKind.Comment);
        record.Comment.ShouldBe("# hello");
    }

    [Test]
    public void LeadingSpacesAndTabsAreNotCommentText()
    {
        var record = Classify("  \t# hello");

        record.Kind.ShouldBe(NamespaceRecordKind.Comment);
        record.Comment.ShouldBe("# hello");
    }

    [Test]
    public void AnEscapedNumberSignDoesNotBeginAComment()
    {
        var record = Classify("\\#a=1");

        record.Kind.ShouldBe(NamespaceRecordKind.Entry);
        record.Name.ShouldBe("\\#a");
        record.Value.ShouldBe("1");
    }

    [Test]
    public void ANumberSignAfterOtherTextIsOrdinary()
    {
        // Section 8.5: a "#" elsewhere in a name or value is an ordinary character.
        var record = Classify("a=x#y");

        record.Kind.ShouldBe(NamespaceRecordKind.Entry);
        record.Value.ShouldBe("x#y");
    }

    [Test]
    public void ACommentIsClassifiedBeforeTheEntryRuleEvenWhenItContainsAnEquals()
    {
        // Rule 2 precedes rule 4, so a comment containing "=" is still a comment.
        Classify("# a=1").Kind.ShouldBe(NamespaceRecordKind.Comment);
    }

    // "3. if the first non-space/tab character is '!', the record is a permanent mask and preceding
    //  spaces/tabs are not part of its pattern; a record beginning with '\!' is not a mask"

    [Test]
    public void ARecordWhoseFirstNonBlankScalarIsAnExclamationMarkIsAMask()
    {
        var record = Classify("!a.b.*");

        record.Kind.ShouldBe(NamespaceRecordKind.Mask);
        record.Pattern.ShouldBe("a.b.*");
    }

    [Test]
    public void LeadingSpacesAndTabsAreNotPartOfAMaskPattern()
    {
        var record = Classify(" \t!a.b");

        record.Kind.ShouldBe(NamespaceRecordKind.Mask);
        record.Pattern.ShouldBe("a.b");
    }

    [Test]
    public void AnEscapedExclamationMarkDoesNotBeginAMask()
    {
        var record = Classify("\\!a=1");

        record.Kind.ShouldBe(NamespaceRecordKind.Entry);
        record.Name.ShouldBe("\\!a");
    }

    [Test]
    public void TheLegacyMaskFormWithAValueIsStillAMask()
    {
        // Section 8.6 keeps "!pattern=ignored". Rule 3 precedes rule 4, so the record is a mask and
        // the ignored value stays in the pattern text for Section 8.6 to discard.
        var record = Classify("!a.b=ignored");

        record.Kind.ShouldBe(NamespaceRecordKind.Mask);
        record.Pattern.ShouldBe("a.b=ignored");
    }

    // "4. otherwise the record must contain a separating '=' and is an ordinary entry"

    [Test]
    public void TheFirstSeparatingEqualsDividesNameFromValue()
    {
        var record = Classify("a.b=x=y");

        record.Kind.ShouldBe(NamespaceRecordKind.Entry);
        record.Name.ShouldBe("a.b");
        record.Value.ShouldBe("x=y");
    }

    [Test]
    public void ValuesMayBeEmpty()
    {
        var record = Classify("a.b=");

        record.Kind.ShouldBe(NamespaceRecordKind.Entry);
        record.Value.ShouldBe(string.Empty);
    }

    [Test]
    public void NeitherNameNorValueIsTrimmed()
    {
        var record = Classify("  a b  =  x y  ");

        record.Name.ShouldBe("  a b  ");
        record.Value.ShouldBe("  x y  ");
    }

    [Test]
    public void AnEscapedEqualsIsNotASeparator()
    {
        var record = Classify("a\\=b=1");

        record.Name.ShouldBe("a\\=b");
        record.Value.ShouldBe("1");
    }

    [Test]
    public void AnEscapedBackslashDoesNotEscapeTheFollowingEquals()
    {
        // "\\" consumes both scalars, so the "=" after it is unescaped and separates.
        var record = Classify("a\\\\=1");

        record.Name.ShouldBe("a\\\\");
        record.Value.ShouldBe("1");
    }

    [Test]
    public void AnEmptyNameStillProducesAnEntry()
    {
        // Section 8.2 rejects the empty name later; classification only locates the separator.
        var record = Classify("=1");

        record.Kind.ShouldBe(NamespaceRecordKind.Entry);
        record.Name.ShouldBe(string.Empty);
    }

    // "5. any remaining record without a separating '=' is PARSE001"

    [Test]
    public void ARecordWithNoEqualsIsMalformed() =>
        Classify("a.b.c").Kind.ShouldBe(NamespaceRecordKind.Malformed);

    [Test]
    public void ARecordWhoseOnlyEqualsIsEscapedIsMalformed() =>
        Classify("a\\=b").Kind.ShouldBe(NamespaceRecordKind.Malformed);

    // "A separating '=' is an unescaped '=' that does not fall inside a Q{...} URI."

    [Test]
    public void AnEqualsInsideAQualifiedElementUriDoesNotSeparate()
    {
        var record = Classify("Q{urn:x?v=1}b=value");

        record.Kind.ShouldBe(NamespaceRecordKind.Entry);
        record.Name.ShouldBe("Q{urn:x?v=1}b");
        record.Value.ShouldBe("value");
    }

    [Test]
    public void AUriMayAppearInAnyComponent()
    {
        var record = Classify("a.#1.Q{urn:p?a=b}c.@x=1");

        record.Name.ShouldBe("a.#1.Q{urn:p?a=b}c.@x");
        record.Value.ShouldBe("1");
    }

    [Test]
    public void AnAttributeMarkerLeavesTheUriMarkerPositionOpen()
    {
        // Section 11.4 spells a qualified attribute "@Q{urn:p}x", so "@" must not close the
        // position at which "Q{" is recognized.
        var record = Classify("a.@Q{urn:p?v=1}x=2");

        record.Name.ShouldBe("a.@Q{urn:p?v=1}x");
        record.Value.ShouldBe("2");
    }

    [Test]
    public void AnEscapedQIsAnOrdinaryComponentAndDoesNotOpenAUri()
    {
        // Section 8.2: "\Q{urn:x}name" is an ordinary literal name part, so the "=" inside it is
        // an ordinary separating "=".
        var record = Classify("\\Q{urn:x?v=1}b=2");

        record.Name.ShouldBe("\\Q{urn:x?v");
        record.Value.ShouldBe("1}b=2");
    }

    [Test]
    public void AQNotFollowedByABraceIsOrdinary()
    {
        var record = Classify("Query=1");

        record.Name.ShouldBe("Query");
        record.Value.ShouldBe("1");
    }

    [Test]
    public void AQBraceAwayFromAComponentStartIsOrdinary()
    {
        // Section 8.2 recognizes the marker only at the start of a name part.
        var record = Classify("aQ{urn:x?v=1}b=2");

        record.Name.ShouldBe("aQ{urn:x?v");
        record.Value.ShouldBe("1}b=2");
    }

    [Test]
    public void AnEscapedClosingBraceDoesNotEndAUri()
    {
        // Section 11.4: inside the URI, "\}" encodes a literal closing brace.
        var record = Classify("Q{urn:x\\}y?v=1}b=2");

        record.Name.ShouldBe("Q{urn:x\\}y?v=1}b");
        record.Value.ShouldBe("2");
    }

    [Test]
    public void AnEscapedBackslashInsideAUriDoesNotEscapeTheClosingBrace()
    {
        var record = Classify("Q{urn:x\\\\}b=2");

        record.Name.ShouldBe("Q{urn:x\\\\}b");
        record.Value.ShouldBe("2");
    }

    [Test]
    public void AnUnterminatedUriSwallowsTheRestOfTheRecordAndLeavesItMalformed()
    {
        // No separating "=" exists outside the URI, so rule 5 applies and the name lexer never has
        // to explain a name that ends in the middle of a URI.
        Classify("Q{urn:x=1").Kind.ShouldBe(NamespaceRecordKind.Malformed);
    }

    [Test]
    public void ADotInsideAUriDoesNotOpenANewComponent()
    {
        // Section 11.4: dots inside Q{...} are part of the URI. If one reopened the marker
        // position, the "Q{" of a nested-looking URI would be misread.
        var record = Classify("Q{urn:a.b.Q{c?v=1}d=2");

        record.Name.ShouldBe("Q{urn:a.b.Q{c?v=1}d");
        record.Value.ShouldBe("2");
    }

    // Totality.

    [Test]
    public void ClassificationIsTotal()
    {
        string[] alphabet = ["a", ".", "=", "\\", "Q", "{", "}", "@", "#", "!", " ", "*", "\u00e9"];
        var random = new Random(20260805);

        for (var i = 0; i < 5000; i++)
        {
            var length = random.Next(8);
            var text = string.Concat(Enumerable.Range(0, length).Select(_ => alphabet[random.Next(alphabet.Length)]));
            var record = NamespaceRecordClassifier.Classify(text, line: 1);

            if (record.Kind == NamespaceRecordKind.Entry)
            {
                // The split must be lossless: name, the separator, and value reconstruct the record.
                (record.Name + "=" + record.Value).ShouldBe(text);
            }
        }
    }
}
