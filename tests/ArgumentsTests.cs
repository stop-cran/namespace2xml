using log4net.Core;
using Namespace2Xml;
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
                "trace",
                null);

            arguments.Verbosity.ShouldBe("trace");
            arguments.LoggingLevel.ShouldBe(Level.Trace);
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
