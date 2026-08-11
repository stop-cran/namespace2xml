using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using Namespace2Xml.Scheme;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// A refusal from a closed set has to name the set. The value is not that the sentence reads well;
/// it is that the only documented route from a rejected token back to a working one stops being a
/// specification read.
/// </summary>
/// <remarks>
/// Every expectation here is authored from the specification clause named in the test. The lists
/// are compared against Sections 16.1 and 16.6 rather than against whatever the parser happens to
/// hold, because a list derived from the parser and asserted against the parser proves only that
/// the code equals itself.
/// </remarks>
[TestFixture]
public class AcceptedValueTests
{
    /// <summary>
    /// Compiles a one-line scheme document and returns the prose of the single diagnostic it
    /// produced, so an assertion reads the bytes a reader would see rather than a reconstruction.
    /// </summary>
    /// <param name="document">The scheme line to compile.</param>
    private static string Refusal(string document)
    {
        var diagnostics = new DiagnosticBuffer();
        ImmutableArray<NamespaceRecord> records =
            [NamespaceRecordClassifier.Classify(document, 1)];

        var read = SchemeReader.Read(records, 2, "s.properties", diagnostics);

        SchemeCompiler.Compile(read.Entries, diagnostics);

        return diagnostics.Drain().ShouldHaveSingleItem().Message;
    }

    /// <summary>
    /// Section 16.1 lists seven declarations an <c>output</c> value may name. The list a diagnostic
    /// offers is written out separately from the table the parser matches, so that a human meets it
    /// in the clause's order; this holds the two together.
    /// </summary>
    [Test]
    public void TheOfferedOutputFormatsAreExactlyTheOnesSection161Lists()
    {
        OutputFormats.Spellings.ShouldBe(
            ["namespace", "quotednamespace", "json", "yaml", "xml", "ini", "ignore"]);

        // The negative declaration is not a format and has no entry in the parse table, so it is
        // the one member the two lists are expected to disagree about.
        OutputFormats.Spellings
            .Where(name => name != OutputFormats.Ignore)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(OutputFormats.ParsedNames.OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// Section 16.6 lists ten type values. A spelling missing from this list is a value an author
    /// may write and no diagnostic will ever suggest.
    /// </summary>
    [Test]
    public void TheOfferedTypeValuesAreExactlyTheOnesSection166Lists()
    {
        TypeSet.Spellings.ShouldBe([
            "default",
            "ignore",
            "array",
            "mapping",
            "string",
            "element",
            "attribute",
            "text",
            "cdata",
            "multiline",
        ]);
    }

    /// <summary>
    /// Section 15.3 calls <c>namespacedelimiter</c> and <c>xmloptions</c> deprecated aliases. A
    /// refusal that offered them would be steering the reader onto a spelling the tool warns about.
    /// </summary>
    [Test]
    public void TheOfferedDirectivesExcludeTheDeprecatedAliases()
    {
        SchemeDirectives.Spellings.ShouldContain("delimiter");
        SchemeDirectives.Spellings.ShouldContain("xmloutputoptions");
        SchemeDirectives.Spellings.ShouldNotContain("namespacedelimiter");
        SchemeDirectives.Spellings.ShouldNotContain("xmloptions");
    }

    /// <summary>
    /// Rejecting the misspelling reported in issue 80 names the spelling that works. This is the
    /// whole point of the change: the reporter tried eleven spellings before finding one.
    /// </summary>
    /// <remarks>
    /// The assertion reads the message the compiler actually emitted. An earlier version of this
    /// test rebuilt the sentence from the same helper the production site calls and asserted that;
    /// deleting the helper call from the production site left it green.
    /// </remarks>
    [Test]
    public void RefusingAnOutputFormatNamesEveryFormat() =>
        Refusal("a.output=quoted-namespace").ShouldEndWith(
            "The accepted values are 'namespace', 'quotednamespace', 'json', 'yaml', 'xml', "
            + "'ini' and 'ignore'.");

    /// <summary>
    /// Section 16.6's ten values are a closed set, so a refused type value can name all of them.
    /// </summary>
    [Test]
    public void RefusingATypeValueNamesEveryType() =>
        Refusal("a.type=str").ShouldEndWith(
            "The accepted values are 'default', 'ignore', 'array', 'mapping', 'string', "
            + "'element', 'attribute', 'text', 'cdata' and 'multiline'.");

    /// <summary>
    /// An unrecognized directive name is the other half of issue 80: the reader knows the whole
    /// Section 15 vocabulary and used to keep it to itself.
    /// </summary>
    [Test]
    public void RefusingADirectiveNameNamesEveryDirective()
    {
        var refusal = Refusal("a.outputs=json");

        refusal.ShouldContain("'output'", Case.Sensitive);
        refusal.ShouldContain("'type'", Case.Sensitive);
        refusal.ShouldContain("The accepted values are ", Case.Sensitive);
    }

    /// <summary>A one-member set reads as one rather than as a list of one.</summary>
    [Test]
    public void ASingleAcceptedValueIsNotOfferedAsAList()
    {
        Diagnostics.AcceptedValues.Sentence(["only"])
            .ShouldBe(" The only accepted value is 'only'.");
    }

    /// <summary>Two members take no comma, which is where a naive join puts one.</summary>
    [Test]
    public void TwoAcceptedValuesAreJoinedWithoutAComma()
    {
        Diagnostics.AcceptedValues.Sentence(["text", "json"])
            .ShouldBe(" The accepted values are 'text' and 'json'.");
    }

    /// <summary>
    /// An empty set contributes nothing rather than a sentence with a hole in it. No caller has an
    /// empty set today, and a future one that does should degrade to the bare refusal.
    /// </summary>
    [Test]
    public void AnEmptySetAddsNothingToTheRefusal() =>
        Diagnostics.AcceptedValues.Sentence([]).ShouldBeEmpty();
}
