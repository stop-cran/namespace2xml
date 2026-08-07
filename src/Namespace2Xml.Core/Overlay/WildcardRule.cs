using System.Collections.Immutable;
using System.Text;
using Namespace2Xml.Profiles;

namespace Namespace2Xml.Overlay;

/// <summary>
/// One Section 12 wildcard rule: a generative template, or a Section 8.6 mask whose pattern
/// contains a wildcard.
/// </summary>
/// <param name="Name">The rule's pattern name, which contains at least one wildcard token.</param>
/// <param name="Value">
/// The template's value, or <see langword="null"/> for a mask. Section 12.4 counts masks as a
/// wildcard rule category and charges them against the shared candidate-check limit, but they
/// generate nothing, so the value is what separates the two.
/// </param>
/// <param name="Order">The rule's Section 4.7 key, which fixes its precedence and its worklist place.</param>
/// <param name="Comments">
/// The comments bound to the rule. Section 4.5 clones them onto every contribution it generates.
/// </param>
/// <param name="Source">The Section 6.4.3 <c>source</c> member, or null for a variable.</param>
/// <param name="Identity">The source's cardinality identity.</param>
/// <param name="Line">The one-based line the rule was written on.</param>
public sealed record WildcardRule(
    QualifiedName Name,
    InterpretedValue? Value,
    StableOrderingKey Order,
    ImmutableArray<BoundComment> Comments,
    string? Source,
    string Identity,
    int Line)
{
    /// <summary>Whether the rule generates contributions, as opposed to only suppressing them.</summary>
    public bool IsGenerative => Value is not null;

    /// <summary>The Section 22 key this rule's diagnostics are emitted once per.</summary>
    public string RuleKey => $"{Identity}:{Line}";

    /// <summary>
    /// The Appendix A canonical name Section 22's <c>rule</c> member carries for this rule. It is
    /// the name alone: a diagnostic that also carries <c>source</c>, <c>line</c> and <c>path</c>
    /// locates the rule through those members and omits <c>rule</c> rather than restating them.
    /// </summary>
    public string CanonicalName => CanonicalPath.Of(Name) ?? string.Empty;
}

/// <summary>
/// Section 12.1 and 12.2 value substitution: the text a matched rule's value contributes.
/// </summary>
public static class WildcardSubstitution
{
    /// <summary>Substitutes a match's captures into a rule's value.</summary>
    /// <param name="value">The rule's interpreted value.</param>
    /// <param name="captures">The captures the match bound.</param>
    /// <returns>The generated payload text.</returns>
    /// <exception cref="InvalidOperationException">
    /// The value carries a reference, which Section 15.1 does not resolve until step 15.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Section 12.1 gives the legacy form two compatibility rules that only look asymmetric: "if a
    /// legacy value contains more wildcard substitutions than the name produced, the last capture is
    /// repeated", and "if it contains fewer, unused captures are ignored". Repeating the last one is
    /// what the clamp below does; ignoring the extras needs no code at all.
    /// </para>
    /// <para>
    /// An explicit capture is looked up by identifier and is guaranteed present, because Section
    /// 12.2 makes an undefined capture an error and <see cref="WildcardEvaluator"/> rejects the rule
    /// before any match reaches here.
    /// </para>
    /// </remarks>
    public static string Apply(InterpretedValue value, WildcardCaptures captures)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(captures);

        if (value.LiteralText is { } literal)
        {
            return literal;
        }

        var text = new StringBuilder();
        var next = 0;

        foreach (var token in value.Tokens)
        {
            switch (token)
            {
                case LiteralValueToken plain:
                    text.Append(plain.Text);
                    break;

                case ValueWildcardToken { CaptureId: null }:
                    if (!captures.Positional.IsEmpty)
                    {
                        text.Append(
                            captures.Positional[Math.Min(next, captures.Positional.Length - 1)]);
                    }

                    next++;
                    break;

                case ValueWildcardToken { CaptureId: { } id }:
                    text.Append(captures.Named[id]);
                    break;

                default:
                    throw new InvalidOperationException(
                        "Section 15.1 resolves references at step 15, so a template value carrying "
                        + "one is refused before evaluation reaches substitution.");
            }
        }

        return text.ToString();
    }
}
