using Namespace2Xml.Output;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// The Section 10.1 <c>RestrictedYaml1</c> resolution rules read backwards: which strings a
/// Section 19.4 writer must quote so that reading its own output returns a string.
/// </summary>
/// <remarks>
/// Every expectation here is authored from Section 10.1 and Section 19.4, never from what the
/// writer currently produces.
/// </remarks>
[TestFixture]
public class YamlScalarTextTests
{
    /// <summary>
    /// Section 10.1: "<c>true</c> and <c>false</c>, case-insensitively, are Boolean", and Section
    /// 10.1's closing note names <c>tRuE</c> as the deliberate difference from namespace inference.
    /// </summary>
    /// <param name="text">A spelling that resolves to a Boolean.</param>
    [TestCase("true")]
    [TestCase("false")]
    [TestCase("True")]
    [TestCase("FALSE")]
    [TestCase("tRuE")]
    public void EveryAsciiCaseSpellingOfATruthValueResolvesToBoolean(string text) =>
        YamlScalarText.ResolvesToNonString(text).ShouldBeTrue();

    /// <summary>
    /// Section 10.1: "<c>null</c>, <c>Null</c>, <c>NULL</c>, <c>~</c>, or an empty plain scalar are
    /// null". The list is exhaustive, so <c>nULL</c> is not one of them.
    /// </summary>
    /// <param name="text">A spelling that resolves to null.</param>
    [TestCase("null")]
    [TestCase("Null")]
    [TestCase("NULL")]
    [TestCase("~")]
    [TestCase("")]
    public void OnlyTheFourNamedNullSpellingsAndAnEmptyScalarResolveToNull(string text) =>
        YamlScalarText.ResolvesToNonString(text).ShouldBeTrue();

    /// <summary>
    /// Section 10.1 spells the null aliases as a closed list rather than case-insensitively, unlike
    /// the Boolean rule immediately above it. A writer that treated them alike would quote strings
    /// it does not need to.
    /// </summary>
    /// <param name="text">A case variant Section 10.1 does not list.</param>
    [TestCase("nULL")]
    [TestCase("nuLL")]
    [TestCase("NuLl")]
    public void UnlistedCaseVariantsOfNullRemainStrings(string text) =>
        YamlScalarText.ResolvesToNonString(text).ShouldBeFalse();

    /// <summary>
    /// Section 10.1: "values such as <c>yes</c>, <c>no</c>, <c>on</c>, <c>off</c>, timestamps, and
    /// sexagesimal numbers remain strings". These are the YAML 1.1 resolutions
    /// <c>RestrictedYaml1</c> deliberately drops.
    /// </summary>
    /// <param name="text">A spelling YAML 1.1 would resolve but Section 10.1 does not.</param>
    [TestCase("yes")]
    [TestCase("no")]
    [TestCase("on")]
    [TestCase("off")]
    [TestCase("Yes")]
    [TestCase("OFF")]
    [TestCase("2001-12-14")]
    [TestCase("190:20:30")]
    [TestCase("y")]
    [TestCase("n")]
    public void TheYamlOneDotOneResolutionsRemainStrings(string text) =>
        YamlScalarText.ResolvesToNonString(text).ShouldBeFalse();

    /// <summary>
    /// Section 10.1: "JSON-compatible decimal integers and floating-point values are numeric", and
    /// its closing note says "plain YAML <c>+1</c>, <c>.5</c>, and <c>1.</c> remain strings because
    /// they are not JSON-compatible numbers".
    /// </summary>
    /// <param name="text">A number spelling.</param>
    /// <param name="numeric">Whether Section 10.1 resolves it as a number.</param>
    [TestCase("0", true)]
    [TestCase("-0", true)]
    [TestCase("42", true)]
    [TestCase("-42", true)]
    [TestCase("1.5", true)]
    [TestCase("-1.5", true)]
    [TestCase("1e3", true)]
    [TestCase("1E3", true)]
    [TestCase("1.5e-3", true)]
    [TestCase("1.5E+3", true)]
    [TestCase("0.5", true)]
    [TestCase("+1", false)]
    [TestCase(".5", false)]
    [TestCase("1.", false)]
    [TestCase("01", false)]
    [TestCase("-01", false)]
    [TestCase("0x1F", false)]
    [TestCase("0o17", false)]
    [TestCase("1_000", false)]
    [TestCase(".inf", false)]
    [TestCase("-.inf", false)]
    [TestCase(".nan", false)]
    [TestCase("1e", false)]
    [TestCase("1e+", false)]
    [TestCase("", false)]
    public void OnlyJsonCompatibleNumbersResolveAsNumeric(string text, bool numeric) =>
        YamlScalarText.IsJsonNumber(text).ShouldBe(numeric);

    /// <summary>
    /// Section 19.4 quotes a string "whose plain spelling would resolve to a non-string kind", and
    /// Section 19.4 prefers the single-quoted form. The doubling rule is YAML's own: a single quote
    /// inside a single-quoted scalar is written twice.
    /// </summary>
    /// <param name="text">A string whose plain spelling would resolve to something else.</param>
    /// <param name="expected">The Section 19.4 spelling.</param>
    [TestCase("true", "'true'")]
    [TestCase("tRuE", "'tRuE'")]
    [TestCase("null", "'null'")]
    [TestCase("~", "'~'")]
    [TestCase("", "''")]
    [TestCase("42", "'42'")]
    [TestCase("1.5e-3", "'1.5e-3'")]
    public void AStringThatWouldResolveOtherwiseIsSingleQuoted(string text, string expected) =>
        YamlScalarText.Spell(text).ShouldBe(expected);

    /// <summary>
    /// A string that resolves to itself and is typed by neither published schema needs no quoting,
    /// so Section 19.4's plain form applies. Every numeric production in the portably typed set
    /// requires at least one digit, so a prefix or a separator with nothing behind it stays plain,
    /// and a sexagesimal group needs a value below sixty.
    /// </summary>
    /// <param name="text">A string with a safe plain spelling.</param>
    [TestCase("hello")]
    [TestCase("a b")]
    [TestCase(".")]
    [TestCase("._")]
    [TestCase("+.")]
    [TestCase(".x")]
    [TestCase("a:b")]
    [TestCase("1:60")]
    [TestCase("99:99")]
    [TestCase("0b")]
    [TestCase("0x")]
    [TestCase("0o")]
    [TestCase("1e")]
    [TestCase("_")]
    public void AStringWithASafePlainSpellingIsNotQuoted(string text) =>
        YamlScalarText.Spell(text).ShouldBe(text);

    /// <summary>
    /// Section 19.4's portably typed set is the union of the YAML 1.2 Core Schema's resolutions and
    /// the YAML 1.1 type repository's, so a spelling either revision types is quoted even where
    /// Section 10.1 keeps it a string. Read back plain, one of these returns a number, a Boolean or
    /// a date instead of the string that was written.
    /// </summary>
    /// <param name="text">A spelling one of the two published schemas types.</param>
    [TestCase("yes")]
    [TestCase("no")]
    [TestCase("on")]
    [TestCase("off")]
    [TestCase("Yes")]
    [TestCase("OFF")]
    [TestCase("y")]
    [TestCase("N")]
    [TestCase("yEs")]
    [TestCase("+1")]
    [TestCase(".5")]
    [TestCase("1.")]
    [TestCase("010")]
    [TestCase("0x1F")]
    [TestCase("0o17")]
    [TestCase("0b101")]
    [TestCase("1_000")]
    [TestCase("0x_")]
    [TestCase("12:30")]
    [TestCase("190:20:30")]
    [TestCase(".inf")]
    [TestCase("-.inf")]
    [TestCase(".NaN")]
    [TestCase("2001-12-14")]
    [TestCase("2001-12-14T21:59:43.10-05:00")]
    public void APortablyTypedSpellingIsQuotedThoughSectionTenKeepsItAString(string text) =>
        YamlScalarText.Spell(text).ShouldBe("'" + text + "'");

    /// <summary>
    /// The productions match "on the value's exact characters", so a trailing line break is not an
    /// end of input. A .NET <c>$</c> anchor matches before one, which would report a value carrying
    /// a line break as portably typed and, read backwards, as safe to spell plain.
    /// </summary>
    /// <param name="text">A typed spelling followed by a line break.</param>
    [TestCase("42\n")]
    [TestCase("yes\n")]
    [TestCase("2001-12-14\n")]
    public void ATrailingLineBreakIsNotAnEndOfInputForTheProductions(string text) =>
        YamlScalarText.IsPortablyTyped(text).ShouldBeFalse();

    /// <summary>
    /// The two key tags cost a refused document rather than a changed value: a YAML 1.1 reader
    /// resolves them to <c>tag:yaml.org,2002:merge</c> and <c>tag:yaml.org,2002:value</c>, and a
    /// reader with no constructor for those tags abandons the whole load, losing every sibling
    /// entry with them. Section 19.4 spells a key by these same rules, so both positions are
    /// covered.
    /// </summary>
    /// <param name="text">A spelling a reader resolves to a key tag.</param>
    [TestCase("<<")]
    [TestCase("=")]
    public void AKeyTagSpellingIsQuotedInEitherPosition(string text) =>
        YamlScalarText.Spell(text).ShouldBe("'" + text + "'");

    /// <summary>
    /// A plain scalar must also be syntactically plain. YAML gives these characters indicator
    /// meaning at the start of a scalar, so a writer that only consulted Section 10.1 resolution
    /// would emit text a parser reads as structure.
    /// </summary>
    /// <param name="text">A string a plain scalar cannot spell.</param>
    [TestCase("- item")]
    [TestCase("? key")]
    [TestCase(": value")]
    [TestCase("#comment")]
    [TestCase("[a]")]
    [TestCase("{a}")]
    [TestCase("&anchor")]
    [TestCase("*alias")]
    [TestCase("!tag")]
    [TestCase("|block")]
    [TestCase(">fold")]
    [TestCase("'quoted")]
    [TestCase("\"quoted")]
    [TestCase("%directive")]
    [TestCase("@reserved")]
    [TestCase("`reserved")]
    [TestCase("a: b")]
    [TestCase("a #b")]
    [TestCase("trailing:")]
    [TestCase(" leading")]
    [TestCase("trailing ")]
    [TestCase("-.")]
    public void AStringNeedingIndicatorCharactersIsNotPlain(string text) =>
        YamlScalarText.IsPlainSafe(text).ShouldBeFalse();

    /// <summary>
    /// Section 19.4's fallback ladder ends at the double-quoted form, which can spell anything. A
    /// line break cannot appear in a single-quoted scalar without folding, so a string carrying one
    /// falls through.
    /// </summary>
    [Test]
    public void AStringCarryingALineBreakCannotBeSingleQuoted() =>
        YamlScalarText.CanSingleQuote("a\nb").ShouldBeFalse();

    /// <summary>
    /// A control character is not YAML <c>c-printable</c>, so it cannot appear literally in a plain
    /// or single-quoted scalar and must be escaped.
    /// </summary>
    [Test]
    public void AControlCharacterForcesTheDoubleQuotedForm() =>
        YamlScalarText.Spell("a\u0007b").ShouldBe("\"a\\u0007b\"");

    /// <summary>
    /// The double-quoted form escapes the two characters that would end it or begin an escape, and
    /// spells a tab as <c>\t</c>, which keeps trailing whitespace visible to a reader and exact to a
    /// parser.
    /// </summary>
    /// <param name="text">The string being spelled.</param>
    /// <param name="expected">The double-quoted spelling.</param>
    [TestCase("a\"b", "\"a\\\"b\"")]
    [TestCase("a\\b", "\"a\\\\b\"")]
    [TestCase("a\tb", "\"a\\tb\"")]
    [TestCase("a\nb", "\"a\\nb\"")]
    [TestCase("a\rb", "\"a\\rb\"")]
    public void TheDoubleQuotedFormEscapesWhatWouldEndIt(string text, string expected) =>
        YamlScalarText.DoubleQuote(text).ShouldBe(expected);

    /// <summary>
    /// Section 19.4 emits UTF-8, so a printable non-ASCII character needs no escape and a plain
    /// scalar can carry it.
    /// </summary>
    [Test]
    public void PrintableNonAsciiTextStaysLiteral() =>
        YamlScalarText.Spell("caf\u00e9 \U0001F600").ShouldBe("caf\u00e9 \U0001F600");

    /// <summary>
    /// A block scalar reproduces its content literally, so it is only available when every line can
    /// be written literally. Trailing whitespace on a line would be invisible and is stripped by
    /// some readers, and a first line beginning with a space would need an explicit indentation
    /// indicator. A value ending in a blank line is excluded for a different reason, recorded on
    /// <see cref="ABlockScalarIsDeclinedForAValueEndingInABlankLine"/>.
    /// </summary>
    /// <param name="text">Multi-line text.</param>
    /// <param name="eligible">Whether a literal block scalar can carry it exactly.</param>
    [TestCase("a\nb", true)]
    [TestCase("a\nb\n", true)]
    [TestCase("a\n\nb", true)]
    [TestCase("a\nb\n\n", false)]
    [TestCase("a \nb", false)]
    [TestCase("a\nb\t", false)]
    [TestCase(" a\nb", false)]
    [TestCase("\ta\nb", false)]
    [TestCase("a\r\nb", false)]
    [TestCase("a\u0007\nb", false)]
    [TestCase("single line", false)]
    [TestCase("", false)]
    public void ABlockScalarIsAvailableOnlyWhenItIsExact(string text, bool eligible) =>
        YamlScalarText.CanBlock(text).ShouldBe(eligible);

    /// <summary>
    /// An empty line inside otherwise indented block content is still block-eligible: the writer
    /// emits it as a truly empty line rather than as indentation, so nothing invisible is added.
    /// </summary>
    [Test]
    public void AnInteriorEmptyLineDoesNotDisqualifyABlockScalar() =>
        YamlScalarText.CanBlock("first\n\nthird\n").ShouldBeTrue();
    /// <summary>
    /// A reader detects a literal block scalar's indentation from its <em>first non-empty</em>
    /// line and strips exactly that much from every line. Leading white space there is therefore
    /// absorbed, so Section 3.3's "writing a format it read preserves the data" forbids a block
    /// spelling. A value whose first line is empty hides this: the first character is the line
    /// break, and only the second line carries the space.
    /// </summary>
    /// <param name="text">A value whose first non-empty line begins with white space.</param>
    [TestCase("\n leading")]
    [TestCase("\n\n  two")]
    [TestCase("\n\ttabbed")]
    [TestCase(" first\nsecond")]
    public void AnIndentedFirstNonEmptyLineDisqualifiesABlockScalar(string text) =>
        YamlScalarText.CanBlock(text).ShouldBeFalse();

    /// <summary>
    /// The converse: an empty leading line is harmless when the first line carrying content starts
    /// at column zero, because the detected indentation is then the writer's own.
    /// </summary>
    [TestCase("\nleading")]
    [TestCase("\n\ntwo")]
    public void AnEmptyLeadingLineAloneDoesNotDisqualifyABlockScalar(string text) =>
        YamlScalarText.CanBlock(text).ShouldBeTrue();

    /// <summary>
    /// A line whose content is <c>...</c> is YAML's document-end marker. A bare scalar occupies a
    /// line of its own, so a plain spelling would end the document and leave it with no value —
    /// losing the string entirely rather than quoting it.
    /// </summary>
    /// <param name="text">A value a reader would take for a document-end marker.</param>
    [TestCase("...")]
    [TestCase("... trailing")]
    public void ADocumentEndMarkerIsNeverSpelledPlain(string text) =>
        YamlScalarText.IsPlainSafe(text).ShouldBeFalse();

    /// <summary>
    /// U+FEFF is a byte order mark at the start of a stream and Section 24 requires UTF-8 "without
    /// a BOM", so a reader either rejects the file or discards the character. It can never be
    /// written as itself, in any spelling.
    /// </summary>
    [Test]
    public void AByteOrderMarkIsNeverWrittenAsItself()
    {
        const string Text = "\uFEFFx";

        YamlScalarText.IsPlainSafe(Text).ShouldBeFalse();
        YamlScalarText.CanSingleQuote(Text).ShouldBeFalse();
        YamlScalarText.CanBlock("\uFEFFa\nb").ShouldBeFalse();
        YamlScalarText.Spell(Text).ShouldBe("\"\\uFEFFx\"");
    }

    /// <summary>
    /// A supplementary character is one Unicode scalar value. Splitting it into two <c>\u</c>
    /// escapes spells two unpaired surrogates, which is a different string: Section 19.4 preserves
    /// the scalar, so it is written as itself in every spelling that admits non-ASCII text.
    /// </summary>
    [Test]
    public void ASupplementaryCharacterSurvivesEverySpelling()
    {
        const string Emoji = "\uD83D\uDE00";

        YamlScalarText.IsPlainSafe(Emoji).ShouldBeTrue();
        YamlScalarText.CanSingleQuote("-" + Emoji).ShouldBeTrue();
        YamlScalarText.Spell("-" + Emoji).ShouldBe("'-" + Emoji + "'");
        YamlScalarText.DoubleQuote(Emoji).ShouldBe("\"" + Emoji + "\"");
        YamlScalarText.CanBlock(Emoji + "\nsecond").ShouldBeTrue();
    }

    /// <summary>
    /// A surrogate with no partner is not a Unicode scalar value, so no YAML spelling carries it.
    /// It is escaped rather than written raw, which at least keeps the file well formed.
    /// </summary>
    [Test]
    public void ALoneSurrogateIsEscapedRatherThanWritten()
    {
        const string Lone = "a\uD83Db";

        YamlScalarText.IsPlainSafe(Lone).ShouldBeFalse();
        YamlScalarText.CanSingleQuote(Lone).ShouldBeFalse();
        YamlScalarText.CanBlock(Lone + "\nsecond").ShouldBeFalse();
        YamlScalarText.Spell(Lone).ShouldBe("\"a\\uD83Db\"");
    }

    /// <summary>
    /// YAML normalizes U+2028 and U+2029 as line breaks exactly as it does LF, so writing either
    /// as itself ends the line and the surrounding spelling with it — a plain or single-quoted
    /// scalar becomes a syntax error, and a block scalar silently gains a line. Both are outside
    /// the C0 and C1 ranges, in categories Zl and Zp, so <c>char.IsControl</c> returns false for
    /// them and the control-character guards do not cover them.
    /// </summary>
    /// <param name="separator">A character YAML treats as a line break.</param>
    [TestCase('\u2028')]
    [TestCase('\u2029')]
    public void ALineSeparatorIsTreatedAsALineBreakRatherThanAsText(char separator)
    {
        string text = "a" + separator + "b";

        char.IsControl(separator).ShouldBeFalse();
        YamlScalarText.IsPlainSafe(text).ShouldBeFalse();
        YamlScalarText.CanSingleQuote(text).ShouldBeFalse();
        YamlScalarText.CanBlock(text + "\nsecond").ShouldBeFalse();
    }

    /// <summary>
    /// Only the double-quoted form admits an escape, so it is where a line separator has to land.
    /// </summary>
    /// <param name="text">Text carrying a line separator.</param>
    /// <param name="expected">Its double-quoted spelling.</param>
    [TestCase("a\u2028b", "\"a\\u2028b\"")]
    [TestCase("a\u2029b", "\"a\\u2029b\"")]
    [TestCase("\u2028", "\"\\u2028\"")]
    public void ALineSeparatorIsEscapedInTheDoubleQuotedForm(string text, string expected) =>
        YamlScalarText.Spell(text).ShouldBe(expected);
}
