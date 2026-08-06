using System.Diagnostics.CodeAnalysis;

namespace Namespace2Xml.Overlay;

/// <summary>The Section 4.3 scalar kinds a payload may carry.</summary>
/// <remarks>
/// The members are spelled exactly as Section 4.3 lists the kinds, so that the enum can be checked
/// against the contract by reading it. That collides with CA1720, which objects to member names
/// containing a CLR type name; the objection does not apply here, because a member of an enum named
/// <see cref="ScalarKind"/> is never mistakable for <see cref="string"/> or <see cref="decimal"/>,
/// and renaming the members would put a translation step between the contract and the code at the
/// one place a reviewer most needs there not to be one.
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "The member names are the normative vocabulary of specification Section 4.3.")]
public enum ScalarKind
{
    /// <summary>
    /// A namespace-profile payload that Section 18 inference has not yet classified. Section 4.3
    /// makes this the initial kind of every namespace scalar, distinct from a settled string.
    /// </summary>
    UntypedString,

    /// <summary>A settled string, either inferred by Section 18 or typed by its source format.</summary>
    String,

    /// <summary>A Boolean.</summary>
    Boolean,

    /// <summary>An arbitrary-precision integer.</summary>
    Integer,

    /// <summary>An arbitrary-precision decimal.</summary>
    Decimal,

    /// <summary>The null payload, which Section 4.2 distinguishes from having no payload at all.</summary>
    Null,
}
