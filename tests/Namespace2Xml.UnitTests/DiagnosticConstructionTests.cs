using Namespace2Xml.Diagnostics;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// The Section 6.4.3 schema is closed and its optional members are interdependent. These tests hold
/// the constructor to it, so that "the stream validates" is a property of the type rather than of
/// the writer that happened to be exercised.
/// </summary>
public sealed class DiagnosticConstructionTests
{
    private static Diagnostic Valid(
        string? source = null,
        int? line = null,
        int? column = null,
        string spec = "§22") =>
        new("CLI001", DiagnosticSeverity.Error, DiagnosticPhase.Cli, spec, "prose", source, line, column);

    [Test]
    public void TheRequiredMembersSuffice() =>
        Should.NotThrow(() => Valid());

    #region Section 6.4.3 — line requires source, column requires line

    [Test]
    public void ALineWithoutASourceIsRejected() =>
        Should.Throw<ArgumentException>(() => Valid(line: 1));

    [Test]
    public void AColumnWithoutALineIsRejected() =>
        Should.Throw<ArgumentException>(() => Valid(source: "a.properties", column: 1));

    [Test]
    public void AColumnWithoutASourceOrLineIsRejected() =>
        Should.Throw<ArgumentException>(() => Valid(column: 1));

    [Test]
    public void ASourceAloneIsAccepted() =>
        Should.NotThrow(() => Valid(source: "a.properties"));

    [Test]
    public void ASourceAndLineAreAccepted() =>
        Should.NotThrow(() => Valid(source: "a.properties", line: 1));

    [Test]
    public void TheFullPositionIsAccepted() =>
        Should.NotThrow(() => Valid(source: "a.properties", line: 1, column: 1));

    #endregion

    #region Section 6.4.3 — integers carry no sign, and positions are one-based

    [TestCase(0)]
    [TestCase(-1)]
    public void ANonPositiveLineIsRejected(int line) =>
        Should.Throw<ArgumentOutOfRangeException>(() => Valid(source: "a.properties", line: line));

    [TestCase(0)]
    [TestCase(-1)]
    public void ANonPositiveColumnIsRejected(int column) =>
        Should.Throw<ArgumentOutOfRangeException>(() => Valid(source: "a.properties", line: 1, column: column));

    #endregion

    #region Section 6.4.3 — the spec anchor spelling

    [TestCase("§22")]
    [TestCase("§6.4.3")]
    [TestCase("§15.1")]
    [TestCase("§B")]
    [TestCase("§C.7")]
    public void AWellFormedAnchorIsAccepted(string spec) =>
        Should.NotThrow(() => Valid(spec: spec));

    [TestCase("")]
    [TestCase("22")]
    [TestCase("#22")]
    [TestCase("Section 22")]
    [TestCase("§")]
    [TestCase("§.1")]
    [TestCase("§22.")]
    [TestCase("§22..1")]
    [TestCase("§b")]
    [TestCase("§BB")]
    [TestCase("§22.x")]
    public void AMalformedAnchorIsRejected(string spec) =>
        Should.Throw<ArgumentException>(() => Valid(spec: spec));

    #endregion

    #region The constructor is the only gate

    /// <summary>
    /// Get-only properties are what makes the constructor total: were they <c>init</c>, a
    /// <c>with</c> expression could assemble a schema-invalid element from a valid one without
    /// running a single check.
    /// </summary>
    [Test]
    public void NoMemberIsSettableAfterConstruction() =>
        typeof(Diagnostic).GetProperties()
            .Where(property => property.Name != "EqualityContract")
            .ShouldAllBe(property => property.SetMethod == null);

    [Test]
    public void AnEmptyCodeIsRejected() =>
        Should.Throw<ArgumentException>(() =>
            new Diagnostic("", DiagnosticSeverity.Error, DiagnosticPhase.Cli, "§22", "prose"));

    #endregion
}
