using System.Collections.Immutable;
using Namespace2Xml.Profiles;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// The Section 19.1 namespace encoder and the Section 16.4 delimiter rules.
/// </summary>
/// <remarks>
/// Expectations are authored from Sections 19.1 and 16.4, never captured from the encoder. The
/// round-trip and injectivity properties at the end are the sharpest oracle available for both the
/// encoder and the lexer, because they hold for every name rather than only for the ones someone
/// thought to write down.
/// </remarks>
[TestFixture]
public sealed class NamespaceEncoderTests
{
    private static QualifiedName Name(params NamePart[] parts) => new([.. parts]);

    private static OrdinaryPart Ordinary(string text) => new([new LiteralToken(text)]);

    private static OrdinaryPart Ordinary(params NameToken[] tokens) => new([.. tokens]);

    private static string Encode(
        QualifiedName name,
        string delimiter = ".",
        bool recordLeading = true)
    {
        NamespaceEncoder.TryEncodeName(name, delimiter, recordLeading, out var text, out var fault)
            .ShouldBeTrue(fault.Message);
        return text!;
    }

    private static EncodingFault EncodeFault(QualifiedName name, string delimiter = ".")
    {
        NamespaceEncoder.TryEncodeName(name, delimiter, recordLeading: true, out _, out var fault)
            .ShouldBeFalse();
        return fault;
    }

    // Section 19.1: joining and the configured delimiter.

    [Test]
    public void PartsJoinWithTheConfiguredDelimiter() =>
        Encode(Name(Ordinary("a"), Ordinary("b"), Ordinary("c"))).ShouldBe("a.b.c");

    [Test]
    public void ANonDefaultDelimiterJoins() =>
        Encode(Name(Ordinary("a"), Ordinary("b")), "::").ShouldBe("a::b");

    [Test]
    public void ASinglePartNeedsNoDelimiter() =>
        Encode(Name(Ordinary("a"))).ShouldBe("a");

    // Section 16.4: delimiter occurrences inside a part.

    [Test]
    public void TheDefaultDelimiterEmitsAUnicodeEscapeRatherThanTheLexerForm() =>
        Encode(Name(Ordinary("a.b"))).ShouldBe("a\\u{2E}b");

    [Test]
    public void OverlappingDelimiterOccurrencesAreEachEscaped() =>
        Encode(Name(Ordinary("a:::b")), "::").ShouldBe("a\\u{3A}\\u{3A}:b");

    [Test]
    public void ADelimiterOccurrenceEscapesOnlyItsFirstScalar() =>
        Encode(Name(Ordinary("::")), "::").ShouldBe("\\u{3A}:");

    [Test]
    public void ATrailingPartialDelimiterIsNotEscaped() =>
        Encode(Name(Ordinary("a:")), "::").ShouldBe("a:");

    [Test]
    public void EmittedEscapeTextIsNeverRescannedForTheDelimiter() =>
        // "u" is a legal delimiter, and the escape it forces contains a "u" that must stay bare.
        Encode(Name(Ordinary("u")), "u").ShouldBe("\\u{75}");

    [Test]
    public void AUnicodeEscapeHasNoLeadingZerosAndUpperCaseDigits() =>
        Encode(Name(Ordinary("a\nb"))).ShouldBe("a\\u{A}b");

    [Test]
    public void ASupplementaryDelimiterEscapesAsOneCodePoint() =>
        Encode(Name(Ordinary("\U0001F600")), "\U0001F600").ShouldBe("\\u{1F600}");

    // Section 19.1: the escape list.

    [TestCase("a\\b", "a\\\\b")]
    [TestCase("a*b", "a\\*b")]
    [TestCase("a=b", "a\\=b")]
    [TestCase("a}b", "a\\}b")]
    [TestCase("a${b", "a\\${b")]
    [TestCase("a\rb", "a\\u{D}b")]
    [TestCase("a\tb", "a\\u{9}b")]
    public void EscapedScalarsUseTheirLexerFormOrAUnicodeEscape(string text, string expected) =>
        Encode(Name(Ordinary(text))).ShouldBe(expected);

    [Test]
    public void ALoneDollarIsNotEscaped() =>
        Encode(Name(Ordinary("a$b"))).ShouldBe("a$b");

    [Test]
    public void AnOpeningBraceAloneIsNotEscaped() =>
        Encode(Name(Ordinary("a{b"))).ShouldBe("a{b");

    [TestCase("a\u0085b", "a\\u{85}b")]
    [TestCase("a\u2028b", "a\\u{2028}b")]
    [TestCase("a\u2029b", "a\\u{2029}b")]
    [TestCase("a\u200Bb", "a\\u{200B}b")]
    [TestCase("a\0b", "a\\u{0}b")]
    public void ForbiddenScalarsEscape(string text, string expected) =>
        Encode(Name(Ordinary(text))).ShouldBe(expected);

    [Test]
    public void OrdinaryPrintableScalarsAreLiteral() =>
        Encode(Name(Ordinary("a-b_c/d:e"))).ShouldBe("a-b_c/d:e");

    // Section 19.1: record-leading escaping.

    [Test]
    public void ALeadingExclamationIsEscaped() =>
        Encode(Name(Ordinary("!a"))).ShouldBe("\\!a");

    [Test]
    public void AnExclamationAfterOnlySpacesIsEscaped() =>
        Encode(Name(Ordinary("  !a"))).ShouldBe("  \\!a");

    [Test]
    public void AnEscapedTabBeforeAnExclamationRemovesTheNeedToEscapeIt() =>
        // A tab is itself escaped, so the emitted text before the '!' is no longer only spaces and
        // tabs, and the record can no longer be read as a directive.
        Encode(Name(Ordinary(" \t!a"))).ShouldBe(" \\u{9}!a");

    [Test]
    public void AnExclamationAfterOtherTextIsNotEscaped() =>
        Encode(Name(Ordinary("x!a"))).ShouldBe("x!a");

    [Test]
    public void AnExclamationInALaterPartIsNotEscaped() =>
        Encode(Name(Ordinary("a"), Ordinary("!b"))).ShouldBe("a.!b");

    // Section 19.1: typed markers on ordinary components.

    [Test]
    public void ALeadingAtSignOnAnOrdinaryComponentIsEscaped() =>
        Encode(Name(Ordinary("@a"))).ShouldBe("\\@a");

    [Test]
    public void ALeadingHashOnAnOrdinaryComponentIsEscapedWithoutDigits() =>
        Encode(Name(Ordinary("#x"))).ShouldBe("\\#x");

    [Test]
    public void ALeadingHashOnAnOrdinaryComponentIsEscapedWithDigits() =>
        Encode(Name(Ordinary("a"), Ordinary("#1"))).ShouldBe("a.\\#1");

    [Test]
    public void ALeadingQBraceOnAnOrdinaryComponentIsEscaped() =>
        Encode(Name(Ordinary("Q{u}v"))).ShouldBe("\\Q{u\\}v");

    [Test]
    public void ALeadingQWithoutABraceIsNotEscaped() =>
        Encode(Name(Ordinary("Qa"))).ShouldBe("Qa");

    [Test]
    public void AnAtSignAfterTheFirstScalarIsNotEscaped() =>
        Encode(Name(Ordinary("a@b"))).ShouldBe("a@b");

    [Test]
    public void AMarkerAfterALeadingWildcardIsNotEscaped() =>
        Encode(Name(Ordinary(new WildcardToken(null), new LiteralToken("@b")))).ShouldBe("*@b");

    // Section 19.1: typed components emit canonical unescaped notation.

    [Test]
    public void AnAttributeEmitsItsMarker() =>
        Encode(Name(Ordinary("a"), new AttributePart(Ordinary("b")))).ShouldBe("a.@b");

    [Test]
    public void AContentTokenEmitsItsOrdinal() =>
        Encode(Name(Ordinary("a"), new ContentPart(3))).ShouldBe("a.#3");

    [Test]
    public void AQualifiedElementEmitsItsUriUnescaped() =>
        Encode(Name(new QualifiedElementPart("http://e/ns", [new LiteralToken("local")])))
            .ShouldBe("Q{http://e/ns}local");

    [Test]
    public void AQualifiedAttributeEmitsBothMarkers() =>
        Encode(Name(
                Ordinary("a"),
                new AttributePart(new QualifiedElementPart("u", [new LiteralToken("b")]))))
            .ShouldBe("a.@Q{u}b");

    [Test]
    public void AnEmptyUriEmitsExplicitly() =>
        Encode(Name(new QualifiedElementPart(string.Empty, [new LiteralToken("a")]))).ShouldBe("Q{}a");

    [Test]
    public void ATypedComponentAndAnOrdinaryOneWithTheSameTextDiffer() =>
        Encode(Name(Ordinary("a"), new AttributePart(Ordinary("b"))))
            .ShouldNotBe(Encode(Name(Ordinary("a"), Ordinary("@b"))));

    [Test]
    public void ADelimiterOccurrenceInsideAUriIsLiteral() =>
        Encode(Name(new QualifiedElementPart("http://e/ns", [new LiteralToken("local")])), "/")
            .ShouldBe("Q{http://e/ns}local");

    [Test]
    public void AUriEscapesOnlyBraceAndBackslash() =>
        Encode(Name(new QualifiedElementPart("a}b\\c=d.e", [new LiteralToken("x")])))
            .ShouldBe("Q{a\\}b\\\\c=d.e}x");

    [Test]
    public void AQualifiedElementLocalNameStillEscapes() =>
        Encode(Name(new QualifiedElementPart("u", [new LiteralToken("a.b")])))
            .ShouldBe("Q{u}a\\u{2E}b");

    [Test]
    public void AQualifiedElementLocalNameHasNoLeadingMarkerEscape() =>
        // The Q{...} prefix already commits the component, so a following '@' cannot be misread.
        Encode(Name(new QualifiedElementPart("u", [new LiteralToken("@a")]))).ShouldBe("Q{u}@a");

    // Section 19.1: wildcards.

    [Test]
    public void ABareWildcardEmitsAnAsterisk() =>
        Encode(Name(Ordinary("a"), Ordinary(new WildcardToken(null)))).ShouldBe("a.*");

    [Test]
    public void AnExplicitCaptureEmitsItsIdentifier() =>
        Encode(Name(Ordinary("a"), Ordinary(new WildcardToken("id")))).ShouldBe("a.*[id]");

    [Test]
    public void AWildcardAndAnEscapedAsteriskDiffer() =>
        Encode(Name(Ordinary(new WildcardToken(null)))).ShouldNotBe(Encode(Name(Ordinary("*"))));

    [Test]
    public void MixedTokensEmitInOrder() =>
        Encode(Name(Ordinary(
                new LiteralToken("a"),
                new WildcardToken(null),
                new LiteralToken("b"),
                new WildcardToken("c"),
                new LiteralToken("d"))))
            .ShouldBe("a*b*[c]d");

    // Section 19.1: names with no spelling.

    [Test]
    public void AnUnpairedSurrogateIsBlocking() =>
        EncodeFault(Name(Ordinary("a\uD800b"))).Message.ShouldContain("surrogate");

    [Test]
    public void APairedSurrogateIsFine() =>
        Encode(Name(Ordinary("a\U0001F600b"))).ShouldBe("a\U0001F600b");

    [Test]
    public void AForbiddenScalarInAUriIsBlocking() =>
        EncodeFault(Name(new QualifiedElementPart("a\u0001b", [new LiteralToken("x")])))
            .Message.ShouldContain("Q{...}");

    [Test]
    public void AnUnpairedSurrogateInAUriIsBlocking() =>
        EncodeFault(Name(new QualifiedElementPart("a\uDC00", [new LiteralToken("x")])))
            .Message.ShouldContain("surrogate");

    [Test]
    public void ALeadingContentTokenIsBlocking() =>
        EncodeFault(Name(new ContentPart(0), Ordinary("a"))).Message.ShouldContain("root");

    [Test]
    public void ANonLeadingContentTokenIsFine() =>
        Encode(Name(Ordinary("a"), new ContentPart(0))).ShouldBe("a.#0");

    // Section 16.4: delimiter validity.

    [TestCase(".")]
    [TestCase("::")]
    [TestCase("_")]
    [TestCase("/")]
    [TestCase("->")]
    [TestCase("u_")]
    [TestCase("G")]
    [TestCase("\U0001F600")]
    public void ValidDelimitersAreAccepted(string delimiter) =>
        NamespaceEncoder.IsValidDelimiter(delimiter, out _).ShouldBeTrue();

    [TestCase("")]
    [TestCase("=")]
    [TestCase("a=b")]
    [TestCase("\\")]
    [TestCase("\n")]
    [TestCase("\t")]
    [TestCase("\0")]
    [TestCase("\u0085")]
    [TestCase("\u2028")]
    [TestCase("\u200B")]
    public void InvalidDelimitersAreRejected(string delimiter) =>
        NamespaceEncoder.IsValidDelimiter(delimiter, out _).ShouldBeFalse();

    [Test]
    public void AnUnpairedSurrogateDelimiterIsRejected() =>
        // Built here rather than in a TestCase, because attribute metadata does not survive a lone
        // surrogate intact.
        NamespaceEncoder.IsValidDelimiter(((char)0xD800).ToString(), out _).ShouldBeFalse();

    [TestCase("E")]
    [TestCase("2E")]
    [TestCase("u{")]
    [TestCase("}")]
    [TestCase("0")]
    [TestCase("ABCDEF")]
    public void DelimitersDrawnOnlyFromEscapeTextAreRejected(string delimiter)
    {
        NamespaceEncoder.IsValidDelimiter(delimiter, out var reason).ShouldBeFalse();
        reason.ShouldNotBeNull().ShouldContain("hexadecimal");
    }

    [Test]
    public void ARejectedDelimiterSaysWhy()
    {
        NamespaceEncoder.IsValidDelimiter("=", out var reason).ShouldBeFalse();
        reason.ShouldNotBeNull().ShouldContain("forbidden set");
    }

    [Test]
    public void AnEmptyDelimiterSaysWhy()
    {
        NamespaceEncoder.IsValidDelimiter(string.Empty, out var reason).ShouldBeFalse();
        reason.ShouldNotBeNull().ShouldContain("empty");
    }

    // Section 19.1: value encoding.

    private static string EncodeValue(params ValueToken[] tokens)
    {
        NamespaceEncoder
            .TryEncodeValue(new InterpretedValue([.. tokens]), ".", out var text, out var fault)
            .ShouldBeTrue(fault.Message);
        return text!;
    }

    [TestCase("a\\b", "a\\\\b")]
    [TestCase("a\nb", "a\\nb")]
    [TestCase("a\rb", "a\\rb")]
    [TestCase("a\tb", "a\\tb")]
    [TestCase("a*b", "a\\*b")]
    [TestCase("a${b", "a\\${b")]
    [TestCase("a$b", "a$b")]
    [TestCase("a=b.c", "a=b.c")]
    [TestCase("a}b", "a}b")]
    public void ValueLiteralsUseTheInverseOfTheValueLexer(string text, string expected) =>
        EncodeValue(new LiteralValueToken(text)).ShouldBe(expected);

    [Test]
    public void AValueWildcardEmitsAnAsterisk() =>
        EncodeValue(new ValueWildcardToken(null)).ShouldBe("*");

    [Test]
    public void AValueCaptureEmitsItsIdentifier() =>
        EncodeValue(new ValueWildcardToken("id")).ShouldBe("*[id]");

    [Test]
    public void AReferenceEmitsItsName() =>
        EncodeValue(new ReferenceToken(Name(Ordinary("a"), Ordinary("b")))).ShouldBe("${a.b}");

    [Test]
    public void AReferenceToANameContainingABraceEscapesIt() =>
        EncodeValue(new ReferenceToken(Name(Ordinary("a}b")))).ShouldBe("${a\\}b}");

    // Section 19.1 line "For record-leading escaping ... all emitted text preceding it in that
    // record consists only of spaces and tabs". A reference is preceded by a name, '=' and '${',
    // so the Section 5 mask marker has no meaning there and is not escaped.

    [Test]
    public void AMaskMarkerIsEscapedWhereItCouldBeginARecord() =>
        Encode(Name(Ordinary("!a"))).ShouldBe("\\!a");

    [Test]
    public void AMaskMarkerInsideAReferenceIsNotEscaped() =>
        EncodeValue(new ReferenceToken(Name(Ordinary("!a")))).ShouldBe("${!a}");

    // Section 19.1: a leading '#' on an ordinary component is escaped for a second, unconditional
    // reason - Section 8.2 makes marker recognition commit - so it stays escaped inside a
    // reference even though the record-leading rule does not apply there.

    [Test]
    public void ACommentMarkerInsideAReferenceIsStillEscaped() =>
        EncodeValue(new ReferenceToken(Name(Ordinary("#a")))).ShouldBe("${\\#a}");

    [Test]
    public void AnEmptyValueEmitsNothing() =>
        EncodeValue().ShouldBe(string.Empty);

    [Test]
    public void AValueMixesTokensInOrder() =>
        EncodeValue(
                new LiteralValueToken("x"),
                new ReferenceToken(Name(Ordinary("a"))),
                new LiteralValueToken("y"),
                new ValueWildcardToken(null),
                new LiteralValueToken("z"))
            .ShouldBe("x${a}y*z");

    [Test]
    public void AValueRejectsAnUnspellableReference() =>
        NamespaceEncoder.TryEncodeValue(
                new InterpretedValue([new ReferenceToken(Name(Ordinary("a\uD800")))]),
                ".",
                out _,
                out _)
            .ShouldBeFalse();

    // Injectivity presupposes that one name has one representation.

    [Test]
    public void AdjacentLiteralsMergeSoOneNameHasOneRepresentation()
    {
        Ordinary(new LiteralToken("a"), new LiteralToken("b")).ShouldBe(Ordinary("ab"));
        Encode(Name(Ordinary(new LiteralToken("a"), new LiteralToken("b")))).ShouldBe("ab");
    }

    [Test]
    public void EmptyLiteralsAreDropped()
    {
        Ordinary(new LiteralToken(string.Empty), new WildcardToken(null))
            .ShouldBe(Ordinary(new WildcardToken(null)));
        new QualifiedElementPart("u", [new LiteralToken("a"), new LiteralToken("b")])
            .ShouldBe(new QualifiedElementPart("u", [new LiteralToken("ab")]));
    }

    [Test]
    public void AdjacentValueLiteralsMerge() =>
        new InterpretedValue([new LiteralValueToken("a"), new LiteralValueToken("b")])
            .ShouldBe(new InterpretedValue([new LiteralValueToken("ab")]));

    // Round-trip and injectivity: the properties Section 19.1 states.

    private static IEnumerable<QualifiedName> RoundTripNames() =>
    [
        Name(Ordinary("a")),
        Name(Ordinary("a"), Ordinary("b"), Ordinary("c")),
        Name(Ordinary("a.b")),
        Name(Ordinary("a\\b")),
        Name(Ordinary("a*b")),
        Name(Ordinary("a=b")),
        Name(Ordinary("a}b")),
        Name(Ordinary("a${b}c")),
        Name(Ordinary("!a")),
        Name(Ordinary("#1")),
        Name(Ordinary("@a")),
        Name(Ordinary("Q{u}v")),
        Name(Ordinary("a\nb\r\tc")),
        Name(Ordinary("a\u0085\u2028\u2029b")),
        Name(Ordinary("a\U0001F600b")),
        Name(Ordinary(new WildcardToken(null))),
        Name(Ordinary(new LiteralToken("a"), new WildcardToken("id"), new LiteralToken("b"))),
        Name(Ordinary("a"), new AttributePart(Ordinary("b"))),
        Name(Ordinary("a"), new AttributePart(Ordinary("b.c"))),
        Name(Ordinary("a"), new ContentPart(0)),
        Name(Ordinary("a"), new ContentPart(42)),
        Name(new QualifiedElementPart("http://e/ns", [new LiteralToken("local")])),
        Name(new QualifiedElementPart(string.Empty, [new LiteralToken("a")])),
        Name(new QualifiedElementPart("a}b\\c", [new LiteralToken("x")])),
        Name(Ordinary("a"), new AttributePart(new QualifiedElementPart("u", [new LiteralToken("b")]))),
        Name(Ordinary(" \t"), Ordinary("b")),
    ];

    [TestCaseSource(nameof(RoundTripNames))]
    public void EncodingThenLexingWithTheDefaultDelimiterYieldsTheSameName(QualifiedName name)
    {
        var text = Encode(name);
        var result = QualifiedNameLexer.Lex(text);

        result.Fault.ShouldBeNull();
        result.Name.ShouldBe(name);
    }

    [Test]
    public void EncodingIsInjectiveAcrossTheRoundTripCorpus()
    {
        var names = RoundTripNames().ToList();
        var encoded = names.Select(name => Encode(name)).ToList();

        names.Distinct().Count().ShouldBe(names.Count);
        encoded.Distinct(StringComparer.Ordinal).Count().ShouldBe(encoded.Count);
    }

    [TestCase("a.b")]
    [TestCase("a\\.b")]
    [TestCase("a\\u{2E}b")]
    [TestCase("a\\u{002e}b")]
    [TestCase("@a")]
    [TestCase("a.#7")]
    [TestCase("Q{u}v")]
    [TestCase("a\\}b")]
    [TestCase("a}b")]
    [TestCase("*[x].b")]
    [TestCase("a*b")]
    [TestCase("\\!a")]
    [TestCase("\\#1")]
    public void EncodingThenLexingIsIdentityOnLexedNames(string text)
    {
        // Every text the lexer accepts denotes a name; encoding that name and lexing again gives the
        // same name back, even where the two spellings differ.
        var first = QualifiedNameLexer.Lex(text);
        first.Fault.ShouldBeNull();

        var again = QualifiedNameLexer.Lex(Encode(first.Name!));
        again.Fault.ShouldBeNull();
        again.Name.ShouldBe(first.Name);
    }

    [Test]
    public void EncodingThenLexingIsIdentityOnRandomNames()
    {
        var random = new Random(20260805);
        const string Alphabet = ".\\*=}${@#!Q{}abc \t\n\r\u0085\u200B\U0001F600";
        var seen = new Dictionary<string, QualifiedName>(StringComparer.Ordinal);
        var encoded = 0;

        for (var i = 0; i < 20_000; i++)
        {
            var parts = ImmutableArray.CreateBuilder<NamePart>();

            for (var p = 0; p < random.Next(1, 4); p++)
            {
                var tokens = ImmutableArray.CreateBuilder<NameToken>();

                for (var t = 0; t < random.Next(1, 3); t++)
                {
                    if (random.Next(4) == 0)
                    {
                        tokens.Add(new WildcardToken(random.Next(2) == 0 ? null : "c" + t));
                        continue;
                    }

                    var text = string.Concat(
                        Enumerable
                            .Range(0, random.Next(1, 6))
                            .Select(_ => Alphabet[random.Next(Alphabet.Length)].ToString()));

                    tokens.Add(new LiteralToken(text));
                }

                parts.Add(random.Next(6) switch
                {
                    0 when p > 0 => new ContentPart(random.Next(0, 100)),
                    1 => new AttributePart(new OrdinaryPart(tokens.ToImmutable())),
                    2 => new QualifiedElementPart("ns" + p, tokens.ToImmutable()),
                    _ => new OrdinaryPart(tokens.ToImmutable()),
                });
            }

            var name = new QualifiedName(parts.ToImmutable());

            if (!NamespaceEncoder.TryEncodeName(name, ".", recordLeading: true, out var spelling, out _))
            {
                continue;
            }

            encoded++;

            var result = QualifiedNameLexer.Lex(spelling!);
            result.Name.ShouldNotBeNull($"'{spelling}' did not lex: {result.Fault?.Message}");
            result.Name.ShouldBe(name);

            // Injectivity: two distinct names never share a spelling.
            if (seen.TryGetValue(spelling!, out var previous))
            {
                previous.ShouldBe(name);
            }
            else
            {
                seen.Add(spelling!, name);
            }
        }

        encoded.ShouldBeGreaterThan(10_000);
    }

    [Test]
    public void EncodingAValueThenLexingItYieldsTheSameValue()
    {
        foreach (var tokens in new ValueToken[][]
        {
            [new LiteralValueToken("plain")],
            [new LiteralValueToken("a\\b*c${d}e\nf\rg\th")],
            [new ValueWildcardToken(null)],
            [new ValueWildcardToken("id")],
            [new ReferenceToken(Name(Ordinary("a"), Ordinary("b")))],
            [new ReferenceToken(Name(Ordinary("a}b")))],
            [new ReferenceToken(Name(Ordinary("a.b")))],
            [new ReferenceToken(Name(Ordinary("a"), new ContentPart(1)))],
            [new LiteralValueToken("x"), new ReferenceToken(Name(Ordinary("a"))), new LiteralValueToken("y")],
            [new LiteralValueToken("x"), new ValueWildcardToken(null), new LiteralValueToken("*")],
        })
        {
            var value = new InterpretedValue([.. tokens]);
            NamespaceEncoder.TryEncodeValue(value, ".", out var text, out var fault)
                .ShouldBeTrue(fault.Message);

            // Section 12.1 recognizes one capture form per rule, so a round trip is only defined
            // against the form the value itself uses: '*[id]' read where only the bare form is
            // recognized is a bare capture followed by literal brackets, which is a different value.
            var syntax = ValueSyntax.Profile(
                tokens.OfType<ValueWildcardToken>().Any(wildcard => wildcard.CaptureId is not null)
                    ? WildcardSyntax.Explicit
                    : WildcardSyntax.Unnamed);
            var result = ValueLexer.Lex(text!, syntax);
            result.Fault.ShouldBeNull();
            result.Value.ShouldBe(value);
        }
    }
}
