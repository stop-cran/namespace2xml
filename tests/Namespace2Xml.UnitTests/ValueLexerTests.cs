using System.Collections.Immutable;
using System.Globalization;
using Namespace2Xml.Profiles;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// The Appendix A.3 value grammar, the Appendix A.5 native-string transducer, the Section 8.4
/// reference syntax, and the Section 12.1 rule that decides whether a value has wildcards at all.
/// </summary>
[TestFixture]
public sealed class ValueLexerTests
{
    private static InterpretedValue Lex(string text, ValueSyntax syntax)
    {
        var result = ValueLexer.Lex(text, syntax);
        result.Fault.ShouldBeNull(text);
        return result.Value!;
    }

    private static InterpretedValue Lex(string text) =>
        Lex(text, ValueSyntax.Profile(WildcardSyntax.Unnamed));

    private static InterpretedValue LexExplicit(string text) =>
        Lex(text, ValueSyntax.Profile(WildcardSyntax.Explicit));

    private static ValueFault LexFault(string text, ValueSyntax syntax)
    {
        var result = ValueLexer.Lex(text, syntax);
        result.Value.ShouldBeNull(text);
        return result.Fault!.Value;
    }

    private static ValueFault LexFault(string text) =>
        LexFault(text, ValueSyntax.Profile(WildcardSyntax.Explicit));

    private static string Describe(InterpretedValue value) =>
        string.Join("|", value.Tokens.Select(token => token switch
        {
            LiteralValueToken literal => "'" + literal.Text + "'",
            ValueWildcardToken { CaptureId: null } => "<*>",
            ValueWildcardToken wildcard => "<*" + wildcard.CaptureId + ">",
            ReferenceToken reference => "ref(" + Render(reference.Name) + ")",
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        }));

    private static string Render(QualifiedName name) =>
        string.Join(".", name.Parts.Select(RenderPart));

    private static string RenderPart(NamePart part) => part switch
    {
        OrdinaryPart ordinary => RenderTokens(ordinary.Tokens),
        QualifiedElementPart qualified => "Q{" + qualified.Uri + "}" + RenderTokens(qualified.Local),
        AttributePart attribute => "@" + RenderPart(attribute.Name),
        ContentPart content => "#" + content.Ordinal.ToString(CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(nameof(part)),
    };

    private static string RenderTokens(ImmutableArray<NameToken> tokens) =>
        string.Concat(tokens.Select(token => token switch
        {
            LiteralToken literal => literal.Text,
            WildcardToken { CaptureId: null } => "<*>",
            WildcardToken wildcard => "<*" + wildcard.CaptureId + ">",
            _ => throw new ArgumentOutOfRangeException(nameof(tokens)),
        }));

    // Appendix A.3 escapes.

    [Test]
    public void PlainTextIsOneLiteralToken() =>
        Describe(Lex("hello world")).ShouldBe("'hello world'");

    [Test]
    public void AnEmptyValueHasNoTokens()
    {
        var value = Lex(string.Empty);

        value.Tokens.ShouldBeEmpty();
        value.LiteralText.ShouldBe(string.Empty);
    }

    [Test]
    public void EveryRecognizedValueEscapeEmitsItsScalar()
    {
        Describe(Lex("a\\\\b")).ShouldBe("'a\\b'");
        Describe(Lex("a\\*b")).ShouldBe("'a*b'");
        Describe(Lex("a\\${b")).ShouldBe("'a${b'");
        Describe(Lex("a\\nb")).ShouldBe("'a\nb'");
        Describe(Lex("a\\rb")).ShouldBe("'a\rb'");
        Describe(Lex("a\\tb")).ShouldBe("'a\tb'");
    }

    [Test]
    public void AnUnknownValueEscapePreservesBothScalars()
    {
        // A.3: unknown-value-escape preserves the backslash and the following scalar. This is where
        // values differ from names, which reject every unlisted backslash sequence.
        Describe(Lex("a\\qb")).ShouldBe("'a\\qb'");
        Describe(Lex("a\\.b")).ShouldBe("'a\\.b'");
        Describe(Lex("a\\}b")).ShouldBe("'a\\}b'");
        Describe(Lex("a\\@b")).ShouldBe("'a\\@b'");
    }

    [Test]
    public void ValuesDoNotRecognizeUnicodeEscapes() =>
        Describe(Lex("\\u{2E}")).ShouldBe("'\\u{2E}'");

    [Test]
    public void ADollarEscapeIsRecognizedOnlyBeforeABrace()
    {
        // "\${" is three scalars and beats the two-scalar unknown escape, but "\$" alone is one.
        Describe(Lex("\\$x")).ShouldBe("'\\$x'");
        Describe(Lex("\\$")).ShouldBe("'\\$'");
    }

    [Test]
    public void ATrailingBackslashEmitsItself() =>
        Describe(Lex("a\\")).ShouldBe("'a\\'");

    [Test]
    public void EmittedTextIsNeverRescanned()
    {
        // The "${" that "\${" emits is literal, which is the whole point of Section 8.4's logger
        // template example, and the "*" that "\*" emits is not a wildcard.
        Describe(Lex("\\${yyyy-MM-dd SEVERITY MESSAGE}"))
            .ShouldBe("'${yyyy-MM-dd SEVERITY MESSAGE}'");
        Describe(Lex("\\*")).ShouldBe("'*'");
    }

    [Test]
    public void AnEscapedBackslashDoesNotShieldTheNextScalar()
    {
        // A.3: "\\${a}" is an escaped backslash and then a reference, because the escape consumed
        // both backslashes and the pass resumes at the dollar sign.
        Describe(Lex("\\\\${a}")).ShouldBe("'\\'|ref(a)");
        Describe(Lex("\\\\*")).ShouldBe("'\\'|<*>");
    }

    // Section 8.4 references.

    [Test]
    public void AReferenceBecomesItsOwnToken() =>
        Describe(Lex("${host}")).ShouldBe("ref(host)");

    [Test]
    public void ConcatenationInterleavesLiteralsAndReferences() =>
        Describe(Lex("https://${host}:${port}"))
            .ShouldBe("'https://'|ref(host)|':'|ref(port)");

    [Test]
    public void AReferenceNameUsesTheQualifiedNameGrammar()
    {
        Describe(Lex("${a.b.c}")).ShouldBe("ref(a.b.c)");
        Describe(Lex("${a.@x}")).ShouldBe("ref(a.@x)");
        Describe(Lex("${a.#1}")).ShouldBe("ref(a.#1)");
        Describe(Lex("${Q{urn:p}x}")).ShouldBe("ref(Q{urn:p}x)");
    }

    [Test]
    public void TheFirstUnescapedBraceEndsAReference()
    {
        // A.4 fixes this: ordinary-scalar would otherwise admit "}" and make "${a}b}" ambiguous.
        Describe(Lex("${a}b}")).ShouldBe("ref(a)|'b}'");
    }

    [Test]
    public void AnEscapedBraceBelongsToTheReferenceName() =>
        Describe(Lex("${a\\}b}")).ShouldBe("ref(a}b)");

    [Test]
    public void ABraceInsideAUriDoesNotEndTheReference() =>
        Describe(Lex("${Q{urn:a\\}b}x}")).ShouldBe("ref(Q{urn:a}b}x)");

    [Test]
    public void AnUnterminatedReferenceIsAReferenceFault()
    {
        var fault = LexFault("${abc");

        fault.Kind.ShouldBe(ValueFaultKind.Reference);
        fault.Offset.ShouldBe(5);
    }

    [Test]
    public void AnEmptyReferenceIsAReferenceFault() =>
        LexFault("${}").Kind.ShouldBe(ValueFaultKind.Reference);

    [Test]
    public void AReferenceOpenedAtTheEndIsAReferenceFault() =>
        LexFault("x${").Kind.ShouldBe(ValueFaultKind.Reference);

    [Test]
    public void AMalformedReferenceNameIsAReferenceFaultRatherThanAParseFault()
    {
        // Appendix B: a malformed name inside a reference is REFERENCE001, not PARSE001.
        LexFault("${a..b}").Kind.ShouldBe(ValueFaultKind.Reference);
        LexFault("${a.#1x}").Kind.ShouldBe(ValueFaultKind.Reference);
        LexFault("${.a}").Kind.ShouldBe(ValueFaultKind.Reference);
    }

    [Test]
    public void ALegacyCaptureInsideAReferenceIsAReferenceFault()
    {
        // Section 12.1 and A.4: references from templates must use explicit captures.
        var fault = LexFault("x${a.*}");

        fault.Kind.ShouldBe(ValueFaultKind.Reference);
        fault.Offset.ShouldBe(1);
    }

    [Test]
    public void AFreeWildcardReferenceIsAReferenceFault() =>
        LexFault("${a.*}").Kind.ShouldBe(ValueFaultKind.Reference);

    [Test]
    public void AnExplicitCaptureInsideAReferenceIsSyntacticallyValid() =>
        Describe(LexExplicit("${a.*[0].b}")).ShouldBe("ref(a.<*0>.b)");

    [Test]
    public void ADollarWithoutABraceIsLiteral()
    {
        Describe(Lex("100$")).ShouldBe("'100$'");
        Describe(Lex("$x")).ShouldBe("'$x'");
    }

    // Section 12.1 and 12.2 wildcards.

    [Test]
    public void ABareAsteriskSubstitutesWhenTheNameDefinesUnnamedCaptures() =>
        Describe(Lex("a*b")).ShouldBe("'a'|<*>|'b'");

    [Test]
    public void ExplicitCapturesSubstituteByIdentifier() =>
        Describe(LexExplicit("text1*[1]-repeat-*[1]"))
            .ShouldBe("'text1'|<*1>|'-repeat-'|<*1>");

    [Test]
    public void AnAsteriskIsLiteralWhenTheNameDefinesNoCaptures()
    {
        // Section 12.1: "values such as pattern=*.txt require no escape".
        Describe(Lex("*.txt", ValueSyntax.Profile(WildcardSyntax.None))).ShouldBe("'*.txt'");
    }

    [Test]
    public void ABracketedAsteriskIsLiteralWhenTheNameDefinesNoCaptures()
    {
        // Section 12.1: a glob, not an undefined capture, and not a lexical error.
        Describe(Lex("/opt/x*[0-9]/y", ValueSyntax.Profile(WildcardSyntax.None)))
            .ShouldBe("'/opt/x*[0-9]/y'");
        Describe(Lex("a*[", ValueSyntax.Profile(WildcardSyntax.None))).ShouldBe("'a*['");
    }

    [Test]
    public void AnUnterminatedCaptureIsAWildcardFaultWhereRecognitionIsEnabled()
    {
        var fault = LexFault("a*[bc");

        fault.Kind.ShouldBe(ValueFaultKind.Wildcard);
        fault.Offset.ShouldBe(2);
    }

    [Test]
    public void AnEmptyCaptureIdentifierIsAWildcardFault() =>
        LexFault("*[]").Kind.ShouldBe(ValueFaultKind.Wildcard);

    [Test]
    public void AnEscapedAsteriskIsLiteralInATemplateValue()
    {
        // Section 12.1: "\* remains the explicit literal spelling in a template value."
        var value = Lex("a\\*b");

        Describe(value).ShouldBe("'a*b'");
        value.ContainsWildcard.ShouldBeFalse();
    }

    // Section 13.4 disabled substitution.

    [Test]
    public void DisabledSubstitutionStillDecodesProfileEscapes()
    {
        // Section 13.4: "Namespace-profile lexical escapes are decoded regardless of substitution
        // mode because they are part of the profile syntax."
        Describe(Lex("a\\nb", ValueSyntax.ProfileUninterpreted)).ShouldBe("'a\nb'");
        Describe(Lex("a\\\\b", ValueSyntax.ProfileUninterpreted)).ShouldBe("'a\\b'");
        Describe(Lex("a\\*b", ValueSyntax.ProfileUninterpreted)).ShouldBe("'a*b'");
        Describe(Lex("a\\${b", ValueSyntax.ProfileUninterpreted)).ShouldBe("'a${b'");
    }

    [Test]
    public void DisabledSubstitutionLeavesReferencesAndWildcardsAsText()
    {
        Describe(Lex("${a}", ValueSyntax.ProfileUninterpreted)).ShouldBe("'${a}'");
        Describe(Lex("a*b", ValueSyntax.ProfileUninterpreted)).ShouldBe("'a*b'");
        Describe(Lex("${", ValueSyntax.ProfileUninterpreted)).ShouldBe("'${'");
    }

    // Appendix A.5 decoded native strings.

    [Test]
    public void ANativeStringRecognizesOnlyTwoEscapes()
    {
        Describe(Lex("a\\*b", ValueSyntax.NativeString(WildcardSyntax.Unnamed))).ShouldBe("'a*b'");
        Describe(Lex("a\\${b", ValueSyntax.NativeString(WildcardSyntax.Explicit))).ShouldBe("'a${b'");
    }

    [Test]
    public void ANativeStringLeavesEveryOtherBackslashAlone()
    {
        // Rule 4: the backslash emits itself and consumes nothing after it, so "\n" stays two
        // scalars rather than becoming LF.
        Describe(Lex("a\\nb", ValueSyntax.NativeString(WildcardSyntax.Explicit))).ShouldBe("'a\\nb'");
        Describe(Lex("a\\tb", ValueSyntax.NativeString(WildcardSyntax.Explicit))).ShouldBe("'a\\tb'");
        Describe(Lex("a\\qb", ValueSyntax.NativeString(WildcardSyntax.Explicit))).ShouldBe("'a\\qb'");
        Describe(Lex("a\\$b", ValueSyntax.NativeString(WildcardSyntax.Explicit))).ShouldBe("'a\\$b'");
        Describe(Lex("a\\", ValueSyntax.NativeString(WildcardSyntax.Explicit))).ShouldBe("'a\\'");
    }

    [Test]
    public void ANativeStringDoesNotDecodeADoubledBackslash()
    {
        // "no substring rescanning or second general \\ decoding pass occurs": the first backslash
        // emits itself and consumes only itself, so the second one still escapes the asterisk.
        Describe(Lex("\\\\*", ValueSyntax.NativeString(WildcardSyntax.Explicit))).ShouldBe("'\\*'");
        Describe(Lex("\\\\${a}", ValueSyntax.NativeString(WildcardSyntax.Explicit))).ShouldBe("'\\${a}'");
        Describe(Lex("a\\\\b", ValueSyntax.NativeString(WildcardSyntax.Explicit))).ShouldBe("'a\\\\b'");
    }

    [Test]
    public void ANativeStringStillRecognizesReferencesAndWildcards()
    {
        // Sections 9.1 and 10: native strings use "the same strict reference and value-escape
        // lexer as namespace values"; only the escape table differs.
        Describe(Lex("${a}", ValueSyntax.NativeString(WildcardSyntax.Explicit))).ShouldBe("ref(a)");
        Describe(Lex("x*y", ValueSyntax.NativeString(WildcardSyntax.Unnamed))).ShouldBe("'x'|<*>|'y'");
    }

    // Structural identity.

    [Test]
    public void EqualValuesAreEqualAndHashAlike()
    {
        var left = LexExplicit("a${b.c}d*[0]");
        var right = LexExplicit("a${b.c}d*[0]");

        left.ShouldBe(right);
        left.GetHashCode().ShouldBe(right.GetHashCode());
    }

    [Test]
    public void ALiteralIsNotAReferenceThatRendersTheSameWay() =>
        Lex("\\${a}").ShouldNotBe(Lex("${a}"));

    [Test]
    public void ALiteralAsteriskIsNotAWildcard() =>
        Lex("\\*").ShouldNotBe(Lex("*"));

    [Test]
    public void LiteralTextIsReportedOnlyWhenNothingIsInterpreted()
    {
        Lex("plain").LiteralText.ShouldBe("plain");
        Lex("a${b}").LiteralText.ShouldBeNull();
        Lex("a*b").LiteralText.ShouldBeNull();
    }

    // Totality.

    [Test]
    public void LexingIsTotal()
    {
        string[] alphabet = ["a", "\\", "$", "{", "}", "*", "[", "]", "0", ".", "n", "Q", "@", "#"];
        ValueSyntax[] syntaxes =
        [
            ValueSyntax.Profile(WildcardSyntax.Unnamed),
            ValueSyntax.Profile(WildcardSyntax.Explicit),
            ValueSyntax.Profile(WildcardSyntax.None),
            ValueSyntax.ProfileUninterpreted,
            ValueSyntax.NativeString(WildcardSyntax.Explicit),
        ];
        var random = new Random(20260806);

        for (var i = 0; i < 20000; i++)
        {
            var length = random.Next(10);
            var text = string.Concat(Enumerable.Range(0, length).Select(_ => alphabet[random.Next(alphabet.Length)]));
            var syntax = syntaxes[random.Next(syntaxes.Length)];
            var result = ValueLexer.Lex(text, syntax);

            (result.Value is not null ^ result.Fault is not null).ShouldBeTrue(text);
            if (result.Fault is { } fault)
            {
                fault.Offset.ShouldBeInRange(0, text.Length);
            }
        }
    }

    [Test]
    public void AnUninterpretedValueNeverFaults()
    {
        string[] alphabet = ["a", "\\", "$", "{", "}", "*", "[", "]", "0", "."];
        var random = new Random(20260807);

        for (var i = 0; i < 5000; i++)
        {
            var length = random.Next(10);
            var text = string.Concat(Enumerable.Range(0, length).Select(_ => alphabet[random.Next(alphabet.Length)]));

            ValueLexer.Lex(text, ValueSyntax.ProfileUninterpreted).Fault.ShouldBeNull(text);
        }
    }
}
