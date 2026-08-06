using System.Collections.Immutable;
using Namespace2Xml.Profiles;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// The Appendix A.2 qualified-name grammar and the Section 8.2 escape and marker rules.
/// </summary>
[TestFixture]
public sealed class QualifiedNameLexerTests
{
    private static QualifiedName Lex(string text)
    {
        var result = QualifiedNameLexer.Lex(text);
        result.Fault.ShouldBeNull();
        return result.Name!;
    }

    private static NameFault LexFault(string text)
    {
        var result = QualifiedNameLexer.Lex(text);
        result.Name.ShouldBeNull();
        return result.Fault!.Value;
    }

    /// <summary>Renders a name unambiguously so a test can state the whole expected structure.</summary>
    private static string Describe(QualifiedName name) =>
        string.Join("|", name.Parts.Select(DescribePart));

    private static string DescribePart(NamePart part) => part switch
    {
        OrdinaryPart ordinary => "ord(" + DescribeTokens(ordinary.Tokens) + ")",
        QualifiedElementPart qualified => "elem(" + qualified.Uri + ";" + DescribeTokens(qualified.Local) + ")",
        AttributePart attribute => "attr(" + DescribePart(attribute.Name) + ")",
        ContentPart content => "content(" + content.Ordinal + ")",
        _ => throw new ArgumentOutOfRangeException(nameof(part)),
    };

    private static string DescribeTokens(ImmutableArray<NameToken> tokens) =>
        string.Concat(tokens.Select(token => token switch
        {
            LiteralToken literal => "'" + literal.Text + "'",
            WildcardToken { CaptureId: null } => "<*>",
            WildcardToken wildcard => "<*" + wildcard.CaptureId + ">",
            _ => throw new ArgumentOutOfRangeException(nameof(tokens)),
        }));

    // "An unescaped '.' separates name parts."

    [Test]
    public void ADelimiterSeparatesParts() =>
        Describe(Lex("a.b.c")).ShouldBe("ord('a')|ord('b')|ord('c')");

    [Test]
    public void ASingleComponentIsAName() =>
        Describe(Lex("a")).ShouldBe("ord('a')");

    [Test]
    public void AnEscapedDelimiterIsALiteralDot() =>
        Describe(Lex("a\\.b")).ShouldBe("ord('a.b')");

    // "A qualified name must not begin or end with an unescaped delimiter or contain consecutive
    //  unescaped delimiters." / "An empty name part is a blocking parse error."

    [Test]
    public void ANameCannotBeginWithADelimiter() =>
        LexFault(".a").Offset.ShouldBe(0);

    [Test]
    public void ANameCannotEndWithADelimiter() =>
        LexFault("a.").Offset.ShouldBe(2);

    [Test]
    public void ANameCannotContainConsecutiveDelimiters() =>
        LexFault("a..b").Offset.ShouldBe(2);

    [Test]
    public void AnEmptyNameIsAFault() =>
        LexFault(string.Empty).Offset.ShouldBe(0);

    // Section 8.2 escapes.

    [Test]
    public void EveryListedNameEscapeYieldsItsLiteral()
    {
        (string Escape, string Literal)[] cases =
        [
            ("\\.", "."),
            ("\\*", "*"),
            ("\\=", "="),
            ("\\#", "#"),
            ("\\!", "!"),
            ("\\$", "$"),
            ("\\@", "@"),
            ("\\}", "}"),
            ("\\Q", "Q"),
            ("\\\\", "\\"),
        ];

        foreach (var (escape, literal) in cases)
        {
            Describe(Lex("x" + escape + "y")).ShouldBe("ord('x" + literal + "y')");
        }
    }

    [Test]
    public void EveryOtherBackslashSequenceIsAFault()
    {
        const string escapable = ".*=#!$@}Q\\";

        // Every ASCII graphic scalar that Section 8.2 does not list, plus the space.
        foreach (var escaped in Enumerable.Range(0x20, 0x5F).Select(code => (char)code))
        {
            if (escapable.Contains(escaped, StringComparison.Ordinal))
            {
                continue;
            }

            QualifiedNameLexer.Lex("x\\" + escaped).Fault.ShouldNotBeNull(
                $"'\\{escaped}' is not a Section 8.2 name escape");
        }
    }

    [Test]
    public void ATrailingBackslashIsAFault() =>
        LexFault("a\\").Offset.ShouldBe(1);

    // "\u{HEX} requires one through six digits and a closing brace. Values above U+10FFFF, values
    //  in the surrogate range U+D800 through U+DFFF, empty digit strings, and malformed or
    //  unterminated forms are PARSE001."

    [Test]
    public void AUnicodeEscapeYieldsItsScalar() =>
        Describe(Lex("a\\u{2E}b")).ShouldBe("ord('a.b')");

    [Test]
    public void AUnicodeEscapeAcceptsEitherCase() =>
        Describe(Lex("\\u{e9}")).ShouldBe("ord('\u00e9')");

    [Test]
    public void AUnicodeEscapeAcceptsSixDigits() =>
        Describe(Lex("\\u{01F600}")).ShouldBe("ord('\U0001F600')");

    [Test]
    public void AUnicodeEscapeRejectsSevenDigits() =>
        LexFault("\\u{0000041}").Offset.ShouldBe(0);

    [Test]
    public void AUnicodeEscapeRejectsAnEmptyDigitString() =>
        LexFault("\\u{}").Offset.ShouldBe(0);

    [Test]
    public void AUnicodeEscapeRejectsAMissingBrace() =>
        LexFault("\\u41").Offset.ShouldBe(0);

    [Test]
    public void AUnicodeEscapeRejectsAnUnterminatedForm() =>
        LexFault("\\u{41").Offset.ShouldBe(0);

    [Test]
    public void AUnicodeEscapeRejectsAValueAboveTheUnicodeRange() =>
        LexFault("\\u{110000}").Offset.ShouldBe(0);

    [Test]
    public void AUnicodeEscapeRejectsASurrogateCodePoint()
    {
        LexFault("\\u{D800}").Offset.ShouldBe(0);
        LexFault("\\u{DFFF}").Offset.ShouldBe(0);
    }

    [Test]
    public void AUnicodeEscapeAcceptsTheScalarsAroundTheSurrogateRange()
    {
        Describe(Lex("\\u{D7FF}")).ShouldBe("ord('\uD7FF')");
        Describe(Lex("\\u{E000}")).ShouldBe("ord('\uE000')");
        Describe(Lex("\\u{10FFFF}")).ShouldBe("ord('\U0010FFFF')");
    }

    [Test]
    public void AUnicodeEscapeIsNotRescanned()
    {
        // Appendix A.6: emitted text is never rescanned, so "\u{2E}" is a literal dot and does not
        // split the name, and "\u{5C}" is a literal backslash that escapes nothing.
        Describe(Lex("a\\u{5C}\\u{2E}b")).ShouldBe("ord('a\\.b')");
    }

    // Wildcards.

    [Test]
    public void AnUnescapedAsteriskIsAWildcard() =>
        Describe(Lex("a.*")).ShouldBe("ord('a')|ord(<*>)");

    [Test]
    public void AWildcardMayBeSurroundedByLiteralText() =>
        Describe(Lex("pre*post")).ShouldBe("ord('pre'<*>'post')");

    [Test]
    public void SeveralWildcardsMayShareAPart() =>
        Describe(Lex("a*b*c")).ShouldBe("ord('a'<*>'b'<*>'c')");

    [Test]
    public void AnExplicitCaptureCarriesItsIdentifier() =>
        Describe(Lex("*[env]")).ShouldBe("ord(<*env>)");

    [Test]
    public void ANumericIdentifierIsValid() =>
        Describe(Lex("*[0]")).ShouldBe("ord(<*0>)");

    [Test]
    public void AnIdentifierAcceptsLettersDigitsUnderscoresAndHyphens() =>
        Describe(Lex("*[a-Z_0-9]")).ShouldBe("ord(<*a-Z_0-9>)");

    [Test]
    public void AnEmptyIdentifierIsAFault() =>
        LexFault("*[]").Offset.ShouldBe(1);

    [Test]
    public void AnIdentifierWithAnIllegalScalarIsAFault() =>
        LexFault("*[a b]").Offset.ShouldBe(1);

    [Test]
    public void AnUnterminatedCaptureIsAFault() =>
        LexFault("*[abc").Offset.ShouldBe(1);

    [Test]
    public void AnEscapedAsteriskIsNotAWildcard()
    {
        var name = Lex("a\\*b");

        Describe(name).ShouldBe("ord('a*b')");
        QualifiedNameLexer.ContainsWildcard(name).ShouldBeFalse();
    }

    [Test]
    public void ContainsWildcardFindsWildcardsInEveryPartKind()
    {
        QualifiedNameLexer.ContainsWildcard(Lex("a.b")).ShouldBeFalse();
        QualifiedNameLexer.ContainsWildcard(Lex("a.*")).ShouldBeTrue();
        QualifiedNameLexer.ContainsWildcard(Lex("a.Q{urn:p}*")).ShouldBeTrue();
        QualifiedNameLexer.ContainsWildcard(Lex("a.@*")).ShouldBeTrue();
        QualifiedNameLexer.ContainsWildcard(Lex("a.@Q{urn:p}*")).ShouldBeTrue();
    }

    // Typed markers, and Section 8.2's rule that recognition commits.

    [Test]
    public void AnAttributeMarkerIntroducesAnAttribute() =>
        Describe(Lex("a.@x")).ShouldBe("ord('a')|attr(ord('x'))");

    [Test]
    public void AQualifiedAttributeIsSupported() =>
        Describe(Lex("a.@Q{urn:p}x")).ShouldBe("ord('a')|attr(elem(urn:p;'x'))");

    [Test]
    public void AContentMarkerIntroducesAContentToken() =>
        Describe(Lex("a.#0")).ShouldBe("ord('a')|content(0)");

    [Test]
    public void AContentOrdinalMayHaveSeveralDigits() =>
        Describe(Lex("#123")).ShouldBe("content(123)");

    /// <summary>
    /// Section 11.4 assigns content-token addresses "using Section 5.4", and Section 5.4 ordering
    /// values are "stable signed 64-bit integer ordering values in the range 0 through
    /// 9,223,372,036,854,775,807".
    /// </summary>
    /// <remarks>
    /// A 32-bit ordinal stops at 2,147,483,647. Every address above that belongs to a document a
    /// high-water allocator can legitimately produce, and rejecting one makes the address
    /// unwritable in a scheme selector or a canonical reference — the model would hold a node no
    /// path could name.
    /// </remarks>
    [TestCase("#2147483648", "content(2147483648)")]
    [TestCase("#9223372036854775807", "content(9223372036854775807)")]
    public void AContentOrdinalSpansTheWholeSignedSixtyFourBitRange(string written, string expected) =>
        Describe(Lex(written)).ShouldBe(expected);

    /// <summary>One past the Section 5.4 maximum is not an ordering value at all.</summary>
    [Test]
    public void AContentOrdinalAboveTheMaximumIsRejected() =>
        LexFault("#9223372036854775808").Message.ShouldContain("9223372036854775807");

    [Test]
    public void TheSpecificationsMixedContentExampleLexes() =>
        Describe(Lex("a.#1.Q{urn:p}b.@x"))
            .ShouldBe("ord('a')|content(1)|elem(urn:p;'b')|attr(ord('x'))");

    [Test]
    public void AnUnqualifiedElementMayBeSpeltExplicitly() =>
        Describe(Lex("Q{}local")).ShouldBe("elem(;'local')");

    [Test]
    public void DotsInsideAUriDoNotSplitTheName() =>
        Describe(Lex("Q{urn:a.b.c}x")).ShouldBe("elem(urn:a.b.c;'x')");

    [Test]
    public void AnEscapedBraceInsideAUriIsLiteral() =>
        Describe(Lex("Q{urn:a\\}b}x")).ShouldBe("elem(urn:a}b;'x')");

    [Test]
    public void AnEscapedBackslashInsideAUriIsLiteral() =>
        Describe(Lex("Q{urn:a\\\\}x")).ShouldBe("elem(urn:a\\;'x')");

    [Test]
    public void AnyOtherBackslashInsideAUriIsAFault()
    {
        // Section 11.4 states this directly.
        LexFault("Q{urn:a\\.b}x").Offset.ShouldBe(7);
        LexFault("Q{urn:a\\u{2E}b}x").Offset.ShouldBe(7);
    }

    [Test]
    public void MarkerRecognitionCommitsForTheAttributeMarker() =>
        LexFault("a.@").Offset.ShouldBe(2);

    [Test]
    public void MarkerRecognitionCommitsForTheContentMarker()
    {
        // Section 8.2: text that begins like a typed marker without completing one is PARSE001,
        // not an ordinary part.
        LexFault("a.#").Offset.ShouldBe(2);
        LexFault("a.#1x").Offset.ShouldBe(4);
        LexFault("a.#x").Offset.ShouldBe(2);
    }

    [Test]
    public void AContentOrdinalIsWrittenWithoutLeadingZeros() =>
        LexFault("#01").Offset.ShouldBe(1);

    [Test]
    public void MarkerRecognitionCommitsForTheQualifiedElementMarker() =>
        LexFault("a.Q{urn:x").Offset.ShouldBe(2);

    [Test]
    public void AnEscapedMarkerCreatesAnOrdinaryPart()
    {
        Describe(Lex("\\@x")).ShouldBe("ord('@x')");
        Describe(Lex("\\#0")).ShouldBe("ord('#0')");
        Describe(Lex("\\#1x")).ShouldBe("ord('#1x')");
        Describe(Lex("\\Q{urn:x}name")).ShouldBe("ord('Q{urn:x}name')");
    }

    [Test]
    public void AMarkerAwayFromAPartStartIsOrdinary()
    {
        Describe(Lex("a@b")).ShouldBe("ord('a@b')");
        Describe(Lex("a#0")).ShouldBe("ord('a#0')");
        Describe(Lex("aQ{urn:x}b")).ShouldBe("ord('aQ{urn:x}b')");
    }

    [Test]
    public void AQNotFollowedByABraceIsOrdinary() =>
        Describe(Lex("Query")).ShouldBe("ord('Query')");

    // Appendix A.2: ordinary-scalar excludes an unescaped '=' and line terminators.

    [Test]
    public void AnUnescapedEqualsIsAFault() =>
        LexFault("a=b").Offset.ShouldBe(1);

    [Test]
    public void ALineTerminatorIsAFault()
    {
        LexFault("a\nb").Offset.ShouldBe(1);
        LexFault("a\rb").Offset.ShouldBe(1);
        LexFault("Q{urn:a\nb}x").Offset.ShouldBe(7);
    }

    [Test]
    public void AnUnescapedClosingBraceOutsideAUriIsOrdinary()
    {
        // Appendix A.2 does not exclude it from ordinary-scalar; Section 8.4 gives it meaning only
        // inside a reference, which this lexer never sees.
        Describe(Lex("a}b")).ShouldBe("ord('a}b')");
    }

    // Structural identity, which the overlay depends on.

    [Test]
    public void EqualNamesAreEqualAndHashAlike()
    {
        var left = Lex("a.#1.Q{urn:p}b.@x.*[env]");
        var right = Lex("a.#1.Q{urn:p}b.@x.*[env]");

        left.ShouldBe(right);
        left.GetHashCode().ShouldBe(right.GetHashCode());
    }

    [Test]
    public void NamesDifferingOnlyInPartKindAreNotEqual()
    {
        // Section 11.4: an ordinary mapping-name component whose text resembles XML syntax is not
        // identical to an XML component.
        Lex("a.@x").ShouldNotBe(Lex("a.\\@x"));
        Lex("a.#1").ShouldNotBe(Lex("a.\\#1"));
        Lex("a.Q{}x").ShouldNotBe(Lex("a.x"));
    }

    [Test]
    public void ALiteralAsteriskIsNotAWildcard() =>
        Lex("a.\\*").ShouldNotBe(Lex("a.*"));

    [Test]
    public void CapturesWithDifferentIdentifiersAreNotEqual() =>
        Lex("*[a]").ShouldNotBe(Lex("*[b]"));

    [Test]
    public void ABareWildcardIsNotAnExplicitCapture() =>
        Lex("*").ShouldNotBe(Lex("*[a]"));

    // Appendix A.4 reference mode.

    [Test]
    public void LexReferenceNameStopsAtTheFirstUnescapedBrace()
    {
        var index = 2;
        var result = QualifiedNameLexer.LexReferenceName("${a.b}tail", ref index);

        result.Fault.ShouldBeNull();
        Describe(result.Name!).ShouldBe("ord('a')|ord('b')");
        index.ShouldBe(6);
    }

    [Test]
    public void LexReferenceNameLeavesTheIndexAloneOnFailure()
    {
        var index = 2;

        QualifiedNameLexer.LexReferenceName("${a..b}", ref index).Fault.ShouldNotBeNull();
        index.ShouldBe(2);
    }

    [Test]
    public void LexReferenceNameRequiresATerminator()
    {
        var index = 2;

        QualifiedNameLexer.LexReferenceName("${abc", ref index).Fault!.Value.Offset.ShouldBe(5);
    }

    [Test]
    public void LexReferenceNameRejectsAnEmptyName()
    {
        var index = 2;

        QualifiedNameLexer.LexReferenceName("${}", ref index).Fault!.Value.Offset.ShouldBe(2);
    }

    [Test]
    public void LexReferenceNameAllowsABraceInsideAUri()
    {
        var index = 2;
        var result = QualifiedNameLexer.LexReferenceName("${Q{urn:a\\}b}x}", ref index);

        Describe(result.Name!).ShouldBe("elem(urn:a}b;'x')");
        index.ShouldBe(15);
    }

    [Test]
    public void AWildcardFaultIsDistinguishedFromAMalformedName()
    {
        // Appendix B maps every condition to its most specific code, and an invalid capture outside
        // a reference is WILDCARD001 rather than the PARSE001 a malformed name earns.
        LexFault("*[]").IsWildcardFault.ShouldBeTrue();
        LexFault("*[abc").IsWildcardFault.ShouldBeTrue();
        LexFault("a..b").IsWildcardFault.ShouldBeFalse();
        LexFault("a\\q").IsWildcardFault.ShouldBeFalse();
    }

    // Totality.

    [Test]
    public void LexingIsTotal()
    {
        string[] alphabet = ["a", ".", "\\", "u", "{", "}", "Q", "@", "#", "*", "[", "]", "0", "=", "\u00e9"];
        var random = new Random(20260805);

        for (var i = 0; i < 20000; i++)
        {
            var length = random.Next(10);
            var text = string.Concat(Enumerable.Range(0, length).Select(_ => alphabet[random.Next(alphabet.Length)]));
            var result = QualifiedNameLexer.Lex(text);

            (result.Name is not null ^ result.Fault is not null).ShouldBeTrue(text);
            if (result.Fault is { } fault)
            {
                fault.Offset.ShouldBeInRange(0, text.Length);
            }
        }
    }
}
