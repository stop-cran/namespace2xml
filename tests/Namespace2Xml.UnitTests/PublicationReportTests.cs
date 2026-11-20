using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Namespace2Xml.Budgets;
using Namespace2Xml.Cli;
using Namespace2Xml.Contract;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Output;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Pipeline.Steps;
using Namespace2Xml.Scheme;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

[TestFixture]
public sealed class PublicationReportTests
{
    private static CommandLine Command(string report, string outputRoot) =>
        new([], [], [], outputRoot, Verbosity.Information, DiagnosticFormat.Json, ResourceLimits.Defaults, ReportFile: report);

    [Test]
    public void PreparationUsesExistingParentsAndRefusesAliases()
    {
        var root = Path.Combine(Path.GetTempPath(), $"n2x-report-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var report = Path.Combine(root, "report.json");
            var outputRoot = Path.Combine(root, "out");
            using (var stream = ReportFile.Prepare(Command(report, outputRoot), []))
            {
                stream.Write("report"u8);
            }

            File.ReadAllText(report).ShouldBe("report");
            var collision = Command(report, outputRoot) with { Inputs = [report.ToUpperInvariant()] };
            Should.Throw<UncontainableDestinationException>(() => ReportFile.Prepare(collision, []));
            File.ReadAllText(report).ShouldBe("report");
            Should.Throw<IOException>(() => ReportFile.Prepare(
                Command(Path.Combine(root, "absent", "report.json"), outputRoot), []));
            Directory.Exists(Path.Combine(root, "absent")).ShouldBeFalse();
            var destination = new PlannedOutput(
                new DestinationPath("a.json"), new FoldKey(0, 0, 0, ""), OutputBuffer.Empty);
            Should.Throw<UncontainableDestinationException>(() => ReportFile.Prepare(
                Command(Path.Combine(outputRoot, "A.JSON"), outputRoot), [destination]));
            Should.Throw<UncontainableDestinationException>(() => ReportFile.Prepare(
                Command(outputRoot, outputRoot), [destination]));
            Should.Throw<UncontainableDestinationException>(() => ReportFile.Prepare(
                Command(Path.Combine(outputRoot, "a.json", "child"), outputRoot), [destination]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>New POSIX reports are private, and rewriting one preserves its existing mode.</summary>
    [Test]
    public void PosixReportCreationIsPrivateAndRewritePreservesPermissions()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("POSIX file modes are not available on Windows.");
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), $"n2x-report-mode-{Guid.NewGuid():N}");
        Directory.CreateDirectory(
            root,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        try
        {
            var report = Path.Combine(root, "report.json");
            var command = Command(report, Path.Combine(root, "out"));
            using (var stream = ReportFile.Prepare(command, []))
            {
                stream.Write("first"u8);
            }

            var privateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            ((int)File.GetUnixFileMode(report) & ~(int)privateMode).ShouldBe(0);

            var existingMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
            File.SetUnixFileMode(report, existingMode);
            using (var stream = ReportFile.Prepare(command, []))
            {
                stream.Write("x"u8);
            }

            ((int)File.GetUnixFileMode(report)).ShouldBe((int)existingMode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void WarningRefusalHasCanonicalBytesAndNoFabricatedObservations()
    {
        var buffer = new OutputBufferWriter(new GlobalBudget(ResourceLimits.Defaults));
        buffer.TryWrite("{\n  \"x\": 1\n}\n"u8).ShouldBeTrue();
        var planned = new PlannedOutput(new DestinationPath("a.json"), new FoldKey(0, 0, 0, "a"), buffer.Build())
        {
            History = [new PublicationContribution("scheme.properties", 1, 1, "a.output", 0, 0, "a", "", OutputFormat.Json)],
        };
        var report = new PublicationReport(
            PublicationOutcome.WarningPolicyRefused, true, false, false,
            [new PublicationObservation(planned, null, null, PublicationStatus.Unattempted, null, "warningPolicy")]);
        var bundle = ContractBundle.Current;
        var hash = Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(bundle.SpecificationSha256 + "\n" + bundle.RegistrySha256)));

        var expected =
            "{\"schemaVersion\":1,\"productVersion\":\"" + ContractBundle.ProductVersion
            + "\",\"contract\":{\"revision\":\"" + bundle.Revision
            + "\",\"hash\":\"" + hash
            + "\",\"specificationSha256\":\"" + bundle.SpecificationSha256
            + "\",\"registrySha256\":\"" + bundle.RegistrySha256
            + "\"},\"exitCode\":1,\"outcome\":\"warningPolicyRefused\",\"planComplete\":true,"
            + "\"publicationOccurred\":false,\"sideEffectsPossible\":false,\"outputs\":["
            + "{\"destination\":\"a.json\",\"format\":\"json\",\"contributions\":["
            + "{\"source\":\"scheme.properties\",\"sourceIndex\":1,\"line\":1,\"declaration\":\"a.output\","
            + "\"formatIndex\":0,\"matchIndex\":0,\"selector\":\"a\",\"root\":\"\",\"format\":\"json\"}],"
            + "\"byteLength\":13,\"sha256\":\"c5e1c113d5c75240d109ea470244fa988c5d9970f26bdbbb885876ae1da9d2d7\","
            + "\"existedBefore\":null,\"bytesDiffered\":null,\"status\":\"unattempted\","
            + "\"failureCode\":null,\"unattemptedReason\":\"warningPolicy\"}]}\n";
        report.ToCanonicalBytes().ShouldBe(Encoding.UTF8.GetBytes(expected));
    }

    [Test]
    public void PipelineReportsFirstAndEqualPublication()
    {
        var root = Path.Combine(Path.GetTempPath(), $"n2x-report-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "input.properties");
            var scheme = Path.Combine(root, "scheme.properties");
            var report = Path.Combine(root, "report.json");
            var outputRoot = Path.Combine(root, "out");
            File.WriteAllText(input, "a.x=1\n");
            File.WriteAllText(scheme, "a.output=json\n");
            var command = Command(report, outputRoot) with { Inputs = [input], Schemes = [scheme] };

            Transformation.Run(command).ExitCode.ShouldBe(0);
            using (var first = JsonDocument.Parse(File.ReadAllBytes(report)))
            {
                var output = first.RootElement.GetProperty("outputs")[0];
                output.GetProperty("destination").GetString().ShouldBe("a.json");
                output.GetProperty("status").GetString().ShouldBe("published");
                output.GetProperty("existedBefore").GetBoolean().ShouldBeFalse();
                output.GetProperty("bytesDiffered").GetBoolean().ShouldBeTrue();
                output.GetProperty("byteLength").GetInt64().ShouldBe(13);
            }

            Transformation.Run(command).ExitCode.ShouldBe(0);
            using var second = JsonDocument.Parse(File.ReadAllBytes(report));
            second.RootElement.GetProperty("outputs")[0].GetProperty("status").GetString().ShouldBe("unchanged");
            second.RootElement.GetProperty("publicationOccurred").GetBoolean().ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void PipelineDistinguishesWarningRefusalFromIncompletePlan()
    {
        var root = Path.Combine(Path.GetTempPath(), $"n2x-report-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "input.properties");
            var missing = Path.Combine(root, "missing.properties");
            var scheme = Path.Combine(root, "scheme.properties");
            var report = Path.Combine(root, "report.json");
            var outputRoot = Path.Combine(root, "out");
            File.WriteAllText(input, "a.x=1\n");
            File.WriteAllText(scheme, "a.output=json\n");
            var command = Command(report, outputRoot) with
            {
                Inputs = [input, missing],
                Schemes = [scheme],
                FailOnWarning = true,
                Verbosity = Verbosity.None,
            };

            Transformation.Run(command, sink: new ForbiddenPublicationSink()).ExitCode.ShouldBe(1);
            using (var refusal = JsonDocument.Parse(File.ReadAllBytes(report)))
            {
                refusal.RootElement.GetProperty("outcome").GetString().ShouldBe("warningPolicyRefused");
                refusal.RootElement.GetProperty("planComplete").GetBoolean().ShouldBeTrue();
                refusal.RootElement.GetProperty("sideEffectsPossible").GetBoolean().ShouldBeFalse();
                var output = refusal.RootElement.GetProperty("outputs")[0];
                output.GetProperty("status").GetString().ShouldBe("unattempted");
                output.GetProperty("unattemptedReason").GetString().ShouldBe("warningPolicy");
                output.GetProperty("existedBefore").ValueKind.ShouldBe(JsonValueKind.Null);
            }

            Directory.Exists(outputRoot).ShouldBeFalse();
            File.WriteAllText(scheme, "a.output=not-a-format\n");
            Transformation.Run(command).ExitCode.ShouldBe(1);
            using var validation = JsonDocument.Parse(File.ReadAllBytes(report));
            validation.RootElement.GetProperty("outcome").GetString().ShouldBe("validationFailed");
            validation.RootElement.GetProperty("planComplete").GetBoolean().ShouldBeFalse();
            validation.RootElement.GetProperty("outputs").GetArrayLength().ShouldBe(0);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class ForbiddenPublicationSink : IPublicationSink
    {
        public bool SupportsSecureContainment => throw new InvalidOperationException("Configuration sink was accessed.");

        public void CreateDirectory(string root, string relative) =>
            throw new InvalidOperationException("Configuration sink was accessed.");

        public bool Write(string root, string relative, OutputBuffer buffer) =>
            throw new InvalidOperationException("Configuration sink was accessed.");
    }

    [Test]
    public void SuccessfulEmptyPlanDoesNotCreateOrValidateOutputRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"n2x-report-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "input.properties");
            var scheme = Path.Combine(root, "scheme.properties");
            var outputRoot = Path.Combine(root, "output-file");
            var report = Path.Combine(root, "report.json");
            File.WriteAllText(input, "a.x=1\n");
            File.WriteAllText(scheme, "a.output=ignore\n");
            File.WriteAllText(outputRoot, "keep");
            var command = Command(report, outputRoot) with { Inputs = [input], Schemes = [scheme] };

            Transformation.Run(command).ExitCode.ShouldBe(0);
            using var document = JsonDocument.Parse(File.ReadAllBytes(report));
            document.RootElement.GetProperty("outcome").GetString().ShouldBe("success");
            document.RootElement.GetProperty("outputs").GetArrayLength().ShouldBe(0);
            document.RootElement.GetProperty("publicationOccurred").GetBoolean().ShouldBeFalse();
            document.RootElement.GetProperty("sideEffectsPossible").GetBoolean().ShouldBeFalse();
            File.ReadAllText(outputRoot).ShouldBe("keep");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestCase("prepare")]
    [TestCase("write")]
    [TestCase("tooLargeWrite")]
    [TestCase("flush")]
    [TestCase("close")]
    public void ReportFailuresRefuseOrFollowConfigurationPublication(string fault)
    {
        var root = Path.Combine(Path.GetTempPath(), $"n2x-report-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "input.properties");
            var scheme = Path.Combine(root, "scheme.properties");
            var report = Path.Combine(root, "report.json");
            var outputRoot = Path.Combine(root, "out");
            File.WriteAllText(input, "a.x=1\n");
            File.WriteAllText(scheme, "a.output=json\n");
            var command = Command(report, outputRoot) with { Inputs = [input], Schemes = [scheme] };
            var result = Transformation.Run(command, null, null, null, (line, outputs) =>
                fault == "prepare"
                    ? throw new IOException("injected preparation failure")
                    : new ReportFailureStream(ReportFile.Prepare(line, outputs), fault));

            result.ExitCode.ShouldBe(1);
            result.Diagnostics.Count(diagnostic => diagnostic.Code == "REPORT001").ShouldBe(1);
            File.Exists(Path.Combine(outputRoot, "a.json")).ShouldBe(fault != "prepare");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void ReportFailureLifecycleCoversPreparationAndFinalization()
    {
        foreach (var fault in new[] { "prepare", "write", "tooLargeWrite", "flush", "close" })
        {
            ReportFailuresRefuseOrFollowConfigurationPublication(fault);
        }
    }

    [Test]
    public void ReportFailureCoexistsWithAConfigurationFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), $"n2x-report-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "input.properties");
            var scheme = Path.Combine(root, "scheme.properties");
            var report = Path.Combine(root, "report.json");
            var outputRoot = Path.Combine(root, "out");
            File.WriteAllText(input, "a.x=1\n");
            File.WriteAllText(scheme, "a.output=json\n");
            var command = Command(report, outputRoot) with { Inputs = [input], Schemes = [scheme] };
            var sink = new FileSystemPublicationSink((_, comparison, stream) =>
                comparison ? stream : new ReportFailureStream(stream, "tooLargeWrite"));
            var result = Transformation.Run(command, null, sink, null, (line, outputs) =>
                new ReportFailureStream(ReportFile.Prepare(line, outputs), "tooLargeWrite"));

            result.ExitCode.ShouldBe(1);
            result.Diagnostics.Select(diagnostic => diagnostic.Code).ShouldBe(["PATH002", "REPORT001"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class ReportFailureStream(Stream inner, string fault) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (fault == "write")
            {
                inner.Write(buffer[..Math.Min(1, buffer.Length)]);
                throw new IOException("injected write failure");
            }

            if (fault == "tooLargeWrite")
            {
                throw LengthFailure("Specified file length was too large for the file system");
            }

            inner.Write(buffer);
        }

        public override void Flush()
        {
            inner.Flush();
            if (fault == "flush")
            {
                throw new IOException("injected flush failure");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                if (fault == "close")
                {
                    throw new IOException("injected close failure");
                }
            }

            base.Dispose(disposing);
        }

        private static ArgumentOutOfRangeException LengthFailure(string value) => new(nameof(value), value);
    }
}
