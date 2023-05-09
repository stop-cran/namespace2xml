using System;
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
using Microsoft.Extensions.DependencyInjection;

namespace Namespace2Xml.Tests
{
    public class CompositionRootTests
    {
        private Mock<IStreamFactory> streamFactory;
        private Mock<IProfileReader> profileReader;
        private Mock<IServiceProvider> serviceProviderMock;
        private Mock<IOptions<QualifiedNameOptions>> optionsMock;
        private LoggerFactory loggerFactory;
        private MemoryStream output;

        [SetUp]
        public void Setup()
        {
            profileReader = new Mock<IProfileReader>();
            output = new MemoryStream();
            streamFactory = new Mock<IStreamFactory>();
            serviceProviderMock = new Mock<IServiceProvider>();
            optionsMock = new Mock<IOptions<QualifiedNameOptions>>();
            loggerFactory = new LoggerFactory();

            optionsMock.Setup(x => x.Value)
                .Returns(new QualifiedNameOptions { XmlRoot = "XmlRoot"});
            serviceProviderMock.Setup(x => x.GetService(typeof(IOptions<QualifiedNameOptions>)))
                .Returns(optionsMock.Object);
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
                .Setup(f => f.CreateOutputStream("a.yaml", It.IsAny<OutputType>()))
                .Returns(output);

            loggerFactory.AddProvider(new ConsoleLoggerProvider(Mock.Of<IOptionsMonitor<ConsoleLoggerOptions>>(f => f.CurrentValue == new ConsoleLoggerOptions())));
        }

        [Test]
        public async Task ShouldWriteOutput()
        {
            await new CompositionRoot(
                profileReader.Object,
                new TreeBuilder(Mock.Of<ILogger<TreeBuilder>>()),
                new FormatterBuilder(serviceProviderMock.Object, streamFactory.Object, loggerFactory),
                Mock.Of<ILogger<CompositionRoot>>()).Write(
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
                new FormatterBuilder(serviceProviderMock.Object, streamFactory.Object, loggerFactory),
                Mock.Of<ILogger<CompositionRoot>>()).Write(
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
