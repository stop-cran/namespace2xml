using System.Globalization;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Specification section 2, "Normative references", names Unicode 16.0.0 and requires an
/// implementation to confirm that the character data it actually resolves matches that edition
/// rather than assuming the edition its runtime documents.
/// </summary>
/// <remarks>
/// <para>
/// The dependency is real rather than pedantic. Section 16.4 defines the scalars a namespace name
/// must escape as the general categories <c>Cc</c>, <c>Cf</c> and <c>Cs</c>, and a general category
/// belongs to an edition: a code point unassigned in one edition can be assigned in the next, at
/// which point the same input escapes differently and section 24's byte-identity promise fails
/// between two builds that differ only in their character tables.
/// </para>
/// <para>
/// The runtime exposes no Unicode version, so this asks the tables directly. The probes are chosen
/// to fail in both directions: characters assigned in 16.0.0 must resolve to their assigned
/// categories, and a character assigned only in a later edition must still be unassigned. A
/// framework upgrade that moves the tables therefore turns this red instead of silently changing
/// what the tool escapes.
/// </para>
/// <para>
/// A red result here is not automatically a defect. It means the runtime and the specification
/// disagree about an edition, and the decision — amend section 2, or pin the runtime — belongs to
/// a human. The failure message says which code point moved so that decision has evidence.
/// </para>
/// </remarks>
[TestFixture]
public class UnicodeEditionTests
{
    /// <summary>
    /// Code points whose general category was fixed by Unicode 16.0.0 or an earlier edition,
    /// with the edition that assigned each one, so a failure names the boundary that moved.
    /// </summary>
    private static IEnumerable<TestCaseData> AssignedInSixteenOrEarlier()
    {
        yield return Probe(0x0890, UnicodeCategory.Format, "14.0.0", "ARABIC POUND MARK ABOVE");
        yield return Probe(0x11F00, UnicodeCategory.NonSpacingMark, "15.0.0", "KAWI SIGN CANDRABINDU");
        yield return Probe(0x1E030, UnicodeCategory.ModifierLetter, "15.0.0", "MODIFIER CYRILLIC SMALL A");
        yield return Probe(0x2EBF0, UnicodeCategory.OtherLetter, "15.1.0", "CJK IDEOGRAPH EXTENSION I FIRST");
        yield return Probe(0x10D40, UnicodeCategory.DecimalDigitNumber, "16.0.0", "GARAY DIGIT ZERO");
        yield return Probe(0x105C0, UnicodeCategory.OtherLetter, "16.0.0", "TODHRI LETTER A");
        yield return Probe(0x1E5D0, UnicodeCategory.OtherLetter, "16.0.0", "OL ONAL LETTER A");
        yield return Probe(0x11BC0, UnicodeCategory.OtherLetter, "16.0.0", "SUNUWAR LETTER DEVI");
    }

    private static TestCaseData Probe(int codePoint, UnicodeCategory category, string edition, string name) =>
        new TestCaseData(codePoint, category)
            .SetArgDisplayNames($"U+{codePoint:X4} {name} ({edition})");

    /// <summary>
    /// The runtime resolves a code point to the general category Unicode 16.0.0 assigns it.
    /// </summary>
    /// <param name="codePoint">The code point to resolve.</param>
    /// <param name="expected">The category the named edition assigns.</param>
    [TestCaseSource(nameof(AssignedInSixteenOrEarlier))]
    public void TheRuntimeAgreesWithTheNamedUnicodeEdition(int codePoint, UnicodeCategory expected) =>
        CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(codePoint), 0)
            .ShouldBe(
                expected,
                $"U+{codePoint:X4} does not carry the category Unicode 16.0.0 assigns it, so the "
                + "runtime's character data is not the edition specification section 2 names.");

    /// <summary>
    /// A code point first assigned after Unicode 16.0.0 is still unassigned, so the runtime has
    /// not moved ahead of the edition the specification names.
    /// </summary>
    /// <remarks>
    /// U+10940 is the first code point of the Sidetic block, which Unicode 17.0.0 encodes and
    /// Unicode 16.0.0 leaves unassigned, and an unassigned code point is
    /// <see cref="UnicodeCategory.OtherNotAssigned"/>.
    /// </remarks>
    [Test]
    public void TheRuntimeHasNotMovedPastTheNamedUnicodeEdition() =>
        CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(0x10940), 0)
            .ShouldBe(
                UnicodeCategory.OtherNotAssigned,
                "U+10940 is assigned after Unicode 16.0.0, so a category other than OtherNotAssigned "
                + "means the runtime's character data is a later edition than specification "
                + "section 2 names.");
}
