using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Shouldly;
using System;

namespace Namespace2Xml.Tests
{
    public class ArgumentsTests
    {
        [Test]
        public void ShouldParseLoggingLevel()
        {
            var arguments = new Arguments(
                null,
                null,
                "",
                "debug",
                null);

            arguments.Verbosity.ShouldBe("debug");
            arguments.LoggingLevel.ShouldBe(LogLevel.Debug);
        }

        [Test]
        public void ShouldThrowOnIncorrectVerbosity() =>
            Should.Throw<ArgumentException>(() =>
                new Arguments(
                    null,
                    null,
                    "",
                    "ttt",
                    null));
    }
}
