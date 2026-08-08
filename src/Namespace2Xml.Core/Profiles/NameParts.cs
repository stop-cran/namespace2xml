using System.Collections.Immutable;
using System.Text;

namespace Namespace2Xml.Profiles;

/// <summary>
/// One element of an Appendix A.2 <c>ordinary-component</c> or <c>local-component</c>: either
/// literal text or a wildcard token.
/// </summary>
/// <remarks>
/// A component is a sequence rather than a string because a wildcard is not a character. Storing
/// <c>a*b</c> as text would make the literal name <c>a\*b</c> and the pattern <c>a*b</c> the same
/// value, and Section 21 requires name encoding to be injective.
/// </remarks>
public abstract record NameToken
{
    private protected NameToken()
    {
    }
}

/// <summary>Literal text, already unescaped.</summary>
/// <param name="Text">The scalars this token contributes. Never empty.</param>
public sealed record LiteralToken(string Text) : NameToken;

/// <summary>
/// An Appendix A.2 <c>wildcard-token</c>: <c>*</c>, or <c>*[identifier]</c> for an explicit capture.
/// </summary>
/// <param name="CaptureId">
/// The explicit capture identifier, or <see langword="null"/> for the legacy bare form.
/// </param>
public sealed record WildcardToken(string? CaptureId) : NameToken;

/// <summary>
/// One Appendix A.2 <c>component</c> of a qualified name.
/// </summary>
public abstract record NamePart
{
    private protected NamePart()
    {
    }
}

/// <summary>
/// An Appendix A.2 <c>xml-name-component</c>: the part an attribute marker may qualify, and the
/// two forms an element name can take.
/// </summary>
public abstract record XmlNameComponent : NamePart
{
    private protected XmlNameComponent()
    {
    }
}

/// <summary>An Appendix A.2 <c>ordinary-component</c>.</summary>
public sealed record OrdinaryPart : XmlNameComponent
{
    /// <summary>Creates an ordinary component from its tokens.</summary>
    public OrdinaryPart(ImmutableArray<NameToken> tokens) => Tokens = TokenSequence.Canonical(tokens);

    /// <summary>The component's tokens, in source order. Never empty.</summary>
    public ImmutableArray<NameToken> Tokens { get; }

    /// <summary>
    /// Whether this component was written with the explicit <c>Q{}</c> marker.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Section 11.4: "A <c>Q{}local</c> component and an unmarked <c>local</c> component are the
    /// same component and address the same overlay node … The marker does not narrow the component
    /// — it narrows the <i>addressing</i>." Both halves of that sentence are load-bearing, and they
    /// pull in opposite directions: the component must compare equal to its unmarked spelling so the
    /// two reach one node, and it must still be distinguishable so Sections 13.1 and 15.2 can see
    /// that this path was addressed canonically and skip the simple alias index.
    /// </para>
    /// <para>
    /// This property is therefore excluded from <see cref="Equals(OrdinaryPart?)"/> and
    /// <see cref="GetHashCode"/> deliberately. It is an annotation on how a path was <i>written</i>,
    /// not part of the component's identity, and a component that changed identity when marked
    /// would split <c>a.Q{}x=1</c> and <c>a.x=2</c> into two overlay nodes — which is exactly what
    /// the first half of that sentence forbids.
    /// </para>
    /// </remarks>
    public bool IsExplicitlyCanonical { get; init; }

    /// <summary>The literal text when the component contains no wildcard, otherwise null.</summary>
    public string? LiteralText =>
        Tokens.Length == 1 && Tokens[0] is LiteralToken literal ? literal.Text : null;

    /// <inheritdoc/>
    public bool Equals(OrdinaryPart? other) => TokenSequence.Equal(Tokens, other?.Tokens);

    /// <inheritdoc/>
    public override int GetHashCode() => TokenSequence.HashCode(Tokens);

    /// <summary>
    /// Prints the tokens only. <see cref="IsExplicitlyCanonical"/> is excluded for the same reason
    /// it is excluded from equality, and because this text reaches diagnostics.
    /// </summary>
    /// <param name="builder">The builder the record's <c>ToString</c> is assembling.</param>
    /// <returns>True, so the base implementation emits the printed members.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Append("Tokens = ").Append(string.Join(", ", Tokens));
        return true;
    }
}

/// <summary>An Appendix A.2 <c>qualified-element</c>: <c>Q{uri}local</c>.</summary>
/// <remarks>
/// The URI is never empty. Section 11.4: "A <c>Q{}local</c> component and an unmarked
/// <c>local</c> component are the same component and address the same overlay node." An empty-URI
/// instance would be a second representation of an <see cref="OrdinaryPart"/> that compares
/// unequal to it and encodes back to text this lexer no longer produces, so it is refused here
/// rather than left for a caller to remember.
/// </remarks>
public sealed record QualifiedElementPart : XmlNameComponent
{
    /// <summary>Creates a qualified element from its URI and local tokens.</summary>
    /// <exception cref="ArgumentException"><paramref name="uri"/> is empty.</exception>
    public QualifiedElementPart(string uri, ImmutableArray<NameToken> local)
    {
        if (string.IsNullOrEmpty(uri))
        {
            throw new ArgumentException(
                "an unqualified element is an ordinary component; see Section 11.4.",
                nameof(uri));
        }

        Uri = uri;
        Local = TokenSequence.Canonical(local);
    }

    /// <summary>The URI body, already unescaped. Never empty.</summary>
    public string Uri { get; }

    /// <summary>The local name's tokens, in source order. Never empty.</summary>
    public ImmutableArray<NameToken> Local { get; }

    /// <inheritdoc/>
    public bool Equals(QualifiedElementPart? other) =>
        other is not null
        && string.Equals(Uri, other.Uri, StringComparison.Ordinal)
        && TokenSequence.Equal(Local, other.Local);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Uri, TokenSequence.HashCode(Local));
}

/// <summary>An Appendix A.2 <c>typed-attribute</c>: <c>@name</c> or <c>@Q{uri}name</c>.</summary>
/// <param name="Name">The attribute's name component.</param>
public sealed record AttributePart(XmlNameComponent Name) : NamePart;

/// <summary>
/// An Appendix A.2 <c>typed-content</c>: <c>#n</c>, the Section 11.4 content-token ordering value.
/// </summary>
/// <param name="Ordinal">
/// The ordering value. Never negative, and never written with a leading zero. Section 11.4 assigns
/// content-token addresses "using Section 5.4", which makes them stable signed 64-bit ordering
/// values through 9,223,372,036,854,775,807; a 32-bit ordinal would reject a valid address a
/// sequence high-water allocator can legitimately reach.
/// </param>
public sealed record ContentPart(long Ordinal) : NamePart;

/// <summary>Structural comparison and canonical form for token sequences.</summary>
/// <remarks>
/// <see cref="ImmutableArray{T}"/> compares by reference, so a record holding one compares two
/// identically-populated instances unequal. Qualified names are dictionary keys throughout the
/// overlay, where that would silently produce a second entry for a path that already exists.
/// </remarks>
internal static class TokenSequence
{
    /// <summary>
    /// Merges adjacent literals and drops empty ones.
    /// </summary>
    /// <remarks>
    /// The token list exists only to keep wildcards distinct from text, so <c>["a", "b"]</c> and
    /// <c>["ab"]</c> denote the same component. Leaving both constructible would make equality
    /// depend on how a name was built rather than on what it names, and give the overlay two keys
    /// for one path. The lexer already emits this form; normalizing here extends the guarantee to
    /// names built in code.
    /// </remarks>
    internal static ImmutableArray<NameToken> Canonical(ImmutableArray<NameToken> tokens)
    {
        if (tokens.IsDefaultOrEmpty)
        {
            return [];
        }

        var needsWork = false;

        for (var i = 0; i < tokens.Length && !needsWork; i++)
        {
            needsWork = tokens[i] is LiteralToken { Text.Length: 0 }
                || (i > 0 && tokens[i] is LiteralToken && tokens[i - 1] is LiteralToken);
        }

        if (!needsWork)
        {
            return tokens;
        }

        var result = ImmutableArray.CreateBuilder<NameToken>(tokens.Length);

        foreach (var token in tokens)
        {
            if (token is not LiteralToken literal)
            {
                result.Add(token);
                continue;
            }

            if (literal.Text.Length == 0)
            {
                continue;
            }

            if (result.Count > 0 && result[^1] is LiteralToken previous)
            {
                result[^1] = new LiteralToken(previous.Text + literal.Text);
            }
            else
            {
                result.Add(literal);
            }
        }

        return result.ToImmutable();
    }

    internal static bool Equal(ImmutableArray<NameToken> left, ImmutableArray<NameToken>? right) =>
        right is { } other && left.SequenceEqual(other);

    internal static int HashCode(ImmutableArray<NameToken> tokens)
    {
        var hash = default(HashCode);
        foreach (var token in tokens)
        {
            hash.Add(token);
        }

        return hash.ToHashCode();
    }
}
