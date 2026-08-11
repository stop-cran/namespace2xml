using Namespace2Xml.Cli;
using Namespace2Xml.Scheme;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Specification section 6.4: what <c>--help</c> is obliged to say about the scheme language.
/// </summary>
/// <remarks>
/// The tool cannot run without a scheme, and a scheme cannot be written without knowing at least
/// one directive and one format name. Before this gate the only enumeration of either lived in a
/// 215 KB specification, and the help text's English prose named a format
/// <c>quoted-namespace</c> — a spelling the parser rejects. Prose that has to agree with a parser
/// eventually will not, so these assert that the help lists what the parser accepts and nothing
/// else.
/// </remarks>
[TestFixture]
public class HelpTextSchemeTests
{
    private static string Help => HelpText.Render();

    /// <summary>
    /// Section 16.1's seven declarations all appear. A format the tool accepts and the help omits
    /// is a capability nobody can find.
    /// </summary>
    [Test]
    public void TheHelpNamesEverySection161OutputFormat()
    {
        foreach (var format in OutputFormats.Spellings)
        {
            Help.ShouldContain(format, Case.Sensitive);
        }
    }

    /// <summary>
    /// The reported defect, asserted directly. A hyphenated spelling in the help reads as the token
    /// it names, and the token has no hyphen.
    /// </summary>
    [Test]
    public void TheHelpDoesNotOfferASpellingTheParserRejects()
    {
        Help.ShouldNotContain("quoted-namespace", Case.Insensitive);

        // The surviving prose describes the format without looking like a token, so the assertion
        // above cannot be satisfied by dropping the mention altogether.
        Help.ShouldContain("shell-quoted namespace", Case.Sensitive);
    }

    /// <summary>
    /// Every Section 15 directive appears, so "what can a scheme say?" is answerable from the help.
    /// </summary>
    [Test]
    public void TheHelpNamesEverySection15Directive()
    {
        foreach (var directive in SchemeDirectives.Spellings)
        {
            Help.ShouldContain(directive, Case.Sensitive);
        }
    }

    /// <summary>
    /// The directive list wraps, and a wrap must not swallow the separator. A line ending in a bare
    /// name reads as the end of the list to anyone scanning it.
    /// </summary>
    /// <remarks>
    /// The block is located by the sentence that introduces it rather than by the names that happen
    /// to fall at today's wrap points, so adding or renaming a directive moves the wrap without
    /// making this assertion vacuous.
    /// </remarks>
    [Test]
    public void TheWrappedDirectiveListKeepsItsSeparators()
    {
        var lines = Help.Split('\n').Select(line => line.TrimEnd('\r').TrimEnd()).ToList();
        var start = lines.FindIndex(line => line.Trim() == "The other directives are") + 1;

        start.ShouldBeGreaterThan(0, "the help is expected to introduce the directive list");

        var tail = lines.Skip(start).ToList();
        var last = tail.FindIndex(line => line.EndsWith('.'));

        last.ShouldBeGreaterThan(0, "the directive list is expected to wrap");

        foreach (var line in tail.Take(last))
        {
            line.ShouldEndWith(",");
        }

        string.Concat(tail.Take(last + 1)).ShouldContain("filemerge.", Case.Sensitive);
    }
}
