using System.Globalization;
using System.Text;

namespace Namespace2Xml.Diagnostics;

/// <summary>
/// Serializes the diagnostic stream in the canonical <c>json</c> encoding of specification
/// Section 6.4.3. The byte layout is fixed so that the stream satisfies the Section 24
/// byte-identity requirement, so this type never emits insignificant whitespace, never
/// reorders members, and never escapes a character the specification says to emit literally.
/// </summary>
public static class JsonDiagnosticWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Renders the whole stream, including its framing and its trailing LF.</summary>
    public static byte[] Render(IReadOnlyList<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var builder = new StringBuilder();

        if (diagnostics.Count == 0)
        {
            builder.Append("[]\n");
        }
        else
        {
            builder.Append("[\n");
            for (var i = 0; i < diagnostics.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(",\n");
                }

                AppendObject(builder, diagnostics[i]);
            }

            builder.Append("\n]\n");
        }

        return Utf8NoBom.GetBytes(builder.ToString());
    }

    private static void AppendObject(StringBuilder builder, Diagnostic diagnostic)
    {
        builder.Append('{');

        // Member order is normative and must not be sorted or reordered.
        AppendString(builder, "code", diagnostic.Code, first: true);
        AppendString(builder, "severity", SeverityText(diagnostic.Severity), first: false);
        AppendString(builder, "phase", PhaseText(diagnostic.Phase), first: false);
        AppendOptionalString(builder, "source", diagnostic.Source);
        AppendOptionalInt(builder, "line", diagnostic.Line);
        AppendOptionalInt(builder, "column", diagnostic.Column);
        AppendOptionalString(builder, "path", diagnostic.Path);
        AppendOptionalString(builder, "declaration", diagnostic.Declaration);
        AppendOptionalString(builder, "rule", diagnostic.Rule);
        AppendOptionalString(builder, "destination", diagnostic.Destination);
        AppendString(builder, "spec", diagnostic.Spec, first: false);
        AppendString(builder, "message", diagnostic.Message, first: false);

        builder.Append('}');
    }

    private static string SeverityText(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => "error",
        DiagnosticSeverity.Warning => "warning",
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null),
    };

    private static string PhaseText(DiagnosticPhase phase) => phase switch
    {
        DiagnosticPhase.Cli => "cli",
        DiagnosticPhase.Scheme => "scheme",
        DiagnosticPhase.Input => "input",
        DiagnosticPhase.Planning => "planning",
        DiagnosticPhase.Publication => "publication",
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, null),
    };

    private static void AppendOptionalString(StringBuilder builder, string name, string? value)
    {
        if (value is not null)
        {
            AppendString(builder, name, value, first: false);
        }
    }

    private static void AppendOptionalInt(StringBuilder builder, string name, int? value)
    {
        if (value is not null)
        {
            builder.Append(',').Append('"').Append(name).Append("\":")
                   .Append(value.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AppendString(StringBuilder builder, string name, string value, bool first)
    {
        if (!first)
        {
            builder.Append(',');
        }

        builder.Append('"').Append(name).Append("\":");
        AppendEscaped(builder, value);
    }

    private static void AppendEscaped(StringBuilder builder, string value)
    {
        builder.Append('"');

        foreach (var c in value)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        builder.Append('"');
    }
}
