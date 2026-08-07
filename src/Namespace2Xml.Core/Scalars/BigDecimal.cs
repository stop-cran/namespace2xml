using System.Globalization;
using System.Numerics;

namespace Namespace2Xml.Scalars;

/// <summary>
/// An arbitrary-precision decimal, as required by specification Section 18.
/// </summary>
/// <remarks>
/// <para>
/// No BCL type qualifies. <see cref="decimal"/> caps at 28 to 29 significant digits and cannot
/// represent negative zero, which Section 18 step 2 requires to survive to output. <see cref="double"/>
/// is binary and loses the source value outright.
/// </para>
/// <para>
/// The exponent is a <see cref="BigInteger"/> rather than an <see cref="int"/>. Section 18 step 6
/// admits any exponent an input can spell, and a JSON or namespace source may spell one far beyond
/// <see cref="int.MaxValue"/>. Nothing truncates it, so nothing has to decide what truncation would mean.
/// </para>
/// <para>
/// Values are normalized on construction: trailing coefficient zeros are removed and the exponent is
/// increased by the same count, per Section 18 step 3. Section 18 states that numeric source spelling
/// is never retained, so <c>1.0</c>, <c>1.00</c> and <c>1e0</c> are one value with one canonical text.
/// </para>
/// <para>
/// Negative zero is retained and is <b>not</b> equal to positive zero. The two have different canonical
/// texts, so treating them as equal would let a value compare equal to one it does not render like,
/// which is the kind of seam that produces a nondeterministic output diff.
/// </para>
/// </remarks>
public readonly struct BigDecimal : IEquatable<BigDecimal>
{
    /// <summary>Lowest adjusted exponent that still uses plain notation, per Section 18 step 5.</summary>
    private const int MinimumPlainAdjustedExponent = -6;

    /// <summary>Highest adjusted exponent that still uses plain notation, per Section 18 step 5.</summary>
    private const int MaximumPlainAdjustedExponent = 20;

    private readonly BigInteger coefficient;
    private readonly BigInteger exponent;
    private readonly bool negativeZero;

    private BigDecimal(BigInteger coefficient, BigInteger exponent, bool negativeZero)
    {
        this.coefficient = coefficient;
        this.exponent = exponent;
        this.negativeZero = negativeZero;
    }

    /// <summary>Positive zero, whose canonical text is <c>0.0</c>.</summary>
    public static BigDecimal Zero => default;

    /// <summary>Negative zero, whose canonical text is <c>-0.0</c>, per Section 18 step 2.</summary>
    public static BigDecimal NegativeZero { get; } = new(BigInteger.Zero, BigInteger.Zero, true);

    /// <summary>
    /// The normalized signed coefficient. For a nonzero value this never ends in a zero digit,
    /// because Section 18 step 3 removes trailing zeros.
    /// </summary>
    public BigInteger Coefficient => coefficient;

    /// <summary>The normalized base-10 exponent. Always zero when the value is zero.</summary>
    public BigInteger Exponent => exponent;

    /// <summary>Whether this value is zero, of either sign.</summary>
    public bool IsZero => coefficient.IsZero;

    /// <summary>Whether this value is the retained negative zero of Section 18 step 2.</summary>
    public bool IsNegativeZero => coefficient.IsZero && negativeZero;

    /// <summary>Whether this value carries a minus sign, including negative zero.</summary>
    public bool IsNegative => coefficient.Sign < 0 || IsNegativeZero;

    /// <summary>
    /// Builds a value from a signed coefficient and an exponent. A zero coefficient yields positive
    /// zero; use <see cref="FromSignedMagnitude"/> to build negative zero.
    /// </summary>
    public static BigDecimal FromCoefficientAndExponent(BigInteger coefficient, BigInteger exponent) =>
        Normalize(coefficient, exponent, negativeZero: false);

    /// <summary>
    /// Builds a value from an explicit sign and a nonnegative magnitude. This is the form a parser
    /// needs, because a source may spell <c>-0.0</c>, whose sign is not recoverable from the magnitude.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude is negative.</exception>
    public static BigDecimal FromSignedMagnitude(bool isNegative, BigInteger magnitude, BigInteger exponent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(magnitude.Sign, 0, nameof(magnitude));

        return Normalize(isNegative ? -magnitude : magnitude, exponent, isNegative);
    }

    /// <summary>
    /// Removes trailing zeros from a coefficient, raising the exponent to compensate.
    /// </summary>
    /// <param name="coefficient">The signed coefficient.</param>
    /// <param name="exponent">Its exponent.</param>
    /// <param name="negativeZero">Whether a zero result is negative zero.</param>
    /// <remarks>
    /// Dividing by ten once per trailing zero is quadratic in the coefficient's digit count,
    /// because every division touches the whole remaining value. Section 23 budgets ordinary
    /// parsing at <c>O((N + P + E) log N + C)</c> and requires that "the implementation must avoid
    /// an unconditional <c>O(n²)</c> scan", and the input that provokes it is a single ordinary
    /// JSON number: <c>1.</c> followed by 128K zeros is a well-formed 128 KiB document.
    /// <para>
    /// This instead finds the number of trailing zeros by doubling — testing 10^1, 10^2, 10^4 and
    /// so on until one does not divide evenly, then binary-searching the last power that does —
    /// and performs a single division. That is <c>O(log n)</c> divisions rather than <c>n</c>.
    /// </para>
    /// </remarks>
    private static BigDecimal Normalize(BigInteger coefficient, BigInteger exponent, bool negativeZero)
    {
        if (coefficient.IsZero)
        {
            return negativeZero ? NegativeZero : Zero;
        }

        var zeros = TrailingZeros(coefficient);

        if (zeros > 0)
        {
            coefficient /= BigInteger.Pow(Ten, zeros);
            exponent += zeros;
        }

        return new BigDecimal(coefficient, exponent, negativeZero: false);
    }

    /// <summary>The number of decimal trailing zeros of a non-zero coefficient.</summary>
    /// <param name="coefficient">The signed coefficient, which must not be zero.</param>
    private static int TrailingZeros(BigInteger coefficient)
    {
        if (!(coefficient % Ten).IsZero)
        {
            return 0;
        }

        // Double until a power of ten no longer divides evenly, so the search range is found in
        // O(log n) divisions rather than by stepping one digit at a time.
        var low = 1;
        var high = 2;
        var power = BigInteger.Pow(Ten, high);

        while ((coefficient % power).IsZero)
        {
            low = high;

            if (high > int.MaxValue / 2)
            {
                return low;
            }

            high *= 2;
            power = BigInteger.Pow(Ten, high);
        }

        // The answer is in [low, high); halve the range the same way.
        while (low + 1 < high)
        {
            var middle = low + ((high - low) / 2);

            if ((coefficient % BigInteger.Pow(Ten, middle)).IsZero)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static readonly BigInteger Ten = 10;

    /// <summary>
    /// Parses the JSON-compatible decimal or exponent form named by Section 18 grammar rule 4.
    /// </summary>
    /// <remarks>
    /// The accepted grammar is JSON's, exactly: an optional <c>-</c>, an integer part that is either a
    /// lone <c>0</c> or a nonzero leading digit followed by any digits, an optional fraction of at least
    /// one digit, and an optional <c>e</c> or <c>E</c> exponent with an optional sign and at least one
    /// digit. A leading <c>+</c>, a leading zero such as <c>01</c>, a trailing point such as <c>1.</c>,
    /// and a bare fraction such as <c>.5</c> are all rejected: JSON does not admit them, and Section 18
    /// rule 4 says JSON-compatible.
    /// <para>
    /// Choosing between Section 18's five grammar rules is scalar inference, which is pipeline step 12
    /// and not this type's concern. This method answers only whether the text is rule 4's form.
    /// </para>
    /// </remarks>
    public static bool TryParse(ReadOnlySpan<char> text, out BigDecimal value)
    {
        value = default;

        if (text.IsEmpty)
        {
            return false;
        }

        int position = 0;
        bool isNegative = text[0] == '-';

        if (isNegative)
        {
            position++;
        }

        int integerStart = position;

        if (position >= text.Length || !IsAsciiDigit(text[position]))
        {
            return false;
        }

        if (text[position] == '0')
        {
            position++;
        }
        else
        {
            while (position < text.Length && IsAsciiDigit(text[position]))
            {
                position++;
            }
        }

        ReadOnlySpan<char> integerDigits = text[integerStart..position];
        ReadOnlySpan<char> fractionDigits = default;

        if (position < text.Length && text[position] == '.')
        {
            position++;
            int fractionStart = position;

            while (position < text.Length && IsAsciiDigit(text[position]))
            {
                position++;
            }

            if (position == fractionStart)
            {
                return false;
            }

            fractionDigits = text[fractionStart..position];
        }

        BigInteger explicitExponent = BigInteger.Zero;

        if (position < text.Length && (text[position] == 'e' || text[position] == 'E'))
        {
            position++;

            bool exponentIsNegative = position < text.Length && text[position] == '-';

            if (position < text.Length && (text[position] == '+' || text[position] == '-'))
            {
                position++;
            }

            int exponentStart = position;

            while (position < text.Length && IsAsciiDigit(text[position]))
            {
                position++;
            }

            if (position == exponentStart)
            {
                return false;
            }

            BigInteger exponentMagnitude = ParseDigits(text[exponentStart..position]);
            explicitExponent = exponentIsNegative ? -exponentMagnitude : exponentMagnitude;
        }

        if (position != text.Length)
        {
            return false;
        }

        // Section 23 budgets ordinary parsing at O((N + P + E) log N + C). Trailing zeros are
        // removed here, on the text, so the coefficient is built only from the digits that survive:
        // "1." followed by 128K zeros is a well-formed 128 KiB JSON number, and constructing its
        // full 128K-digit coefficient only to divide the zeros back out is superlinear work any
        // input can ask for. Normalize still guards the two public factories, which are handed a
        // coefficient that already exists.
        int totalDigits = integerDigits.Length + fractionDigits.Length;
        int strippable = TrailingZeroDigits(integerDigits, fractionDigits);

        // One digit always remains, so an all-zero coefficient still parses as the digit '0'.
        int zeros = Math.Min(strippable, totalDigits - 1);
        int fractionLength = fractionDigits.Length;

        if (zeros <= fractionDigits.Length)
        {
            fractionDigits = fractionDigits[..(fractionDigits.Length - zeros)];
        }
        else
        {
            integerDigits = integerDigits[..(totalDigits - zeros)];
            fractionDigits = default;
        }

        BigInteger magnitude = fractionDigits.IsEmpty
            ? ParseDigits(integerDigits)
            : ParseDigits(string.Concat(integerDigits, fractionDigits));

        value = FromSignedMagnitude(
            isNegative, magnitude, explicitExponent - fractionLength + zeros);
        return true;
    }

    /// <summary>
    /// The number of trailing zero digits of the coefficient the two digit spans spell together.
    /// </summary>
    /// <param name="integerDigits">The digits before the decimal point.</param>
    /// <param name="fractionDigits">The digits after it, which may be empty.</param>
    private static int TrailingZeroDigits(
        ReadOnlySpan<char> integerDigits, ReadOnlySpan<char> fractionDigits)
    {
        var zeros = 0;

        for (var i = fractionDigits.Length - 1; i >= 0 && fractionDigits[i] == '0'; i--)
        {
            zeros++;
        }

        if (zeros < fractionDigits.Length)
        {
            return zeros;
        }

        for (var i = integerDigits.Length - 1; i >= 0 && integerDigits[i] == '0'; i--)
        {
            zeros++;
        }

        return zeros;
    }

    /// <summary>
    /// Renders the canonical decimal text of Section 18 steps 1 through 7. The result depends on the
    /// value alone: not on the current culture, and not on how the value was spelled in its source.
    /// </summary>
    public string ToCanonicalText()
    {
        if (IsZero)
        {
            return negativeZero ? "-0.0" : "0.0";
        }

        string digits = BigInteger.Abs(coefficient).ToString(CultureInfo.InvariantCulture);
        BigInteger adjustedExponent = exponent + (digits.Length - 1);
        string sign = coefficient.Sign < 0 ? "-" : string.Empty;

        if (adjustedExponent < MinimumPlainAdjustedExponent || adjustedExponent > MaximumPlainAdjustedExponent)
        {
            return sign + Scientific(digits, adjustedExponent);
        }

        // Plain notation implies exponent == adjustedExponent - digits.Length + 1, and the guard above
        // bounds adjustedExponent, so the exponent is bounded by the digit count and fits an int.
        return sign + Plain(digits, (int)exponent);
    }

    private static string Plain(string digits, int exponent)
    {
        // Section 18 step 7: a nonnegative exponent leaves no fractional digits, so the text would
        // otherwise be indistinguishable from an integer.
        if (exponent >= 0)
        {
            return string.Concat(digits, new string('0', exponent), ".0");
        }

        int fractionLength = -exponent;

        if (digits.Length > fractionLength)
        {
            return string.Concat(
                digits.AsSpan(0, digits.Length - fractionLength),
                ".",
                digits.AsSpan(digits.Length - fractionLength));
        }

        return string.Concat("0.", new string('0', fractionLength - digits.Length), digits);
    }

    private static string Scientific(string digits, BigInteger adjustedExponent) =>
        string.Concat(
            digits.AsSpan(0, 1),
            ".",
            // Section 18 step 6 requires at least one digit after the point, even when it is zero.
            digits.Length > 1 ? digits.AsSpan(1) : "0".AsSpan(),
            "e" + adjustedExponent.ToString(CultureInfo.InvariantCulture));

    private static BigInteger ParseDigits(ReadOnlySpan<char> digits) =>
        BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);

    private static bool IsAsciiDigit(char value) => value is >= '0' and <= '9';

    /// <inheritdoc/>
    public bool Equals(BigDecimal other) =>
        coefficient == other.coefficient
        && exponent == other.exponent
        && IsNegativeZero == other.IsNegativeZero;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is BigDecimal other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(coefficient, exponent, IsNegativeZero);

    /// <summary>Returns the canonical decimal text of Section 18.</summary>
    public override string ToString() => ToCanonicalText();

    /// <summary>Compares two values by their canonical representation.</summary>
    public static bool operator ==(BigDecimal left, BigDecimal right) => left.Equals(right);

    /// <summary>Compares two values by their canonical representation.</summary>
    public static bool operator !=(BigDecimal left, BigDecimal right) => !left.Equals(right);
}
