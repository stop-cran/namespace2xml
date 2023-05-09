using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Moq;
using Namespace2Xml.Formatters;
using Namespace2Xml.Scheme;
using Namespace2Xml.Syntax;
using NUnit.Framework;
using Shouldly;
using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Namespace2Xml.Tests
{
    public class FormatterBuilderTests
    {
        private Mock<IStreamFactory> streamFactory;
        private Mock<IServiceProvider> serviceProviderMock;
        private Mock<IOptions<QualifiedNameOptions>> optionsMock;
        private ILoggerFactory loggerFactory;
        private FormatterBuilder formatterBuilder;

        [SetUp]
        public void Setup()
        {
            streamFactory = new Mock<IStreamFactory>();
            serviceProviderMock = new Mock<IServiceProvider>();
            loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(new ConsoleLoggerProvider(Mock.Of<IOptionsMonitor<ConsoleLoggerOptions>>(f => f.CurrentValue == new ConsoleLoggerOptions())));

            optionsMock = new Mock<IOptions<QualifiedNameOptions>>();
            optionsMock.Setup(x => x.Value)
                .Returns(new QualifiedNameOptions { XmlRoot = "XmlRoot"});

            serviceProviderMock.Setup(x => x.GetService(typeof(IOptions<QualifiedNameOptions>)))
                .Returns(optionsMock.Object);

            formatterBuilder = new FormatterBuilder(serviceProviderMock.Object, streamFactory.Object, loggerFactory);
        }

        [Test]
        public void ShouldBuildIgnoreBuilder()
        {
            var formatters = formatterBuilder.Build(
                new SchemeNode("a".ToNamePart(),
                new[]
                {
                    new SchemeLeaf(EntryType.output, "xml,json,yaml,namespace,ini", Helpers.CreatePayload("", ""))
                }))
                .ToList();

            formatters.Count.ShouldBe(5);

            foreach (var (prefix, formatter) in formatters)
            {
                prefix.Parts
                    .ShouldHaveSingleItem()
                    .Tokens
                    .ShouldHaveSingleItem()
                    .ShouldBeOfType<TextNameToken>()
                    .Text
                    .ShouldBe("a");

                formatter.ShouldBeOfType<IgnoreFormatter>();
            }
        }

        [Test]
        public void ShouldDetectSchemeError()
        {
            Should.Throw<ApplicationException>(() =>
            formatterBuilder.Build(
                new SchemeNode("a".ToNamePart(),
                new ISchemeEntry[]
                {
                    new SchemeLeaf(EntryType.output, "xml", Helpers.CreatePayload("", "")),
                    new SchemeError("error1", new SourceMark(0,"testfile", 12)),
                })).ToList());
            // RK TODO: logger.Verify(l => l.Error(It.IsAny<object>()));
        }

        [Test]
        public void ShouldIgnoreSchemeIfNoOutput()
        {
            formatterBuilder.Build(
                new SchemeNode("a".ToNamePart(),
                new ISchemeEntry[]
                {
                    new SchemeLeaf(EntryType.filename, "xxx", Helpers.CreatePayload("", "")),
                }))
                .ShouldBeEmpty();
        }
    }
}
