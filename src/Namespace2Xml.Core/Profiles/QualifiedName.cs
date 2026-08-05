using System.Collections.Immutable;

namespace Namespace2Xml.Profiles;

/// <summary>
/// An Appendix A.2 <c>qname</c>: one or more components separated by unescaped delimiters.
/// </summary>
/// <remarks>
/// Equality is structural over the components, because a qualified name is the identity of a node
/// throughout the overlay and the output plan.
/// </remarks>
public sealed record QualifiedName
{
    /// <summary>Creates a qualified name from its components.</summary>
    /// <param name="parts">The components, in source order. Never empty.</param>
    public QualifiedName(ImmutableArray<NamePart> parts)
    {
        if (parts.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "Appendix A.2 spells qname as one or more components, so an empty name is not one.",
                nameof(parts));
        }

        Parts = parts;
    }

    /// <summary>The components, in source order.</summary>
    public ImmutableArray<NamePart> Parts { get; }

    /// <inheritdoc/>
    public bool Equals(QualifiedName? other) => other is not null && Parts.SequenceEqual(other.Parts);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var part in Parts)
        {
            hash.Add(part);
        }

        return hash.ToHashCode();
    }
}
