using Namespace2Xml.Text;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 22 column arithmetic. Expectations are authored from the clause, not from the helper.
/// </summary>
[TestFixture]
public sealed class ScalarColumnTests
{
    /// <summary>Section 22: each scalar advances the column by one.</summary>
    [Test]
    public void EveryBasicPlaneScalarAdvancesOneColumn() =>
        ScalarColumn.Advance("abc", 3).ShouldBe(3);

    /// <summary>
    /// Section 22: "a character outside the Basic Multilingual Plane occupies one column". U+1D11E
    /// is two UTF-16 code units, so a code-unit count would say two.
    /// </summary>
    [Test]
    public void AnAstralScalarAdvancesOneColumn() =>
        ScalarColumn.Advance("\U0001D11E", 2).ShouldBe(1);

    /// <summary>Section 22, for an offset that stops inside a surrogate pair.</summary>
    /// <remarks>
    /// No lexer produces such an offset, because it would not name a character boundary. The
    /// answer is defined anyway so the conversion is total: the half counts as the scalar it
    /// begins.
    /// </remarks>
    [Test]
    public void AnOffsetInsideASurrogatePairCountsThePairOnce() =>
        ScalarColumn.Advance("\U0001D11Ex", 1).ShouldBe(1);

    /// <summary>Section 22: a tab occupies one column.</summary>
    [Test]
    public void ATabOccupiesOneColumn() =>
        ScalarColumn.Width("\ta").ShouldBe(2);

    /// <summary>Width is the whole text measured in Section 22 columns.</summary>
    [Test]
    public void WidthCountsScalarsNotCodeUnits() =>
        ScalarColumn.Width("a\U0001D11Eb").ShouldBe(3);

    /// <summary>An offset past the text names no position and is a programming error.</summary>
    [Test]
    public void AnOffsetPastTheTextIsRejected() =>
        Should.Throw<ArgumentOutOfRangeException>(() => ScalarColumn.Advance("a", 2));
}
