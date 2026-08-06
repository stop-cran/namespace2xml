using Namespace2Xml.Overlay;
using Namespace2Xml.Profiles;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 8.7 canonical ordering-value spelling, and the Section 5.4 range it addresses.
/// </summary>
[TestFixture]
public class OrderingValuesTests
{
    /// <summary>
    /// Section 8.7: "valid spelling is <c>0</c> or a nonzero digit followed by decimal digits".
    /// </summary>
    [TestCase("0", 0L)]
    [TestCase("1", 1L)]
    [TestCase("7", 7L)]
    [TestCase("10", 10L)]
    [TestCase("9223372036854775807", long.MaxValue)]
    public void ACanonicalDecimalIsAnOrderingValue(string text, long expected)
    {
        OrderingValues.TryRead(text, out var value).ShouldBeTrue();
        value.ShouldBe(expected);
    }

    /// <summary>
    /// Section 8.7: "leading-zero spellings such as <c>00</c> and <c>01</c> are ordinary mapping
    /// keys and prevent sequence interpretation."
    /// </summary>
    [TestCase("00")]
    [TestCase("01")]
    [TestCase("007")]
    public void ALeadingZeroSpellingIsAnOrdinaryMappingKey(string text) =>
        OrderingValues.TryRead(text, out _).ShouldBeFalse();

    /// <summary>
    /// Section 8.7: "a canonically spelled decimal above the supported maximum is an ordinary
    /// mapping key and prevents sequence interpretation." It is deliberately not an error.
    /// </summary>
    [Test]
    public void ADecimalAboveTheMaximumIsAnOrdinaryMappingKey() =>
        OrderingValues.TryRead("9223372036854775808", out _).ShouldBeFalse();

    /// <summary>
    /// Section 5.4 puts ordering values in the range <c>0</c> through <c>long.MaxValue</c>, so a
    /// sign is not part of the spelling and a signed reading must not be accepted.
    /// </summary>
    [TestCase("-1")]
    [TestCase("+1")]
    [TestCase("1.0")]
    [TestCase("1 ")]
    [TestCase(" 1")]
    [TestCase("1_0")]
    [TestCase("0x1")]
    [TestCase("")]
    [TestCase("\uFF11")]
    [TestCase("\u0663")]
    public void AnythingButACanonicalDecimalIsAnOrdinaryMappingKey(string text) =>
        OrderingValues.TryRead(text, out _).ShouldBeFalse();

    /// <summary>
    /// Section 5.4 exposes ordering values as decimal name parts, and Section 8.2 builds a
    /// component from tokens. A component holding an unresolved wildcard has no text yet, so it
    /// cannot be a canonical spelling however it later resolves.
    /// </summary>
    [Test]
    public void AComponentThatIsNotASingleLiteralIsNotAnOrderingValue()
    {
        var wildcard = new OrdinaryPart([new LiteralToken("1"), new WildcardToken(null)]);

        OrderingValues.TryRead(wildcard, out _).ShouldBeFalse();
    }

    /// <summary>
    /// Section 8.7 decides on the component's text, so a component assembled from adjacent literals
    /// is read as the text it spells.
    /// </summary>
    [Test]
    public void AComponentSpelledByAdjacentLiteralsIsReadAsItsText()
    {
        var composite = new OrdinaryPart([new LiteralToken("1"), new LiteralToken("2")]);

        OrderingValues.TryRead(composite, out var value).ShouldBeTrue();
        value.ShouldBe(12L);
    }

    /// <summary>
    /// A Section 8.2 typed component names an XML node rather than a mapping key, so it is never an
    /// ordering value even when its text is a canonical decimal.
    /// </summary>
    [Test]
    public void ATypedComponentIsNotAnOrderingValue()
    {
        NamePart attribute = new AttributePart(new OrdinaryPart([new LiteralToken("1")]));

        OrderingValues.TryRead(attribute, out _).ShouldBeFalse();
    }

    /// <summary>Section 5.4 exposes ordering values "as decimal name parts".</summary>
    [Test]
    public void AnOrderingValueRoundTripsThroughItsNameComponent()
    {
        OrderingValues.TryRead(OrderingValues.ToNamePart(42), out var value).ShouldBeTrue();
        value.ShouldBe(42L);
    }

    /// <summary>
    /// Section 8.7's spelling rule is one rule, so the spelling this produces must be the spelling
    /// it accepts; producing <c>01</c> for 1 would create a name it would then reject.
    /// </summary>
    [Test]
    public void TheCanonicalSpellingOfEveryBoundaryValueIsAccepted()
    {
        foreach (var value in new[] { 0L, 1L, 9L, 10L, long.MaxValue })
        {
            OrderingValues.TryRead(OrderingValues.ToCanonicalText(value), out var read).ShouldBeTrue();
            read.ShouldBe(value);
        }
    }

    /// <summary>Section 5.4 has no negative ordering values, so there is nothing to spell.</summary>
    [Test]
    public void ANegativeValueHasNoCanonicalSpelling() =>
        Should.Throw<ArgumentOutOfRangeException>(() => OrderingValues.ToCanonicalText(-1));
}
