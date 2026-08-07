using System.Globalization;
using System.Text;

namespace Namespace2Xml.Diagnostics;

/// <summary>
/// Renders one diagnostic in the human-readable <c>text</c> encoding of specification
/// Section 6.4.2. Prose is localizable and is not part of byte-identical determinism, so this
/// layout may change; the structured fields it shows may not.
/// </summary>
public static class TextDiagnosticWriter
{
    /// <summary>Renders a single line, without a trailing terminator.</summary>
    public static string Render(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        var builder = new StringBuilder();

        if (diagnostic.Source is not null)
        {
            builder.Append(diagnostic.Source);

            if (diagnostic.Line is not null)
            {
                builder.Append('(').Append(diagnostic.Line.Value.ToString(CultureInfo.InvariantCulture));

                if (diagnostic.Column is not null)
                {
                    builder.Append(',').Append(diagnostic.Column.Value.ToString(CultureInfo.InvariantCulture));
                }

                builder.Append(')');
            }

            builder.Append(": ");
        }

        builder.Append(diagnostic.Severity == DiagnosticSeverity.Error ? "error " : "warning ");
        builder.Append(diagnostic.Code).Append(' ').Append(diagnostic.Spec).Append(": ");
        builder.Append(diagnostic.Message);

        Append(builder, "path", diagnostic.Path);
        Append(builder, "declaration", diagnostic.Declaration);
        Append(builder, "rule", diagnostic.Rule.IsDefaultOrEmpty ? null : string.Join(", ", diagnostic.Rule));
        Append(builder, "destination", diagnostic.Destination);

        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string name, string? value)
    {
        if (value is not null)
        {
            builder.Append(" [").Append(name).Append(": ").Append(value).Append(']');
        }
    }
}
