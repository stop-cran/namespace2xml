using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline.Steps;
using Namespace2Xml.Profiles;
using Namespace2Xml.Scalars;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 18 scalar inference. Every expectation here is the canonical text the section says the
/// inferred value has, not the text the implementation happens to produce.
/// </summary>
[TestFixture]
public sealed class ScalarInferenceTests
{
    private static string Canonical(string raw) => ScalarInference.Infer(raw).ToCanonicalText();

    // Rule 1: the three literals, matched case-insensitively. Null carries no format-independent
    // text — Section 19 gives each format its own spelling — so it is asserted by kind.
    [TestCase("true")]
    [TestCase("false")]
    public void TheBooleanLiteralsAreRecognized(string raw) => Canonical(raw).ShouldBe(raw);

    [TestCase("null")]
    [TestCase("Null")]
    [TestCase("NULL")]
    public void TheNullLiteralIsRecognizedWithoutRegardToCase(string raw) =>
        ScalarInference.Infer(raw).Kind.ShouldBe(ScalarKind.Null);

    // Section 18 rules 1 and 2 are "exact case-insensitive", so a differently cased spelling is the
    // same literal and canonicalizes to the lowercase form.
    [TestCase("TRUE", "true")]
    [TestCase("True", "true")]
    [TestCase("FALSE", "false")]
    [TestCase("fAlSe", "false")]
    public void ABooleanIsMatchedWithoutRegardToCase(string raw, string canonical) =>
        Canonical(raw).ShouldBe(canonical);

    // "Exact" bounds the match to the whole value: no surrounding text, no prefix.
    [TestCase("truex")]
    [TestCase("xtrue")]
    [TestCase("true ")]
    [TestCase("nul")]
    public void AValueMerelyContainingALiteralStaysAString(string raw) => Canonical(raw).ShouldBe(raw);

    // Rule 3 is deliberately more permissive than JSON: a leading `+`, and leading zeros, are
    // integers here. Canonical integer text drops both.
    [TestCase("0", "0")]
    [TestCase("7", "7")]
    [TestCase("-7", "-7")]
    [TestCase("+7", "7")]
    [TestCase("007", "7")]
    [TestCase("-007", "-7")]
    [TestCase("+0", "0")]
    [TestCase("-0", "0")]
    public void IntegersAreInferredAndCanonicalized(string raw, string canonical) =>
        Canonical(raw).ShouldBe(canonical);

    // An integer beyond any fixed width is still an integer; nothing in Section 18 bounds it.
    [Test]
    public void AVeryLargeIntegerRemainsExact() =>
        Canonical("123456789012345678901234567890").ShouldBe("123456789012345678901234567890");

    // Rule 4 is JSON's number grammar. Canonical decimal text strips insignificant trailing zeros
    // and, per Section 18 step 7, appends `.0` when plain notation would otherwise be
    // indistinguishable from an integer — which is what keeps a decimal a decimal in output.
    [TestCase("1.50", "1.5")]
    [TestCase("1.0", "1.0")]
    [TestCase("-0.5", "-0.5")]
    [TestCase("1e3", "1000.0")]
    [TestCase("1E3", "1000.0")]
    [TestCase("1.5e-2", "0.015")]
    [TestCase("-0.0", "-0.0")]
    [TestCase("0.0", "0.0")]
    public void DecimalsAreInferredAndCanonicalized(string raw, string canonical) =>
        Canonical(raw).ShouldBe(canonical);

    // Rule 3 runs before rule 4, and only rule 3 accepts a leading `+` or leading zeros. Were the
    // order reversed, `0.5e1` and `+0.5` would change kind, so the ordering is asserted rather
    // than assumed.
    [Test]
    public void RuleThreeIsTriedBeforeRuleFour()
    {
        Canonical("007").ShouldBe("7");
        Canonical("+0.5").ShouldBe("+0.5");
    }

    // Forms JSON's number grammar excludes are not decimals, so they stay strings unchanged.
    [TestCase(".5")]
    [TestCase("5.")]
    [TestCase("1.2.3")]
    [TestCase("0x10")]
    [TestCase("1_000")]
    [TestCase("Infinity")]
    [TestCase("NaN")]
    [TestCase("1 000")]
    [TestCase("")]
    [TestCase(" 1")]
    [TestCase("1 ")]
    public void ANonNumberStaysAString(string raw) => Canonical(raw).ShouldBe(raw);

    // Inference never revisits a payload that already carries a type. A settled payload is
    // returned as it stands.
    [Test]
    public void AnAlreadyTypedPayloadIsLeftAlone()
    {
        var typed = ScalarInference.Infer("1.50");

        ScalarInference.Infer(typed).ShouldBeSameAs(typed);
    }

    [Test]
    public void AnUntypedPayloadIsSettled() =>
        ScalarInference.Infer(ScalarPayload.OfString("true")).ToCanonicalText().ShouldBe("true");

    [Test]
    public void ScalarInferenceHandlesTheDefaultDepthOnABoundedStack()
    {
        var position = StableOrderingKey.First;
        var name = new OrdinaryPart([new LiteralToken("a")]);
        var input = OverlayNode.OfPayload(ScalarPayload.Untyped("42"), position);
        const int depth = 511;
        for (var i = 0; i < depth; i++)
        {
            input = OverlayNode.Empty(NodeMarks.At(position)).WithChild(name, input);
        }

        OverlayNode? result = null;
        var worker = new Thread(() => result = InputPhase.InferScalarKinds(input).Value, 128 * 1024);
        worker.Start();
        worker.Join();

        var current = result.ShouldNotBeNull();
        for (var i = 0; i < depth; i++)
        {
            current = current.Children[name];
        }

        current.Payload.ShouldNotBeNull().Kind.ShouldBe(ScalarKind.Integer);
        current.Payload.ToCanonicalText().ShouldBe("42");
    }

    [Test]
    public void ScalarInferenceSettlesBothFacetsAndPreservesTypedValues()
    {
        var position = StableOrderingKey.First;
        var name = new OrdinaryPart([new LiteralToken("typed")]);
        var typed = OverlayNode.OfPayload(ScalarPayload.OfString("007"), position);
        var item = OverlayNode.OfPayload(ScalarPayload.Untyped("false"), position);
        var input = OverlayNode.OfPayload(ScalarPayload.Untyped("true"), position)
            .WithChild(name, typed)
            .WithSequenceItem(7, SequenceItem.Numbered(item));

        var result = InputPhase.InferScalarKinds(input).Value.ShouldNotBeNull();

        result.Payload.ShouldNotBeNull().Boolean.ShouldBeTrue();
        result.Children[name].ShouldBeSameAs(typed);
        result.Children[name].Payload.ShouldNotBeNull().ToCanonicalText().ShouldBe("007");
        result.Sequence[7].Node.Payload.ShouldNotBeNull().Boolean.ShouldBeFalse();
        result.Sequence[7].Provenance.ShouldBe(OrderingProvenance.Explicit);
        result.SequenceHighWater.ShouldBe(7);
    }
}
