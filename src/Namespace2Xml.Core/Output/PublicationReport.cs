using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Namespace2Xml.Contract;
using Namespace2Xml.Diagnostics;

namespace Namespace2Xml.Output;

internal enum PublicationOutcome
{
    Success,
    ValidationFailed,
    WarningPolicyRefused,
    PublicationFailed,
}

internal enum PublicationStatus
{
    Published,
    Unchanged,
    Failed,
    Unattempted,
}

internal sealed record PublicationObservation(
    PlannedOutput Output,
    bool? ExistedBefore,
    bool? BytesDiffered,
    PublicationStatus Status,
    string? FailureCode,
    string? UnattemptedReason);

internal sealed record PublicationReport(
    PublicationOutcome Outcome,
    bool PlanComplete,
    bool PublicationOccurred,
    bool SideEffectsPossible,
    ImmutableArray<PublicationObservation> Outputs)
{
    internal byte[] ToCanonicalBytes()
    {
        var bundle = ContractBundle.Current;
        var digestInput = Encoding.UTF8.GetBytes($"{bundle.SpecificationSha256}\n{bundle.RegistrySha256}");
        var digest = Convert.ToHexStringLower(SHA256.HashData(digestInput));
        var text = new StringBuilder("{\"schemaVersion\":1,\"productVersion\":");
        String(text, ContractBundle.ProductVersion);
        text.Append(",\"contract\":{\"revision\":");
        String(text, bundle.Revision);
        text.Append(",\"hash\":");
        String(text, digest);
        text.Append(",\"specificationSha256\":");
        String(text, bundle.SpecificationSha256);
        text.Append(",\"registrySha256\":");
        String(text, bundle.RegistrySha256);
        text.Append("},\"exitCode\":").Append(Outcome == PublicationOutcome.Success ? '0' : '1');
        text.Append(",\"outcome\":");
        String(text, CamelCase(Outcome.ToString()));
        text.Append(",\"planComplete\":");
        Boolean(text, PlanComplete);
        text.Append(",\"publicationOccurred\":");
        Boolean(text, PublicationOccurred);
        text.Append(",\"sideEffectsPossible\":");
        Boolean(text, SideEffectsPossible);
        text.Append(",\"outputs\":[");

        for (var index = 0; index < Outputs.Length; index++)
        {
            if (index != 0)
            {
                text.Append(',');
            }

            var observation = Outputs[index];
            var output = observation.Output;
            text.Append("{\"destination\":");
            String(text, output.Path.Canonical);
            text.Append(",\"format\":");
            String(text, output.History[^1].Format.ToString().ToLowerInvariant());
            text.Append(",\"contributions\":[");
            for (var contributionIndex = 0; contributionIndex < output.History.Length; contributionIndex++)
            {
                if (contributionIndex != 0)
                {
                    text.Append(',');
                }

                var contribution = output.History[contributionIndex];
                text.Append("{\"source\":");
                String(text, contribution.Source);
                text.Append(",\"sourceIndex\":").Append(contribution.SourceIndex.ToString(CultureInfo.InvariantCulture));
                text.Append(",\"line\":").Append(contribution.Line.ToString(CultureInfo.InvariantCulture));
                text.Append(",\"declaration\":");
                String(text, contribution.Declaration);
                text.Append(",\"formatIndex\":").Append(contribution.FormatIndex.ToString(CultureInfo.InvariantCulture));
                text.Append(",\"matchIndex\":").Append(contribution.MatchIndex.ToString(CultureInfo.InvariantCulture));
                text.Append(",\"selector\":");
                String(text, contribution.Selector);
                text.Append(",\"root\":");
                String(text, contribution.Root);
                text.Append(",\"format\":");
                String(text, contribution.Format.ToString().ToLowerInvariant());
                text.Append('}');
            }

            text.Append("],\"byteLength\":").Append(output.Buffer.Length.ToString(CultureInfo.InvariantCulture));
            text.Append(",\"sha256\":");
            String(text, output.Buffer.Sha256());
            text.Append(",\"existedBefore\":");
            Boolean(text, observation.ExistedBefore);
            text.Append(",\"bytesDiffered\":");
            Boolean(text, observation.BytesDiffered);
            text.Append(",\"status\":");
            String(text, CamelCase(observation.Status.ToString()));
            text.Append(",\"failureCode\":");
            NullableString(text, observation.FailureCode);
            text.Append(",\"unattemptedReason\":");
            NullableString(text, observation.UnattemptedReason);
            text.Append('}');
        }

        text.Append("]}\n");
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    private static string CamelCase(string value) =>
        char.ToLowerInvariant(value[0]) + value[1..];

    private static void String(StringBuilder text, string value) =>
        JsonDiagnosticWriter.AppendEscaped(text, value);

    private static void NullableString(StringBuilder text, string? value)
    {
        if (value is null)
        {
            text.Append("null");
        }
        else
        {
            String(text, value);
        }
    }

    private static void Boolean(StringBuilder text, bool? value) =>
        text.Append(value is null ? "null" : value.Value ? "true" : "false");
}
