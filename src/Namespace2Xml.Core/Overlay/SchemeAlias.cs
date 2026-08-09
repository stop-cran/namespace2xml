using System.Collections.Immutable;
using Namespace2Xml.Profiles;

namespace Namespace2Xml.Overlay;

/// <summary>
/// The Section 15.2 simple alias, as a scheme path sees it.
/// </summary>
/// <remarks>
/// <para>
/// Section 15.2: "Scheme paths use the same typed component model as canonical data paths. An
/// explicitly marked <c>Q{}</c>, <c>@</c>, or <c>#n</c> component selects only that XML component.
/// An unmarked component uses the simple alias index for compatibility and convenience."
/// </para>
/// <para>
/// The grant is stated <em>per component</em>, unlike Section 13.1, where "a reference containing an
/// unescaped <c>Q{...}</c>, <c>@</c>, or <c>#n</c> ... is canonical" and one marker makes the whole
/// reference canonical. A scheme path <c>a.x.@y</c> therefore aliases at <c>x</c> and binds
/// canonically at <c>@y</c>, and folding whole paths would differ exactly there.
/// </para>
/// <para>
/// Only the two length-preserving rewrites are applied: <c>@local</c> and <c>Q{uri}local</c> both
/// alias to <c>local</c>. Section 13.1's <c>#n</c> elisions change a path's length, and Section 11.4
/// holds an element and its sole content token to be "two canonical addresses for one scalar
/// identity, not two candidates in the simple-alias ambiguity index" — applying that here would make
/// a directive on a mixed-content element ambiguous with its own text. See KNOWN-LIMITS.
/// </para>
/// </remarks>
internal static class SchemeAlias
{
    /// <summary>The ordinary component an XML component aliases to, or null when it has none.</summary>
    /// <param name="part">The concrete component.</param>
    /// <returns>The alias, or null for an ordinary or content component.</returns>
    internal static OrdinaryPart? Of(NamePart part) => part switch
    {
        AttributePart attribute => Unqualified(attribute.Name),
        QualifiedElementPart qualified => new OrdinaryPart(qualified.Local),
        _ => null,
    };

    /// <summary>
    /// Rewrites a concrete path into what the pattern's unmarked components address.
    /// </summary>
    /// <param name="pattern">The scheme path's components.</param>
    /// <param name="count">How many leading components take part in the match.</param>
    /// <param name="concrete">The concrete path being matched.</param>
    /// <returns>
    /// The concrete path with an XML component folded to its alias at every position the pattern
    /// left unmarked, and unchanged everywhere else.
    /// </returns>
    /// <remarks>
    /// A wildcard component is unmarked, so <c>a.*</c> reaches an attribute written <c>a.@x</c>.
    /// That is the compatibility affordance Section 15.2 is granting: a 2.x scheme had no typed
    /// model to mark, so every component it wrote was unmarked.
    /// </remarks>
    internal static ImmutableArray<NamePart> Align(
        ImmutableArray<NamePart> pattern,
        int count,
        ImmutableArray<NamePart> concrete)
    {
        ImmutableArray<NamePart>.Builder? aligned = null;

        for (var i = 0; i < count && i < concrete.Length; i++)
        {
            if (pattern[i] is not OrdinaryPart { IsExplicitlyCanonical: false }
                || Of(concrete[i]) is not { } alias)
            {
                continue;
            }

            aligned ??= concrete.ToBuilder();
            aligned[i] = alias;
        }

        return aligned is null ? concrete : aligned.ToImmutable();
    }

    private static OrdinaryPart Unqualified(XmlNameComponent component) => component switch
    {
        QualifiedElementPart qualified => new OrdinaryPart(qualified.Local),
        OrdinaryPart ordinary => new OrdinaryPart(ordinary.Tokens),
        _ => throw new ArgumentOutOfRangeException(nameof(component)),
    };
}
