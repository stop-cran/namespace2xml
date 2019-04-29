using Moq;
using Namespace2Xml;
using NUnit.Framework;
using NUnit.Framework.Internal;
using Shouldly;
using System;

namespace Namespace2Xml.Tests
{
    public class LazyStringTests
    {
        private Mock<object> objectMock;

        [SetUp]
        public void Setup()
        {
            objectMock = new Mock<object>();
        }

        [Test]
        public void ShouldNotCallToStringBeforeToStrring()
        {
            var mock = new Mock<Func<string>>();

            var lazy = new LazyString(mock.Object);

            mock.Verify(f => f(), Times.Never());
        }

        [Test]
        public void ShouldCallToStringOnToStrring()
        {
            var mock = new Mock<Func<string>>();

            mock.Setup(f => f()).Returns("test123");

            new LazyString(mock.Object).ToString().ShouldBe("test123");

            mock.Verify(f => f());
        }
    }
}
