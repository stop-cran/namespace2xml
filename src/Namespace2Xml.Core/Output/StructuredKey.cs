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
/// attribute <c>@x</c> from the element <c>x</c> once both are ordinary mapping keys. Section 9.1
/// reads those markers back, so an ordinary component whose own text begins with one is escaped
/// here — otherwise the key this writes would not name the component it was written from.
/// </remarks>
internal static class StructuredKey
{
    /// <summary>The literal key text of one component.</summary>
    /// <param name="part">The component.</param>
    public static string Of(NamePart part)
    {
        var builder = new StringBuilder();

        Append(part, builder);

        return Escaped(builder.ToString(), part);
    }

    /// <summary>
    /// Escapes a leading marker that Section 9.1 would otherwise read back as a typed component.
    /// </summary>
    /// <param name="key">The spelled key.</param>
    /// <param name="part">The component it was spelled from.</param>
    /// <remarks>
    /// Only an ordinary component needs this: the marked components spell their own marker and are
    /// meant to be read back as themselves. A leading backslash is escaped as well, because
    /// Section 9.1 gives a leading <c>\</c> before one of these characters its escaping meaning,
    /// and a key beginning <c>\@</c> would otherwise read back one character shorter.
    /// </remarks>
    private static string Escaped(string key, NamePart part)
    {
        if (part is not OrdinaryPart)
        {
            return key;
        }

        var marked = key.StartsWith('@')
            || key.StartsWith('#')
            || key.StartsWith('\\')
            || key.StartsWith("Q{", StringComparison.Ordinal);

        return marked ? "\\" + key : key;
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
