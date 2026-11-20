using System.Text;
using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Pipeline;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

[TestFixture]
public class ProgramInvariantTests
{
    [TestCase("--version", "--diagnostics-format=json")]
    [TestCase("--version=ignored", "--diagnostics-format=invalid")]
    public void VersionRemainsAnImmediateLfDelimitedInformationalMode(string option, string format)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        using var json = new MemoryStream();

        var exitCode = Program.Execute(
            [format, option],
            stdout,
            stderr,
            (_, _) => throw new InvalidOperationException("Transformation must not run"),
            json);

        exitCode.ShouldBe(0);
        stderr.ToString().ShouldBeEmpty();
        json.ToArray().ShouldBeEmpty();
        stdout.ToString().ShouldContain("\nlocal-documentation: ", Case.Sensitive);
        stdout.ToString().ShouldNotContain("\r", Case.Sensitive);
        stdout.ToString().ShouldEndWith("\n", Case.Sensitive);
    }

    [Test]
    public void TextInvariantFailureIsOneLfTerminatedMessage()
    {
        var stdout = new StringWriter();
        var text = new StringWriter();
        using var json = new MemoryStream();

        var exitCode = Program.Execute(
            ["-i", "in.txt", "-s", "scheme.txt"],
            stdout,
            text,
            (_, _) => throw new PipelineInvariantException("deliberate test failure"),
            json);

        exitCode.ShouldBe(1);
        stdout.ToString().ShouldBeEmpty();
        text.ToString().ShouldBe(
            "namespace2xml 3.0.0: internal pipeline invariant failed: deliberate test failure\n");
        json.ToArray().ShouldBeEmpty();
    }

    [Test]
    public void JsonInvariantFailureIsOnlyAnEmptyLfTerminatedArray()
    {
        var stdout = new StringWriter();
        var text = new StringWriter();
        using var json = new MemoryStream();

        var exitCode = Program.Execute(
            [
                "-i", "in.txt",
                "-s", "scheme.txt",
                "--diagnostics-format", "json",
                "--verbosity", "none",
            ],
            stdout,
            text,
            (_, _) => throw new PipelineInvariantException("must not leak"),
            json);

        exitCode.ShouldBe(1);
        stdout.ToString().ShouldBeEmpty();
        text.ToString().ShouldBeEmpty();
        Encoding.UTF8.GetString(json.ToArray()).ShouldBe("[]\n");
    }

    [Test]
    public void TextInvariantFailureHonorsNoneVerbosity()
    {
        var stdout = new StringWriter();
        var text = new StringWriter();
        using var json = new MemoryStream();

        var exitCode = Program.Execute(
            [
                "-i", "in.txt",
                "-s", "scheme.txt",
                "--verbosity", "none",
            ],
            stdout,
            text,
            (_, _) => throw new PipelineInvariantException("must not leak"),
            json);

        exitCode.ShouldBe(1);
        stdout.ToString().ShouldBeEmpty();
        text.ToString().ShouldBeEmpty();
        json.ToArray().ShouldBeEmpty();
    }
}
