using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Inputs;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Pipeline.Steps;
using Namespace2Xml.Profiles;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// How a diagnostic raised inside a <c>--variables</c> argument names the argument it came from.
/// </summary>
/// <remarks>
/// Section 8.1 makes a variable's diagnostic omit <c>source</c>, <c>line</c> and <c>column</c>, so
/// every member that would locate it is gone and the message becomes the only place the argument
/// can be named. Appendix C.4 never compares <c>message</c>, so a conformance fixture can pin the
/// omission but not the naming, and the naming is the half a reader with two <c>-v</c> tokens
/// actually needs. That is why this is asserted here.
/// </remarks>
[TestFixture]
public sealed class VariableDiagnosticTests
{
    /// <summary>A reader no variable ever consults, since a variable is not read from a file.</summary>
    private static TransformationTests.Sources NoFiles() => new(("unused.txt", ""));

    /// <summary>
    /// The single diagnostic <paramref name="text"/> raises when read as one record of
    /// <paramref name="source"/>. Section 8.1 classification happens as the source is loaded and
    /// the record is read afterwards, so both halves have to run for a rule 5 record to be seen.
    /// </summary>
    private static Diagnostic Reading(string text, ProfileSource source)
    {
        var diagnostics = new DiagnosticBuffer();

        NamespaceProfileReader.Read(
            [NamespaceRecordClassifier.Classify(text, 1)],
            sourceOrdinal: 1,
            source,
            SubstituteModeMap.Default,
            diagnostics);

        return diagnostics.Drain().ShouldHaveSingleItem();
    }

    /// <summary>The message a variable at <paramref name="position"/> raises for a rule 5 record.</summary>
    private static string VariableMessage(string text, int position) =>
        Reading(text, ProfileSource.OfVariable(position)).Message;

    /// <summary>The message a comment record supplied as a variable raises.</summary>
    private static string CommentVariableMessage(string text, int position)
    {
        var diagnostics = new DiagnosticBuffer();

        new SourceLoader(NoFiles(), ResourceLimits.Defaults)
            .LoadVariable(text, position, ordinal: 100, diagnostics);

        return diagnostics.Drain().ShouldHaveSingleItem().Message;
    }

    /// <summary>
    /// A rule 5 record supplied as a variable names the argument, because nothing else does.
    /// </summary>
    [Test]
    public void AMalformedVariableNamesItsPosition()
    {
        VariableMessage("no-equals-here", 2).ShouldStartWith("-v[2]: ", Case.Sensitive);
    }

    /// <summary>
    /// A comment record supplied as a variable names the argument the same way. Section 8.1 raises
    /// this one before the record reaches the reader, so it is a second emission point for the same
    /// rule rather than the same one reached twice.
    /// </summary>
    [Test]
    public void ACommentVariableNamesItsPosition()
    {
        CommentVariableMessage("# a comment", 3).ShouldStartWith("-v[3]: ", Case.Sensitive);
    }

    /// <summary>
    /// The position is the one supplied, not a constant that happens to read correctly in the
    /// examples above.
    /// </summary>
    [Test]
    public void ThePositionIsTheOneSupplied()
    {
        VariableMessage("no-equals-here", 7).ShouldStartWith("-v[7]: ", Case.Sensitive);
    }

    /// <summary>
    /// A file's diagnostic carries no such prefix. Its <c>source</c>, <c>line</c> and <c>column</c>
    /// already say where it is, so prefixing the message would restate what the reader can see.
    /// </summary>
    [Test]
    public void AFileDiagnosticIsNotPrefixed()
    {
        var diagnostic = Reading("no-equals-here", ProfileSource.OfFile("in.txt"));

        diagnostic.Message.ShouldNotStartWith("-v[", Case.Sensitive);
        diagnostic.Source.ShouldBe("in.txt");
    }

    /// <summary>
    /// A variable's diagnostic omits every member that would locate it in a file, which is what
    /// makes the message the only place left to name the argument.
    /// </summary>
    [Test]
    public void AVariableDiagnosticOmitsSourceLineAndColumn()
    {
        var diagnostic = Reading("no-equals-here", ProfileSource.OfVariable(2));

        diagnostic.Source.ShouldBeNull();
        diagnostic.Line.ShouldBeNull();
        diagnostic.Column.ShouldBeNull();
    }
}
