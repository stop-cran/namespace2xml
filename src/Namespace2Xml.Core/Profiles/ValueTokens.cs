using System.Collections.Immutable;

namespace Namespace2Xml.Profiles;

/// <summary>
/// One element of an interpreted value: literal text, a wildcard substitution, or a reference.
/// </summary>
/// <remarks>
/// A value is a sequence rather than a string for the reason Appendix A.3 gives: escapes, wildcard
/// tokens, and references are recognized in one left-to-right pass and emitted text is never
/// rescanned, so the difference between the <c>${</c> that begins a reference and the <c>${</c> that
/// <c>\${</c> emits has to survive lexing.
/// </remarks>
public abstract record ValueToken
{
    private protected ValueToken()
    {
    }
}

/// <summary>Literal text, already unescaped.</summary>
/// <param name="Text">The scalars this token contributes. Never empty.</param>
public sealed record LiteralValueToken(string Text) : ValueToken;

/// <summary>
/// A Section 12.1 legacy capture substitution, or a Section 12.2 explicit one.
/// </summary>
/// <param name="CaptureId">
/// The explicit capture identifier, or <see langword="null"/> for the legacy bare form.
/// </param>
public sealed record ValueWildcardToken(string? CaptureId) : ValueToken;

/// <summary>A Section 8.4 reference.</summary>
/// <param name="Name">The referenced name.</param>
public sealed record ReferenceToken(QualifiedName Name) : ValueToken;

/// <summary>
/// An interpreted value: the token sequence Appendix A.3 produces.
/// </summary>
public sealed record InterpretedValue
{
    /// <summary>Creates a value from its tokens.</summary>
    /// <param name="tokens">The tokens, in source order. May be empty: a value may be empty.</param>
    /// <remarks>
    /// Adjacent literals are merged and empty ones dropped, for the reason the name tokens are: the
    /// list exists only to keep wildcards and references distinct from text, so equality must not
    /// depend on how the value was built.
    /// </remarks>
    public InterpretedValue(ImmutableArray<ValueToken> tokens) => Tokens = Canonical(tokens);

    /// <summary>The tokens, in source order.</summary>
    public ImmutableArray<ValueToken> Tokens { get; }

    /// <summary>
    /// The value's text when it contains no wildcard and no reference, otherwise
    /// <see langword="null"/>. An empty value has empty text.
    /// </summary>
    public string? LiteralText => Tokens switch
    {
        [] => string.Empty,
        [LiteralValueToken literal] => literal.Text,
        _ => null,
    };

    /// <summary>Whether the value contains a reference.</summary>
    public bool ContainsReference => Tokens.OfType<ReferenceToken>().Any();

    /// <summary>Whether the value contains a wildcard substitution.</summary>
    public bool ContainsWildcard => Tokens.OfType<ValueWildcardToken>().Any();

    /// <inheritdoc/>
    public bool Equals(InterpretedValue? other) =>
        other is not null && Tokens.SequenceEqual(other.Tokens);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var token in Tokens)
        {
            hash.Add(token);
        }

        return hash.ToHashCode();
    }

    private static ImmutableArray<ValueToken> Canonical(ImmutableArray<ValueToken> tokens)
    {
        if (tokens.IsDefaultOrEmpty)
        {
            return [];
        }

        var needsWork = false;

        for (var i = 0; i < tokens.Length && !needsWork; i++)
        {
            needsWork = tokens[i] is LiteralValueToken { Text.Length: 0 }
                || (i > 0 && tokens[i] is LiteralValueToken && tokens[i - 1] is LiteralValueToken);
        }

        if (!needsWork)
        {
            return tokens;
        }

        var result = ImmutableArray.CreateBuilder<ValueToken>(tokens.Length);

        foreach (var token in tokens)
        {
            if (token is not LiteralValueToken literal)
            {
                result.Add(token);
                continue;
            }

            if (literal.Text.Length == 0)
            {
                continue;
            }

            if (result.Count > 0 && result[^1] is LiteralValueToken previous)
            {
                result[^1] = new LiteralValueToken(previous.Text + literal.Text);
            }
            else
            {
                result.Add(literal);
            }
        }

        return result.ToImmutable();
    }
}
