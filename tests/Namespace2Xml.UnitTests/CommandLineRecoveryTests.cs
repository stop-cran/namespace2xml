using System.Text;
using System.Text.Json;
using Namespace2Xml.Cli;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

[TestFixture]
public sealed class CommandLineRecoveryTests
{
    [TestCase("help")]
    [TestCase("/?")]
    public void UnattachedHelpSpellingGetsARecoveryHintInText(string token)
    {
        var result = Run(token);

        result.ExitCode.ShouldBe(1);
        result.StandardOutput.ShouldBeEmpty();
        result.StandardError.ShouldBe(
            $"error CLI001 §6.2: '{token}' is not attached to any option. "
            + "Run 'namespace2xml --help' for the complete list.\n");
        result.Json.ShouldBeEmpty();
    }

    [TestCase("help")]
    [TestCase("/?")]
    public void UnattachedHelpSpellingGetsARecoveryHintInJson(string token)
    {
        var result = Run(token, "--diagnostics-format", "json");

        result.ExitCode.ShouldBe(1);
        result.StandardOutput.ShouldBeEmpty();
        result.StandardError.ShouldBeEmpty();

        var json = Encoding.UTF8.GetString(result.Json);
        using var document = JsonDocument.Parse(json);
        var diagnostics = document.RootElement;
        diagnostics.GetArrayLength().ShouldBe(1);

        var diagnostic = diagnostics[0];
        diagnostic.GetProperty("code").GetString().ShouldBe("CLI001");
        diagnostic.GetProperty("severity").GetString().ShouldBe("error");
        diagnostic.GetProperty("phase").GetString().ShouldBe("cli");
        diagnostic.GetProperty("spec").GetString().ShouldBe("§6.2");
        diagnostic.GetProperty("message").GetString().ShouldBe(
            $"'{token}' is not attached to any option. "
            + "Run 'namespace2xml --help' for the complete list.");
        json.ShouldBe(
            "[\n{\"code\":\"CLI001\",\"severity\":\"error\",\"phase\":\"cli\",\"spec\":\"§6.2\","
            + $"\"message\":\"'{token}' is not attached to any option. "
            + "Run 'namespace2xml --help' for the complete list.\"}\n]\n");
    }

    [TestCase("-h")]
    [TestCase("-?")]
    public void ExistingShortHelpAttemptsKeepTheirRecoveryHint(string token)
    {
        var result = Run(token);

        result.ExitCode.ShouldBe(1);
        result.StandardError.ShouldBe(
            $"error CLI001 §6.2: '{token}' is not a recognized option. "
            + "Run 'namespace2xml --help' for the complete list.\n");
    }

    [Test]
    public void HelpLookingValuesAndEndOfOptionsRemainLiteral()
    {
        CommandLineParser.Parse(["-s", "scheme.txt", "-i", "help"])
            .CommandLine.ShouldNotBeNull().Inputs.ShouldBe(["help"]);
        CommandLineParser.Parse(["--input=-h", "-s", "scheme.txt"])
            .CommandLine.ShouldNotBeNull().Inputs.ShouldBe(["-h"]);
        CommandLineParser.Parse(["-s", "scheme.txt", "-i", "--", "help", "/?", "-h", "--help"])
            .CommandLine.ShouldNotBeNull().Inputs.ShouldBe(["help", "/?", "-h", "--help"]);
    }

    [TestCase("help")]
    [TestCase("/?")]
    public void ValueSpecificAndEarlierFaultsDoNotBecomeHelpRequests(string value)
    {
        var invalidVerbosity = CommandLineParser.Parse(
            ["-i", "in.txt", "-s", "scheme.txt", "--verbosity", value]).Diagnostic.ShouldNotBeNull();
        invalidVerbosity.Message.ShouldContain("'--verbosity' accepts");
        invalidVerbosity.Message.ShouldNotContain("namespace2xml --help", Case.Sensitive);

        var earlierFault = CommandLineParser.Parse(["stray", value]).Diagnostic.ShouldNotBeNull();
        earlierFault.Message.ShouldBe("'stray' is not attached to any option.");

        var missingInput = CommandLineParser.Parse(["-i", "-h"]).Diagnostic.ShouldNotBeNull();
        missingInput.Message.ShouldContain("'--input' requires a value");
        missingInput.Message.ShouldNotContain("namespace2xml --help", Case.Sensitive);
    }

    [Test]
    public void InformationalPrecedenceStillBypassesMalformedArguments()
    {
        var version = Run("help", "--version", "--verbosity", "invalid");
        version.ExitCode.ShouldBe(0);
        version.StandardOutput.ShouldStartWith("name: namespace2xml\n");
        version.StandardError.ShouldBeEmpty();

        var help = Run("--help", "--version", "--verbosity", "invalid");
        help.ExitCode.ShouldBe(0);
        help.StandardOutput.ShouldNotBeEmpty();
        help.StandardOutput.ShouldNotStartWith("name: namespace2xml\n");
        help.StandardError.ShouldBeEmpty();

        var apparentValue = Run("--diagnostics-format", "--help");
        apparentValue.ExitCode.ShouldBe(0);
        apparentValue.StandardOutput.ShouldNotBeEmpty();
        apparentValue.StandardError.ShouldBeEmpty();
    }

    private static (int ExitCode, string StandardOutput, string StandardError, byte[] Json) Run(
        params string[] arguments)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        using var json = new MemoryStream();

        var exitCode = Program.Execute(
            arguments,
            stdout,
            stderr,
            (_, _) => throw new InvalidOperationException("The parser should reject this invocation."),
            json);

        return (exitCode, stdout.ToString(), stderr.ToString(), json.ToArray());
    }
}
