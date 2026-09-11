using Namespace2Xml.Gatekeeper;
using NUnit.Framework;
using Shouldly;
using YamlDotNet.RepresentationModel;

namespace Namespace2Xml.UnitTests;

[TestFixture]
public class MatrixExpanderTests
{
    [Test]
    public void CartesianExcludeAndIncludeUseActionsSemantics()
    {
        var cells = Expand(
            """
            matrix:
              os: [linux, windows]
              runtime: [8, 9]
              exclude:
                - os: linux
                  runtime: 9
              include:
                - os: windows
                  runtime: 9
                  arch: arm64
                - os: macos
                  runtime: 10
            """);

        Render(cells).ShouldBe(
        [
            """[os="linux",runtime=8]""",
            """[os="windows",runtime=8]""",
            """[arch="arm64",os="windows",runtime=9]""",
            """[os="macos",runtime=10]""",
        ]);
    }

    [Test]
    public void IncludeOnlyMatricesProduceOneCellPerEntry()
    {
        var cells = Expand(
            """
            matrix:
              include:
                - ansible: '2.15'
                  python: '3.11'
                - ansible: '2.21'
                  python: '3.13'
            """);

        Render(cells).ShouldBe(
        [
            """[ansible="2.15",python="3.11"]""",
            """[ansible="2.21",python="3.13"]""",
        ]);
    }

    [Test]
    public void LaterIncludesReplaceEarlierAugmentationsOnCompatibleOriginals()
    {
        var cells = Expand(
            """
            matrix:
              fruit: [apple, pear]
              animal: [cat, dog]
              include:
                - color: green
                - animal: cat
                  color: pink
            """);

        Render(cells).ShouldBe(
        [
            """[animal="cat",color="pink",fruit="apple"]""",
            """[animal="dog",color="green",fruit="apple"]""",
            """[animal="cat",color="pink",fruit="pear"]""",
            """[animal="dog",color="green",fruit="pear"]""",
        ]);
    }

    [Test]
    public void EmptyDynamicAndDuplicateMatricesFailClosed()
    {
        string[] invalid =
        [
            "matrix: {}",
            """matrix: '${{ fromJSON(inputs.matrix) }}'""",
            "matrix:\n  os: [linux, linux]",
        ];

        foreach (var document in invalid)
        {
            Should.Throw<InvalidDataException>(() => Expand(document), document);
        }
    }

    [TestCase("true", "true")]
    [TestCase("True", "true")]
    [TestCase("TRUE", "true")]
    [TestCase("TrUe", @"""TrUe""")]
    [TestCase("false", "false")]
    [TestCase("False", "false")]
    [TestCase("FALSE", "false")]
    [TestCase("FaLsE", @"""FaLsE""")]
    [TestCase("", "null")]
    [TestCase("null", "null")]
    [TestCase("Null", "null")]
    [TestCase("NULL", "null")]
    [TestCase("NuLl", @"""NuLl""")]
    [TestCase("~", "null")]
    [TestCase("01", "1")]
    [TestCase("+12", "12")]
    [TestCase("0x10", "16")]
    [TestCase("0xFFFFFFFF", "-1")]
    [TestCase("0o10", "8")]
    [TestCase("1.25", "1.25")]
    [TestCase("1e2", "100")]
    [TestCase(".5", "0.5")]
    public void PlainScalarsUseActionsYamlCoreAndRuntimeJsonRendering(
        string value,
        string expected)
    {
        MatrixScalar.FromYaml(value, quoted: false).CanonicalJson.ShouldBe(expected);
    }

    [Test]
    public void QuotedScalarsRemainStrings()
    {
        var cells = Expand(
            """
            matrix:
              value: ['true', "Null", '01', '0x10', '1.25']
            """);

        Render(cells).ShouldBe(
        [
            """[value="true"]""",
            """[value="Null"]""",
            """[value="01"]""",
            """[value="0x10"]""",
            """[value="1.25"]""",
        ]);
    }

    [Test]
    public void ExplicitCoreTagsUseActionsRunnerSemantics()
    {
        var cells = Expand(
            """
            matrix:
              value:
                - !!str true
                - !!bool true
                - !!int 01
                - !!float 1e2
                - !!null null
            """);

        Render(cells).ShouldBe(
        [
            """[value="true"]""",
            "[value=true]",
            "[value=1]",
            "[value=100]",
            "[value=null]",
        ]);
    }

    [Test]
    public void MappingKeysUseActionsRunnerScalarAndExplicitTagSemantics()
    {
        var cells = Expand(
            """
            matrix:
              !!str os: [linux]
              !!bool true: [enabled]
              include:
                - os: linux
                  !!str arch: arm64
            """);

        Render(cells).ShouldBe(
        [
            """[arch="arm64",os="linux",true="enabled"]""",
        ]);
    }

    [TestCase("!!bool TrUe")]
    [TestCase("!!int 1.5")]
    [TestCase("!!float '1.5'")]
    [TestCase("!!null nope")]
    [TestCase("!custom value")]
    public void InvalidOrUnsupportedExplicitTagsFailClosed(string value)
    {
        Should.Throw<FormatException>(() => Expand($"matrix:\n  value: [{value}]\n"));
    }

    [TestCase("!custom os: [linux]")]
    [TestCase("!!bool os: [linux]")]
    [TestCase("os: [linux]\n  include:\n    - !custom arch: arm64")]
    [TestCase("os: [linux]\n  exclude:\n    - !custom os: linux")]
    [TestCase("!custom include:\n    - os: linux")]
    public void InvalidOrUnsupportedMappingKeyTagsFailClosed(string matrix)
    {
        Should.Throw<FormatException>(() => Expand($"matrix:\n  {matrix}\n"));
    }

    [TestCase(".inf")]
    [TestCase("-.INF")]
    [TestCase(".NaN")]
    [TestCase("1e9999")]
    public void NonfiniteMatrixNumbersFailClosed(string value)
    {
        Should.Throw<FormatException>(() => Expand($"matrix:\n  value: [{value}]\n"));
    }

    private static IReadOnlyList<MatrixCell> Expand(string text)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(text));
        var root = (YamlMappingNode)stream.Documents[0].RootNode;
        return MatrixExpander.Expand(root.Children[new YamlScalarNode("matrix")]);
    }

    private static List<string> Render(IEnumerable<MatrixCell> cells) =>
        cells.Select(GateIdentityRenderer.RenderCell).ToList();
}
