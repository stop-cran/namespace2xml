using Namespace2Xml.Text;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 22's line and column model, applied to a host parser's position.
/// </summary>
/// <remarks>
/// Section 22 fixes two things a host parser gets differently: a line ends at "LF, CRLF, or a lone
/// CR, and by nothing else", and "Column 1 is the first Unicode scalar value of a line, and each
/// subsequent scalar advances the column by one, so a character outside the Basic Multilingual
/// Plane occupies one column". A .NET string is indexed in UTF-16 code units, so the second rule
/// is the one a reader silently breaks by reporting a library's column unchanged.
/// </remarks>
[TestFixture]
public class SourceLinesTests
{
    /// <summary>A line of characters in the Basic Multilingual Plane needs no conversion.</summary>
    /// <param name="text">The source.</param>
    /// <param name="line">The one-based line.</param>
    /// <param name="column">The host's one-based column.</param>
    [TestCase("abc", 1, 1)]
    [TestCase("abc", 1, 3)]
    [TestCase("\u0436\u0436\u0436", 1, 3)]
    [TestCase("ab\ncd", 2, 2)]
    public void ABasicPlaneColumnIsUnchanged(string text, int line, int column) =>
        new SourceLines(text).ColumnOf(line, column).ShouldBe(column);

    /// <summary>
    /// A scalar outside the Basic Multilingual Plane occupies one column although it is stored as
    /// two UTF-16 code units, so every column to its right converts down by one per such scalar.
    /// </summary>
    /// <param name="text">The source.</param>
    /// <param name="line">The one-based line.</param>
    /// <param name="column">The host's one-based UTF-16 column.</param>
    /// <param name="expected">The Section 22 column.</param>
    [TestCase("\U0001F600x", 1, 1, 1)]
    [TestCase("\U0001F600x", 1, 3, 2)]
    [TestCase("\U0001F600\U0001F600x", 1, 5, 3)]
    [TestCase("a\U0001F600b", 1, 4, 3)]
    public void ASupplementaryScalarOccupiesOneColumn(
        string text, int line, int column, int expected) =>
        new SourceLines(text).ColumnOf(line, column).ShouldBe(expected);

    /// <summary>
    /// Each of the three line terminators Section 22 recognizes starts a new line.
    /// </summary>
    /// <param name="terminator">The terminator.</param>
    /// <remarks>
    /// The source is <c>ab</c>, the terminator, an emoji, and <c>z</c>. On line 2 the emoji stands
    /// in column 1 and <c>z</c> in column 2, though a host indexes <c>z</c> at UTF-16 column 3.
    /// </remarks>
    [TestCase("\n")]
    [TestCase("\r\n")]
    [TestCase("\r")]
    public void ATerminatorStartsANewLine(string terminator) =>
        new SourceLines("ab" + terminator + "\U0001F600z").ColumnOf(2, 3).ShouldBe(2);

    /// <summary>
    /// Section 22 names U+0085, U+2028 and U+2029 as characters that do not terminate a line, so
    /// what follows one of them is still on the same line.
    /// </summary>
    /// <param name="other">The character.</param>
    /// <remarks>
    /// The source is one line of five scalars -- <c>a</c>, <c>b</c>, the character, the emoji, and
    /// <c>z</c> -- of which a host indexes the last at UTF-16 column 6.
    /// </remarks>
    [TestCase("\u0085")]
    [TestCase("\u2028")]
    [TestCase("\u2029")]
    public void ACharacterSection22ExcludesDoesNotStartANewLine(string other) =>
        new SourceLines("ab" + other + "\U0001F600z").ColumnOf(1, 6).ShouldBe(5);

    /// <summary>
    /// A line this source does not contain is returned unchanged, and a column beyond the end of
    /// its line keeps advancing one per code unit. Inventing a position would be worse than
    /// repeating the host's, which at least came from somewhere.
    /// </summary>
    /// <param name="line">The one-based line.</param>
    /// <param name="column">The host's one-based column.</param>
    /// <remarks>
    /// Line 2 of the source carries an emoji so that a conversion which ran past the end of line 1
    /// would count it and come back one short. Section 22 measures a column from "the first
    /// Unicode scalar value of a line", so a scalar on another line can never move this one.
    /// </remarks>
    [TestCase(0, 5)]
    [TestCase(9, 5)]
    [TestCase(1, 0)]
    [TestCase(1, 400)]
    public void APositionOutsideTheSourceIsUnchanged(int line, int column) =>
        new SourceLines("ab\n\U0001F600z").ColumnOf(line, column).ShouldBe(column);

    /// <summary>An empty source has one line, and column 1 of it is column 1.</summary>
    [Test]
    public void AnEmptySourceHasOneLine() =>
        new SourceLines("").ColumnOf(1, 1).ShouldBe(1);

    /// <summary>
    /// An offset resolves to the Section 22 line that contains it and the Section 22 column it
    /// stands at, counting scalars from the start of that line.
    /// </summary>
    /// <param name="text">The source.</param>
    /// <param name="index">A zero-based UTF-16 code-unit offset into it.</param>
    /// <param name="line">The expected one-based line.</param>
    /// <param name="column">The expected one-based column.</param>
    /// <remarks>
    /// The offset is the only input, so this is the conversion for a host whose line numbering
    /// disagrees with Section 22 rather than only its column.
    /// </remarks>
    [TestCase("abc", 0, 1, 1)]
    [TestCase("abc", 2, 1, 3)]
    [TestCase("ab\ncd", 3, 2, 1)]
    [TestCase("ab\ncd", 4, 2, 2)]
    [TestCase("ab\r\ncd", 4, 2, 1)]
    [TestCase("ab\rcd", 3, 2, 1)]
    [TestCase("\U0001F600z", 2, 1, 2)]
    [TestCase("a\n\U0001F600\U0001F600z", 6, 2, 3)]
    public void AnOffsetResolvesToItsLineAndColumn(string text, int index, int line, int column) =>
        new SourceLines(text).PositionOf(index).ShouldBe((line, column));

    /// <summary>
    /// U+0085, U+2028 and U+2029 do not start a line under Section 22, so an offset after one of
    /// them is still on the first line and its column has counted the character.
    /// </summary>
    /// <param name="other">The character.</param>
    /// <remarks>
    /// This is the rule a YAML 1.1 scanner breaks. The source is <c>ab</c>, the character, and
    /// <c>z</c>; a scanner that treated the character as a break would put <c>z</c> at line 2
    /// column 1 instead of line 1 column 4.
    /// </remarks>
    [TestCase("\u0085")]
    [TestCase("\u2028")]
    [TestCase("\u2029")]
    public void AnOffsetAfterACharacterSection22ExcludesIsOnTheSameLine(string other) =>
        new SourceLines("ab" + other + "z").PositionOf(3).ShouldBe((1, 4));

    /// <summary>
    /// An offset at or past the end names the position just after the last scalar, which is where
    /// a host reports an unexpected end of input.
    /// </summary>
    /// <param name="text">The source.</param>
    /// <param name="index">A zero-based offset at or beyond its end.</param>
    /// <param name="line">The expected one-based line.</param>
    /// <param name="column">The expected one-based column.</param>
    /// <remarks>
    /// A source ending in a terminator has an empty final line, so the end of <c>ab\n</c> is line
    /// 2 column 1 and not line 1 column 3. A negative offset is clamped to the start for the same
    /// reason the far end is clamped: a position outside the source is a host defect, and a
    /// position inside it is the least misleading answer available.
    /// </remarks>
    [TestCase("ab", 2, 1, 3)]
    [TestCase("ab", 900, 1, 3)]
    [TestCase("ab\n", 3, 2, 1)]
    [TestCase("ab\r\n", 4, 2, 1)]
    [TestCase("", 0, 1, 1)]
    [TestCase("ab", -5, 1, 1)]
    [TestCase("a\U0001F600", 3, 1, 3)]
    public void AnOffsetOutsideTheSourceIsClamped(string text, int index, int line, int column) =>
        new SourceLines(text).PositionOf(index).ShouldBe((line, column));
}
