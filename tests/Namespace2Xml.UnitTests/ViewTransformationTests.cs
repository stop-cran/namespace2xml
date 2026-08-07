using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Pipeline;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Specification Section 15.1 step 16: the path-scoped <c>key</c> and <c>type</c> transformations.
/// </summary>
/// <remarks>
/// <para>
/// Every expectation is read from Sections 16.5 and 16.6. The worked example in Section 16.5 is
/// asserted exactly as the specification prints it, including its namespace projection.
/// </para>
/// <para>
/// Assertions go through rendered namespace output rather than through the overlay, because that
/// is where a reader can check them against the specification's own examples. The one thing
/// namespace output cannot show is <c>type=mapping</c> — Section 19.1 spells a sequence item and a
/// canonical numeric mapping key identically — so that directive is asserted through the shape it
/// forces a later <c>multiline</c> to meet.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ViewTransformationTests
{
    // ---- 16.5 key ------------------------------------------------------------------------------

    /// <summary>
    /// Section 16.5's worked example, with the namespace projection the specification prints for
    /// it. The selector prefix <c>a</c> is not part of the emitted names, so the printed
    /// <c>a.0.name=b</c> appears here as <c>0.name=b</c>.
    /// </summary>
    [Test]
    public void TheWorkedExampleProducesTheProjectionSectionSixteenFivePrints() =>
        Render("a.b.x=1\na.c.x=2\n", "a.key=name\n")
            .ShouldBe("0.name=b\n0.x=1\n1.name=c\n1.x=2\n");

    /// <summary>
    /// Section 16.5: "the generated key field is inserted first". First is a position in Section
    /// 5.2 mapping order, not an accident of the name: <c>zz</c> sorts after <c>x</c> by name and
    /// must still be emitted before it.
    /// </summary>
    [Test]
    public void TheGeneratedFieldIsFirstWhateverItIsNamed() =>
        Render("a.b.x=1\n", "a.key=zz\n").ShouldBe("0.zz=b\n0.x=1\n");

    /// <summary>
    /// Section 16.5: "a child already carrying ordering-value provenance retains that value and
    /// provenance", while "a child without ordering provenance receives a fresh implicit ordering
    /// value in mapping order". Section 5.4 reserves the canonical numeric child's value, so the
    /// fresh one is allocated above it rather than colliding with it.
    /// </summary>
    [Test]
    public void ANumericChildKeepsItsOrderingValueAndOthersAreAllocatedAboveIt() =>
        Render("a.0.x=1\na.b.x=2\n", "a.key=name\n")
            .ShouldBe("0.name=0\n0.x=1\n1.name=b\n1.x=2\n");

    /// <summary>
    /// Section 16.5: "a child without a mapping projection becomes a record containing the
    /// generated key field followed by <c>value</c> holding the complete child overlay".
    /// </summary>
    [Test]
    public void AScalarChildBecomesAKeyAndAValueField() =>
        Render("a.b=1\n", "a.key=name\n").ShouldBe("0.name=b\n0.value=1\n");

    /// <summary>
    /// Section 16.5: "every independent scalar, null, or sequence projection of that child is
    /// placed under a field named <c>value</c>". The mapping fields stay in the record beside it.
    /// </summary>
    [Test]
    public void AnIndependentScalarOfAMappingChildMovesUnderValue() =>
        Render("a.b=1\na.b.x=2\n", "a.key=name\n")
            .ShouldBe("0.name=b\n0.value=1\n0.x=2\n");

    /// <summary>
    /// Section 16.5: "if the child already contains that field name, processing is blocking
    /// <c>TYPE001</c>". The transformation must not replace data the input supplied.
    /// </summary>
    [Test]
    public void AChildAlreadyHoldingTheKeyFieldIsRejected() =>
        Failure("a.b.name=x\n", "a.key=name\n").ShouldBe("TYPE001");

    /// <summary>Section 16.5: "if <c>value</c> would collide, processing is blocking TYPE001".</summary>
    [Test]
    public void AChildAlreadyHoldingValueBesideAnIndependentScalarIsRejected() =>
        Failure("a.b=1\na.b.value=2\n", "a.key=name\n").ShouldBe("TYPE001");

    /// <summary>
    /// Section 16.5: "applying <c>key</c> to a sequence-only or scalar-only target is
    /// <c>TYPE001</c>".
    /// </summary>
    [Test]
    public void KeyOnAScalarOnlyTargetIsRejected() =>
        Failure("a.b=1\n", "a.b.key=name\n").ShouldBe("TYPE001");

    /// <summary>
    /// Section 16.5: "if several directives match, the later directive wins. A later directive may
    /// therefore intentionally replace the key-field name chosen by an earlier wildcard rule."
    /// </summary>
    [Test]
    public void ALaterKeyDirectiveReplacesAnEarlierWildcardOne() =>
        Render("a.b.x=1\n", "*.key=wrong\na.key=right\n").ShouldBe("0.right=b\n0.x=1\n");

    /// <summary>
    /// Section 16.5: "an effective <c>type=array</c> and <c>key</c> at the same path is therefore
    /// an illegal option combination and raises <c>SCHEME001</c>; implementations must not silently
    /// choose an order or reinterpret the resulting sequence as a mapping."
    /// </summary>
    [Test]
    public void ArrayAndKeyAtOnePathIsAnIllegalCombination() =>
        Failure("a.b.x=1\n", "a.key=name\na.type=array\n").ShouldBe("SCHEME001");

    // ---- 16.6 array ----------------------------------------------------------------------------

    /// <summary>
    /// Section 16.6: "otherwise every key is discarded and every child ... receives a fresh
    /// implicit ordering value above the node's current high-water mark in current mapping order."
    /// </summary>
    [Test]
    public void ArrayDiscardsNonNumericKeysAndAllocatesFreshValues() =>
        Render("a.b=1\na.c=2\n", "a.type=array\n").ShouldBe("0=1\n1=2\n");

    /// <summary>
    /// Section 16.6: "if the winning container is already a sequence, retain it". Section 8.7 has
    /// already inferred this one, so the directive changes nothing.
    /// </summary>
    [Test]
    public void ArrayOnAnInferredSequenceRetainsIt() =>
        Render("a.0=x\na.1=y\n", "a.type=array\n").ShouldBe("0=x\n1=y\n");

    /// <summary>Section 16.6: "if no container projection exists, it is a blocking type error".</summary>
    [Test]
    public void ArrayOnAScalarOnlyPathIsRejected() =>
        Failure("a.b=1\n", "a.b.type=array\n").ShouldBe("TYPE001");

    /// <summary>
    /// Section 16.6: "an independent scalar payload remains available to formats that can
    /// represent both", so converting the container does not discard the payload. Section 19.1
    /// emits "a node's own scalar before anything beneath it".
    /// </summary>
    [Test]
    public void ArrayKeepsAnIndependentScalarPayload() =>
        Render("a.b=1\na.b.c=2\n", "a.b.type=array\n").ShouldBe("b=1\nb.0=2\n");

    // ---- 16.6 mapping --------------------------------------------------------------------------

    /// <summary>
    /// Section 16.6: <c>mapping</c> "is the explicit escape hatch for preserving numeric keys as
    /// mapping keys rather than projecting them as an array". Section 19.1 spells the two
    /// identically, so the proof is what a later pass then meets: Section 16.5 <c>key</c> demands
    /// an ordered mapping and rejects the Section 8.7 inferred sequence, and the same input with
    /// <c>type=mapping</c> is accepted because step 16 runs <c>type</c> before <c>key</c>.
    /// </summary>
    [Test]
    public void MappingUndoesSequenceInference()
    {
        Failure("a.b.0=x\na.b.1=y\n", "a.b.key=name\n").ShouldBe("TYPE001");

        Render("a.b.0=x\na.b.1=y\n", "a.b.type=mapping\na.b.key=name\n")
            .ShouldBe("b.0.name=0\nb.0.value=x\nb.1.name=1\nb.1.value=y\n");
    }

    /// <summary>
    /// Section 16.6: "<c>ignore</c>, <c>default</c>, and <c>mapping</c> must appear alone. ...
    /// Every other combination is a blocking scheme error."
    /// </summary>
    [Test]
    public void MappingMayNotAppearWithAnotherType() =>
        Failure("a.b.0=x\n", "a.b.type=mapping,multiline\n").ShouldBe("SCHEME001");

    /// <summary>Section 16.6: "if no container projection exists, it is a blocking type error".</summary>
    [Test]
    public void MappingOnAScalarOnlyPathIsRejected() =>
        Failure("a.b=1\n", "a.b.type=mapping\n").ShouldBe("TYPE001");

    // ---- 16.6 multiline ------------------------------------------------------------------------

    /// <summary>
    /// Section 16.6: "combines the matching sequence of scalar lines using logical LF", and Section
    /// 19.1 emits LF in a namespace value as <c>\n</c> because "physical output entries are always
    /// one line".
    /// </summary>
    [Test]
    public void MultilineJoinsASequenceWithLineFeeds() =>
        Render("a.b.0=x\na.b.1=y\n", "a.b.type=multiline\n").ShouldBe("b=x\\ny\n");

    /// <summary>Section 16.6: "a lone scalar is a one-line value and is unchanged".</summary>
    [Test]
    public void MultilineLeavesALoneScalarAlone() =>
        Render("a.b=x\n", "a.b.type=multiline\n").ShouldBe("b=x\n");

    /// <summary>Section 16.6: "null contributes an empty line".</summary>
    [Test]
    public void MultilineRendersANullItemAsAnEmptyLine() =>
        Render("a.b.0=x\na.b.1=\na.b.2=y\n", "a.b.type=multiline\n").ShouldBe("b=x\\n\\ny\n");

    /// <summary>
    /// Section 16.6: "a mapping with no sequence projection, a node with no scalar or sequence
    /// projection, or a sequence item having only container shape is <c>TYPE001</c>".
    /// </summary>
    [Test]
    public void MultilineOnAMappingWithNoSequenceProjectionIsRejected() =>
        Failure("a.b.p=x\na.b.q=y\n", "a.b.type=multiline\n").ShouldBe("TYPE001");

    /// <summary>Section 16.6: a sequence item having only container shape is TYPE001.</summary>
    [Test]
    public void MultilineOnASequenceOfContainersIsRejected() =>
        Failure("a.b.0.deep=x\na.b.1.deep=y\n", "a.b.type=multiline\n").ShouldBe("TYPE001");

    // ---- 15.1 step 16: the order is observable ---------------------------------------------------

    /// <summary>
    /// Section 15.1 step 16 fixes the order as "<c>type=array</c> or <c>type=mapping</c>", then
    /// "<c>multiline</c>". The two directives written in one set therefore succeed against a
    /// mapping, because <c>array</c> has produced the sequence <c>multiline</c> requires; the same
    /// mapping with <c>multiline</c> alone has no sequence projection and is <c>TYPE001</c>.
    /// </summary>
    /// <remarks>
    /// This is the pair that makes the order observable rather than merely stated. Reversing the
    /// two passes turns the first assertion into the second.
    /// </remarks>
    [Test]
    public void ArrayRunsBeforeMultilineWithinOneTypeSet()
    {
        Render("a.b.p=x\na.b.q=y\n", "a.b.type=array,multiline\n").ShouldBe("b=x\\ny\n");

        Failure("a.b.p=x\na.b.q=y\n", "a.b.type=multiline\n").ShouldBe("TYPE001");
    }

    /// <summary>
    /// Section 16.6: "the later complete <c>type</c> directive replaces the earlier complete type
    /// set; values from separate matching directives do not accumulate." An accumulating model
    /// would keep the <c>multiline</c> and join the converted sequence.
    /// </summary>
    [Test]
    public void ALaterTypeSetReplacesTheEarlierOneRatherThanMergingWithIt() =>
        Render("a.b.p=x\na.b.q=y\n", "a.b.type=array,multiline\na.b.type=array\n")
            .ShouldBe("b.0=x\nb.1=y\n");

    // ---- 16.6 ignore ---------------------------------------------------------------------------

    /// <summary>
    /// Section 16.6: <c>ignore</c> "removes the complete matched overlay subtree, including
    /// payload, all container projections, descendants, and comments, from the selected output
    /// instance only".
    /// </summary>
    [Test]
    public void IgnoreRemovesTheWholeSubtree() =>
        Render("a.b=1\na.b.deep=2\na.c=3\n", "a.b.type=ignore\n").ShouldBe("c=3\n");

    /// <summary>
    /// Section 16.6: "applying <c>type=ignore</c> to the concrete selector root is a blocking type
    /// error because it would leave an output instance without a root value."
    /// </summary>
    [Test]
    public void IgnoreOnTheSelectorRootIsRejected() =>
        Failure("a.b=1\n", "a.type=ignore\n").ShouldBe("TYPE001");

    /// <summary>
    /// Section 16.6: "an effective ignore at path <c>P</c> removes every descendant regardless of
    /// directives matching those descendants."
    /// </summary>
    [Test]
    public void ADirectiveUnderAnIgnoredPathDoesNothing() =>
        Render("a.b.p=1\na.c=3\n", "a.b.type=ignore\na.b.p.type=array\n").ShouldBe("c=3\n");

    // ---- 15.3 legacy values --------------------------------------------------------------------

    /// <summary>
    /// Section 15.3 accepts the legacy type values "treated as no-ops", so a directive naming only
    /// those changes nothing and does not clear the directive before it.
    /// </summary>
    [Test]
    public void ALegacyOnlyTypeDirectiveDoesNotClearAnEarlierOne() =>
        Render("a.b.p=x\na.b.q=y\n", "a.b.type=array\na.b.type=xmlns\n")
            .ShouldBe("b.0=x\nb.1=y\n");

    // ---- 15.2 unbound directives ---------------------------------------------------------------

    /// <summary>
    /// Section 15.2: an "output-view transformation that binds to no concrete output instance emits
    /// one scheme warning and is otherwise inert."
    /// </summary>
    [Test]
    public void ADirectiveMatchingNoPathWarnsOnce()
    {
        var (result, sink) = Run("a.b=1\n", "a.z.type=array\n");

        result.ExitCode.ShouldBe(0);
        sink.Written["a.properties"].ShouldBe("b=1\n");
        result.Diagnostics.Select(d => d.Code).ShouldBe(["WARN009"]);
    }

    /// <summary>
    /// Section 16.6: "A directive matching only a descendant of an ignored path is inert and emits
    /// the unbound-directive warning from Section 15.2." The <c>ignore</c> itself has bound.
    /// </summary>
    [Test]
    public void ADirectiveUnderAnIgnoredPathWarns()
    {
        var (result, _) = Run("a.b.p=1\na.c=3\n", "a.b.type=ignore\na.b.p.type=array\n");

        result.Diagnostics.Select(d => d.Code).ShouldBe(["WARN009"]);
    }

    /// <summary>
    /// Section 15.2 evaluates <c>type</c> and <c>key</c> "in every output instance containing the
    /// path", so binding in one instance is binding, and Section 22 counts the warning once per
    /// declaration rather than once per instance that lacks the path.
    /// </summary>
    [Test]
    public void ADirectiveBindingInOneInstanceDoesNotWarn()
    {
        var (result, _) = Run(
            "a.b.p.x=1\na.c.q=2\n",
            "a.b.output=namespace\na.c.output=namespace\na.b.p.type=array\n");

        result.ExitCode.ShouldBe(0);
        result.Diagnostics.ShouldBeEmpty();
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static string Render(string data, string scheme)
    {
        var (result, sink) = Run(data, scheme);

        result.ExitCode.ShouldBe(
            0,
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code} {d.Message}")));

        return sink.Written["a.properties"];
    }

    private static string Failure(string data, string scheme)
    {
        var (result, sink) = Run(data, scheme);

        sink.Written.ShouldBeEmpty();

        return result.Diagnostics.Select(d => d.Code).ToImmutableArray().ShouldHaveSingleItem();
    }

    private static (TransformationResult Result, TransformationTests.Sink Sink) Run(
        string data,
        string scheme)
    {
        var sink = new TransformationTests.Sink();
        var sources = new TransformationTests.Sources(
            ("in.txt", data), ("scheme.txt", "a.output=namespace\n" + scheme));

        return (
            TransformationTests.Run(sink, sources, "-i", "in.txt", "-s", "scheme.txt"),
            sink);
    }
}
