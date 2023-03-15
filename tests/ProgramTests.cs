using Microsoft.Extensions.DependencyInjection;
using Moq;
using Namespace2Xml.Formatters;
using Namespace2Xml.Scheme;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Namespace2Xml.Tests
{
    public class ProgramTests
    {
        private Mock<IStreamFactory> streamFactory;
        private MemoryStream output;

        [SetUp]
        public void Setup()
        {
            streamFactory = new Mock<IStreamFactory>();
            output = new MemoryStream();

            streamFactory
                .Setup(f => f.CreateInputStream("input.properties"))
                .Returns<string>(_ => new MemoryStream(Encoding.UTF8.GetBytes("a.x=1")));

            streamFactory
                .Setup(f => f.CreateInputStream("scheme.properties"))
                .Returns<string>(_ => new MemoryStream(Encoding.UTF8.GetBytes("a.output=yaml")));

            streamFactory
                .Setup(f => f.CreateOutputStream("a.yaml", It.IsAny<OutputType>()))
                .Returns(output);

            Program.ServiceOverrides = serviceCollection =>
                serviceCollection.AddTransient<IProfileReader, ProfileReader>()
                .AddSingleton(streamFactory.Object);
        }

        [Test]
        public async Task ShouldRun()
        {
            var exitCode = await Program.Main(new[]
            {
                "-i", "input.properties",
                "-s", "scheme.properties"
            });

            exitCode.ShouldBe(0);

            Encoding.UTF8.GetString(output.ToArray())
                .Trim()
                .ShouldBe("x: 1");
        }

        [Test]
        public async Task ShouldLogUnexpectedError()
        {
            var exitCode = await Program.Main(new[]
            {
                "-i", "invalid.properties",
                "-s", "scheme.properties"
            });

            exitCode.ShouldBe(1);
            output.Length.ShouldBe(0);
        }

        [Test]
        public async Task ShouldReturnOneUnexpectedError()
        {
            var reader = new Mock<IProfileReader>();

            reader.Setup(r => r.ReadFiles(It.IsAny<IEnumerable<string>>(), default))
                .Throws<TestException>();

            Program.ServiceOverrides = serviceCollection => serviceCollection.AddSingleton(reader.Object);

            await Program.Main(new[]
            {
                "-i", "input.properties",
                "-s", "scheme.properties"
            }).ShouldThrowAsync<TestException>();

            output.Length.ShouldBe(0);
        }

        private class TestException : Exception { }
    }
}
