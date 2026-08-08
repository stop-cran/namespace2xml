using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Namespace2Xml.Profiles;

namespace Namespace2Xml.Output;

/// <summary>
/// Spells one name component as the literal JSON or YAML key text of Section 19.3 and Section 19.4.
/// </summary>
/// <remarks>
/// A mapping key is a quoted string in both formats, so it carries text rather than a namespace
/// path and needs none of the Section 19.1 escaping: a component holding a dot is one key whose
/// text contains a dot, and writing it <c>a\.b</c> would name a key nobody addressed. The typed XML
/// components keep their Section 11.4 markers, which is the only spelling that distinguishes an
/// attribute <c>@x</c> from the element <c>x</c> once both are ordinary mapping keys.
/// </remarks>
internal static class StructuredKey
{
    /// <summary>The literal key text of one component.</summary>
    /// <param name="part">The component.</param>
    public static string Of(NamePart part)
    {
        var builder = new StringBuilder();

        Append(part, builder);

        return builder.ToString();
    }

    private static void Append(NamePart part, StringBuilder builder)
    {
        switch (part)
        {
            case ContentPart content:
                builder.Append('#').Append(content.Ordinal.ToString(CultureInfo.InvariantCulture));
                break;

            case AttributePart attribute:
                builder.Append('@');
                Append(attribute.Name, builder);
                break;

            case QualifiedElementPart qualified:
                builder.Append("Q{").Append(qualified.Uri).Append('}');
                Append(qualified.Local, builder);
                break;

            case OrdinaryPart ordinary:
                Append(ordinary.Tokens, builder);
                break;

            default:
                throw new InvalidOperationException(
                    $"'{part.GetType().Name}' is not an Appendix A.2 component; see Section 11.4.");
        }
    }

    private static void Append(ImmutableArray<NameToken> tokens, StringBuilder builder)
    {
        foreach (var token in tokens)
        {
            switch (token)
            {
                case LiteralToken literal:
                    builder.Append(literal.Text);
                    break;

                default:
                    // A wildcard reaching a rendered name is a step 13 defect: Section 15.1 expands
                    // every pattern before an output view exists. Spelling it keeps this total.
                    builder.Append('*');
                    break;
            }
        }
    }
}
