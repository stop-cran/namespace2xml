using log4net;
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
    public class FileStreamFactoryTests
    {
        private string tempDirectoryName;
        private FileStreamFactory fileStreamFactory;
        private Mock<ILog> logger;

        [SetUp]
        public void Setup()
        {
            tempDirectoryName = "temp" + new Random().Next();

            Directory.CreateDirectory(tempDirectoryName);

            logger = new Mock<ILog>();
            fileStreamFactory = new FileStreamFactory(tempDirectoryName, logger.Object);
        }

        [TearDown]
        public void RemoveTempFiles()
        {
            Directory.Delete(tempDirectoryName, true);
        }

        private async Task WriteTempFile(string file, params string[] lines) =>
            await File.WriteAllLinesAsync(Path.Combine(tempDirectoryName, file), lines);

        [Test]
        public async Task ShouldRead()
        {
            string text;

            await WriteTempFile("input.properties", "qwerty");

            using (var stream = fileStreamFactory.CreateInputStream(Path.Combine(tempDirectoryName, "input.properties")))
            {
                stream.ShouldBeOfType<FileStream>();

                using (var reader = new StreamReader(stream))
                    text = await reader.ReadToEndAsync();
            }

            text.TrimEnd().ShouldBe("qwerty");
        }

        [Test]
        public async Task ShouldWrite()
        {
            using (var stream = fileStreamFactory.CreateOutputStream("output.properties", OutputType.yaml))
            {
                stream.ShouldBeOfType<FileStream>();

                using (var writer = new StreamWriter(stream))
                    await writer.WriteAsync("asdfgh");
            }

            var text = await File.ReadAllTextAsync(Path.Combine(tempDirectoryName, "output.properties"));

            text.ShouldBe("asdfgh");
        }
    }
}