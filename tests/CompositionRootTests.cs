using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Moq;
using Namespace2Xml.Formatters;
using Namespace2Xml.Scheme;
using Namespace2Xml.Semantics;
using Namespace2Xml.Syntax;
using NUnit.Framework;
using Shouldly;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Namespace2Xml.Tests
{
    public class CompositionRootTests
    {
        private Mock<IStreamFactory> streamFactory;
        private Mock<IProfileReader> profileReader;
        private Mock<ITreeBuilder> treeBuilder;
        private Mock<IFormatterBuilder> formatterBuilder;
        private LoggerFactory loggerFactory;
        private MemoryStream output;

        [SetUp]
        public void Setup()
        {
            profileReader = new Mock<IProfileReader>();
            output = new MemoryStream();
            streamFactory = new Mock<IStreamFactory>();
            treeBuilder = new Mock<ITreeBuilder>();
            formatterBuilder = new Mock<IFormatterBuilder>();
            loggerFactory = new LoggerFactory();

            profileReader.Setup(r => r.ReadFiles(new[] { "input" }, default))
                .Returns(Task.FromResult<IReadOnlyList<IProfileEntry>>(new[]
                {
                    new Payload(
                        new[]{ "a", "x" }.ToQualifiedName(),
                        new[]{ new TextValueToken("1") },
                        new SourceMark(0, "testfile", 1))
                }));

            profileReader.Setup(r => r.ReadFiles(new[] { "scheme" }, default))
                .Returns(Task.FromResult<IReadOnlyList<IProfileEntry>>(new[]
                {
                    new Payload(
                        new[]{ "a", "output" }.ToQualifiedName(),
                        new[]{ new TextValueToken("yaml") },
                        new SourceMark(0, "schemefile", 1))
                }));

            profileReader.Setup(r => r.ReadVariables(It.IsAny<IEnumerable<string>>()))
                .Returns(new IProfileEntry[0]);

            streamFactory
                .Setup(f => f.CreateOutputStream("a.yml", It.IsAny<OutputType>()))
                .Returns(output);

            loggerFactory.AddProvider(new ConsoleLoggerProvider(Mock.Of<IOptionsMonitor<ConsoleLoggerOptions>>(f => f.CurrentValue == new ConsoleLoggerOptions())));
        }

        [Test]
        public async Task ShouldWriteOutput()
        {
            await new CompositionRoot(
                profileReader.Object,
                new TreeBuilder(Mock.Of<ILogger<TreeBuilder>>()),
                new FormatterBuilder(streamFactory.Object, loggerFactory)).Write(
                new Arguments(
                    new[] { "input" },
                    new[] { "scheme" },
                    ".",
                    null,
                    null
                    ), default);

            Encoding.UTF8.GetString(output.ToArray())
                .Trim()
                .ShouldBe("x: 1");
        }

        [Test]
        public async Task ShouldApplyVariables()
        {
            profileReader.Setup(r => r.ReadVariables(new[] { "a.x=2" }))
                .Returns(new[]
                {
                    new Payload(
                        new[]{ "a", "x" }.ToQualifiedName(),
                        new[]{ new TextValueToken("2") },
                        new SourceMark(0, "variables", 1))
                });

            await new CompositionRoot(
                profileReader.Object,
                new TreeBuilder(Mock.Of<ILogger<TreeBuilder>>()),
                new FormatterBuilder(streamFactory.Object, loggerFactory)).Write(
                new Arguments(
                    new[] { "input" },
                    new[] { "scheme" },
                    ".",
                    null,
                    new[] { "a.x=2" }
                    ), default);

            Encoding.UTF8.GetString(output.ToArray())
                .Trim()
                .ShouldBe("x: 2");
        }
    }
}
