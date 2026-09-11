using Namespace2Xml.Gatekeeper;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

[TestFixture]
public class NUnitTestDiscoveryTests
{
    [Test]
    public void PinnedAdapterListOutputAcceptsItsExactHeaderAndDisplayLayout()
    {
        var output =
            """
            Test run for C:\repo\Tests.dll (.NETCoreApp,Version=v10.0)
            VSTest version 18.0.0

            The following Tests are available:
                Tests.OrdinaryFixture.AnOrdinaryTest
                Tests.ParameterizedFixture.AnExplicitlyNamedCase

            """;

        NUnitListTestsParser.Parse(output).ShouldBe(
        [
            "Tests.OrdinaryFixture.AnOrdinaryTest",
            "Tests.ParameterizedFixture.AnExplicitlyNamedCase",
        ]);
    }

    [Test]
    public void PinnedAdapterProtocolPreservesFullyQualifiedLeafNames()
    {
        string[] lines =
        [
            """
            TpTrace Verbose: TestRequestSender.OnDiscoveryMessageReceived: Received message: {"Version":7,"MessageType":"TestDiscovery.TestFound","Payload":[{"FullyQualifiedName":"Tests.OrdinaryFixture.AnOrdinaryTest","DisplayName":"AnOrdinaryTest","Source":"C:\\repo\\Tests.dll"}]}
            """,
            """
            TpTrace Verbose: TestRequestSender.OnDiscoveryMessageReceived: Received message: {"Version":7,"MessageType":"TestDiscovery.Completed","Payload":{"TotalTests":2,"LastDiscoveredTests":[{"FullyQualifiedName":"Tests.ParameterizedFixture.AnExplicitlyNamedCase","DisplayName":"AnExplicitlyNamedCase","Source":"C:\\repo\\Tests.dll"}]}}
            """,
        ];

        NUnitDiscoveryProtocolParser.Parse(lines, @"C:\repo\Tests.dll").ShouldBe(
        [
            "Tests.OrdinaryFixture.AnOrdinaryTest",
            "Tests.ParameterizedFixture.AnExplicitlyNamedCase",
        ]);
    }

    [Test]
    public void UnknownAndEmptyAdapterLayoutsFailClosed()
    {
        string[] invalid =
        [
            "Tests.OrdinaryFixture.AnOrdinaryTest",
            "The following Tests are available:\n",
            "The following Tests are available:\n  Tests.One\nSummary\n",
        ];

        foreach (var output in invalid)
        {
            Should.Throw<InvalidDataException>(() => NUnitListTestsParser.Parse(output), output);
        }
    }

    [Test]
    public void IncompleteProtocolCatalogsFailClosed()
    {
        string[] incomplete =
        [
            """
            TpTrace Verbose: TestRequestSender.OnDiscoveryMessageReceived: Received message: {"Version":7,"MessageType":"TestDiscovery.Completed","Payload":{"TotalTests":1,"LastDiscoveredTests":[]}}
            """,
        ];
        Should.Throw<InvalidDataException>(() =>
            NUnitDiscoveryProtocolParser.Parse(incomplete, @"C:\repo\Tests.dll"));
    }
}
