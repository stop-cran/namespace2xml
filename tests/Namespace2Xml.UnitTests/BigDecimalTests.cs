using System.Globalization;
using System.Numerics;
using Namespace2Xml.Scalars;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Specification Section 18, clause by clause.
/// </summary>
/// <remarks>
/// Every expectation below is read off the specification text, not off the implementation. Section 18
/// numbers its canonical-text rules 1 through 7, and each region of this fixture names the step it
/// exercises so that a future reader can check the expectation against the contract rather than against
/// what the code happens to do. See CONTRIBUTING.md rule C1.
/// </remarks>
[TestFixture]
public class BigDecimalTests
{
    private static BigDecimal Of(string coefficient, string exponent) =>
        BigDecimal.FromCoefficientAndExponent(
            BigInteger.Parse(coefficient, CultureInfo.InvariantCulture),
            BigInteger.Parse(exponent, CultureInfo.InvariantCulture));

    // ---- Step 2: zero is 0.0 or -0.0 according to its retained sign ----

    [Test]
    public void PositiveZeroIsRenderedWithItsSign() =>
        BigDecimal.Zero.ToCanonicalText().ShouldBe("0.0");

    [Test]
    public void NegativeZeroIsRenderedWithItsSign() =>
        BigDecimal.NegativeZero.ToCanonicalText().ShouldBe("-0.0");

    [Test]
    public void ZeroDiscardsItsExponent()
    {
        Of("0", "5").ToCanonicalText().ShouldBe("0.0");
        Of("0", "-5").ToCanonicalText().ShouldBe("0.0");
        BigDecimal.FromSignedMagnitude(true, BigInteger.Zero, 5).ToCanonicalText().ShouldBe("-0.0");
    }

    [Test]
    public void ADefaultValueIsPositiveZero()
    {
        default(BigDecimal).IsZero.ShouldBeTrue();
        default(BigDecimal).ToCanonicalText().ShouldBe("0.0");
    }

    // ---- Step 3: trailing coefficient zeros are removed, and source spelling is never retained ----

    [Test]
    public void TrailingCoefficientZerosAreRemovedAndTheExponentRises()
    {
        BigDecimal value = Of("123450", "-3");

        value.Coefficient.ShouldBe(new BigInteger(12345));
        value.Exponent.ShouldBe(new BigInteger(-2));
    }

    [Test]
    public void TheThreeSpellingsOfOneAreOneValue()
    {
        // Section 18: "decimal 1.0 remains 1.0", and numeric source spelling is never retained.
        BigDecimal fromPointZero = Of("10", "-1");
        BigDecimal fromPointZeroZero = Of("100", "-2");
        BigDecimal fromExponent = Of("1", "0");

        fromPointZero.ShouldBe(fromPointZeroZero);
        fromPointZero.ShouldBe(fromExponent);
        fromPointZero.ToCanonicalText().ShouldBe("1.0");
        fromPointZeroZero.ToCanonicalText().ShouldBe("1.0");
        fromExponent.ToCanonicalText().ShouldBe("1.0");
    }

    [Test]
    public void EqualValuesAgreeOnTheirHashCode() =>
        Of("100", "-2").GetHashCode().ShouldBe(Of("1", "0").GetHashCode());

    // ---- Steps 4 and 5: the adjusted exponent decides plain versus scientific, at -6 and 20 ----

    [TestCase("1", "-6", "0.000001", Description = "adjusted exponent -6, the low plain boundary")]
    [TestCase("1", "-7", "1.0e-7", Description = "adjusted exponent -7, first scientific below")]
    [TestCase("1", "20", "100000000000000000000.0", Description = "adjusted exponent 20, the high plain boundary")]
    [TestCase("1", "21", "1.0e21", Description = "adjusted exponent 21, first scientific above")]
    [TestCase("123", "-8", "0.00000123", Description = "three digits, adjusted exponent -6")]
    [TestCase("123", "-9", "1.23e-7", Description = "three digits, adjusted exponent -7")]
    [TestCase("123", "18", "123000000000000000000.0", Description = "three digits, adjusted exponent 20")]
    [TestCase("123", "19", "1.23e21", Description = "three digits, adjusted exponent 21")]
    public void TheAdjustedExponentSelectsTheNotation(string coefficient, string exponent, string expected) =>
        Of(coefficient, exponent).ToCanonicalText().ShouldBe(expected);

    // ---- Step 6: scientific notation has one leading digit, a forced fraction digit, and a bare exponent ----

    [TestCase("1", "21", "1.0e21", Description = "at least one digit after the point, even when it is 0")]
    [TestCase("12345", "100", "1.2345e104")]
    [TestCase("-12345", "100", "-1.2345e104")]
    [TestCase("1", "-30", "1.0e-30")]
    public void ScientificNotationFollowsStepSix(string coefficient, string exponent, string expected)
    {
        string text = Of(coefficient, exponent).ToCanonicalText();

        text.ShouldBe(expected);

        // Section 18 step 6 fixes a lowercase 'e' and an exponent with no leading '+'.
        text.ShouldContain("e", Case.Sensitive);
        text.ShouldNotContain("E", Case.Sensitive);
        text.ShouldNotContain("e+", Case.Sensitive);
    }

    [Test]
    public void ScientificExponentsCarryNoRedundantZeros() =>
        Of("1", "100").ToCanonicalText().ShouldBe("1.0e100");

    // ---- Step 7: plain text that would read as an integer gains a .0 ----

    [TestCase("1", "0", "1.0")]
    [TestCase("12", "0", "12.0")]
    [TestCase("5", "3", "5000.0")]
    [TestCase("-1", "0", "-1.0")]
    public void PlainTextIndistinguishableFromAnIntegerGainsAFraction(
        string coefficient, string exponent, string expected) =>
        Of(coefficient, exponent).ToCanonicalText().ShouldBe(expected);

    [TestCase("12345", "-2", "123.45")]
    [TestCase("-12345", "-2", "-123.45")]
    [TestCase("5", "-1", "0.5")]
    [TestCase("15", "-4", "0.0015")]
    public void PlainTextWithAFractionIsLeftAlone(string coefficient, string exponent, string expected) =>
        Of(coefficient, exponent).ToCanonicalText().ShouldBe(expected);

    // ---- The exponent is not an Int32 ----

    [TestCase("10000000000", "1.0e10000000000", Description = "beyond Int32")]
    [TestCase("-10000000000", "1.0e-10000000000", Description = "beyond Int32, negative")]
    [TestCase("2147483647", "1.0e2147483647", Description = "exactly Int32.MaxValue")]
    [TestCase("2147483648", "1.0e2147483648", Description = "one past Int32.MaxValue")]
    [TestCase("-2147483648", "1.0e-2147483648", Description = "exactly Int32.MinValue")]
    [TestCase("-2147483649", "1.0e-2147483649", Description = "one past Int32.MinValue")]
    [TestCase("99999999999999999999999999999999", "1.0e99999999999999999999999999999999",
        Description = "beyond Int64 as well")]
    public void ExponentsBeyondInt32AreCarriedAndRendered(string exponent, string expected) =>
        Of("1", exponent).ToCanonicalText().ShouldBe(expected);

    [Test]
    public void AnEnormousExponentSurvivesParsing()
    {
        BigDecimal.TryParse("1e99999999999999999999", out BigDecimal value).ShouldBeTrue();
        value.ToCanonicalText().ShouldBe("1.0e99999999999999999999");
    }

    // ---- Section 18 is locale-independent, and Section 24 requires that of every output ----

    [Test]
    public void CanonicalTextIgnoresTheCurrentCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        var hostile = (CultureInfo)CultureInfo.InvariantCulture.Clone();

        hostile.NumberFormat.NegativeSign = "MINUS";
        hostile.NumberFormat.NumberDecimalSeparator = ",";
        hostile.NumberFormat.NumberGroupSeparator = "_";
        hostile.NumberFormat.NumberGroupSizes = [3];

        try
        {
            CultureInfo.CurrentCulture = hostile;

            Of("-12345", "-2").ToCanonicalText().ShouldBe("-123.45");
            Of("-1", "-30").ToCanonicalText().ShouldBe("-1.0e-30");
            BigDecimal.NegativeZero.ToCanonicalText().ShouldBe("-0.0");
            new BigInteger(-1234567).ToCanonicalText().ShouldBe("-1234567");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ---- Grammar rule 4: the accepted form is JSON's, exactly ----

    [TestCase("0", "0.0")]
    [TestCase("-0", "-0.0")]
    [TestCase("0.0", "0.0")]
    [TestCase("-0.0", "-0.0")]
    [TestCase("-0e5", "-0.0")]
    [TestCase("1", "1.0")]
    [TestCase("1.0", "1.0")]
    [TestCase("1.00", "1.0")]
    [TestCase("1e0", "1.0")]
    [TestCase("1E0", "1.0")]
    [TestCase("1e+0", "1.0")]
    [TestCase("-1.5e-3", "-0.0015")]
    [TestCase("123.450", "123.45")]
    [TestCase("1e100000000000", "1.0e100000000000")]
    public void AcceptedFormsParseToTheirCanonicalText(string text, string expected)
    {
        BigDecimal.TryParse(text, out BigDecimal value).ShouldBeTrue(text);
        value.ToCanonicalText().ShouldBe(expected);
    }

    [TestCase("", Description = "empty")]
    [TestCase("+1", Description = "JSON admits no leading plus")]
    [TestCase("01", Description = "JSON admits no leading zero")]
    [TestCase("-01", Description = "JSON admits no leading zero")]
    [TestCase("1.", Description = "JSON requires a digit after the point")]
    [TestCase(".5", Description = "JSON requires a digit before the point")]
    [TestCase("-.5", Description = "JSON requires a digit before the point")]
    [TestCase("1e", Description = "JSON requires an exponent digit")]
    [TestCase("1e+", Description = "JSON requires an exponent digit")]
    [TestCase("1e1.5", Description = "the exponent is an integer")]
    [TestCase("1.2.3", Description = "one point only")]
    [TestCase("--1")]
    [TestCase("-")]
    [TestCase(" 1", Description = "no surrounding space")]
    [TestCase("1 ", Description = "no surrounding space")]
    [TestCase("1_000", Description = "Section 18: thousands separators are not inferred")]
    [TestCase("1,5", Description = "Section 18: locale decimal commas are not inferred")]
    [TestCase("0x1F", Description = "Section 18: hexadecimal is not inferred")]
    [TestCase("NaN", Description = "Section 18: NaN is not inferred")]
    [TestCase("Infinity", Description = "Section 18: infinities are not inferred")]
    [TestCase("-Infinity", Description = "Section 18: infinities are not inferred")]
    public void RejectedFormsDoNotParse(string text) =>
        BigDecimal.TryParse(text, out _).ShouldBeFalse(text);

    // ---- Negative zero is retained, so it is not the same value as positive zero ----

    [Test]
    public void NegativeZeroIsNotPositiveZero()
    {
        BigDecimal.NegativeZero.ShouldNotBe(BigDecimal.Zero);
        (BigDecimal.NegativeZero == BigDecimal.Zero).ShouldBeFalse();
        (BigDecimal.NegativeZero != BigDecimal.Zero).ShouldBeTrue();
        BigDecimal.NegativeZero.IsNegative.ShouldBeTrue();
        BigDecimal.Zero.IsNegative.ShouldBeFalse();
        BigDecimal.NegativeZero.IsNegativeZero.ShouldBeTrue();
    }

    [Test]
    public void ANegativeSignOnANonzeroValueIsNotNegativeZero()
    {
        Of("-1", "0").IsNegativeZero.ShouldBeFalse();
        Of("-1", "0").IsNegative.ShouldBeTrue();
    }

    [Test]
    public void ACoefficientMustNotBeSignedAndSeparatelySigned() =>
        Should.Throw<ArgumentOutOfRangeException>(
            () => BigDecimal.FromSignedMagnitude(false, new BigInteger(-1), BigInteger.Zero));

    // ---- Section 18's closing requirement: one text, used by every format ----

    [TestCase("0", "0")]
    [TestCase("-0", "0", Description = "an integer has no negative zero; only Section 18 step 2 does")]
    [TestCase("12345678901234567890123456789", "12345678901234567890123456789")]
    [TestCase("-12345678901234567890123456789", "-12345678901234567890123456789")]
    public void IntegerTextIsPlainBaseTen(string value, string expected) =>
        BigInteger.Parse(value, CultureInfo.InvariantCulture).ToCanonicalText().ShouldBe(expected);

    [Test]
    public void ToStringIsTheCanonicalText() =>
        Of("12345", "-2").ToString().ShouldBe("123.45");
}
