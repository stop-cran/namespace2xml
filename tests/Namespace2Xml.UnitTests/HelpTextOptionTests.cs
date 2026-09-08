using System.Globalization;
using System.Text.RegularExpressions;
using Namespace2Xml.Cli;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Specification Section 6.2: the structured option inventory in <c>--help</c> must be the
/// parser's complete accepted catalog, including effective resource-limit defaults.
/// </summary>
[TestFixture]
public sealed partial class HelpTextOptionTests
{
    private static readonly IReadOnlyDictionary<string, HelpOptionRow> Rows = ParseRows();

    [GeneratedRegex(
        @"^  (?:(?<alias>-[a-z0-9]), )?(?<name>--[a-z0-9-]+)(?: (?<value><[^>]+>(?:\.\.\.)?))?(?: {2,}|$)",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex OptionRow();

    /// <summary>
    /// An accepted option omitted from help is undiscoverable; a help-only option is worse because
    /// it invites a guaranteed CLI001. Equality in both directions catches both failures.
    /// </summary>
    [Test]
    public void StructuredHelpRowsExactlyMatchTheAcceptedCatalog()
    {
        Rows.Keys.Order().ShouldBe(CommandLineOptions.All.Select(option => option.Name).Order());

        foreach (var option in CommandLineOptions.All)
        {
            var row = Rows[option.Name];
            row.Alias.ShouldBe(option.Alias);
            row.ValueLabel.ShouldBe(RenderValueLabel(option));
            row.Text.ShouldContain(option.Description.TrimEnd('.'), Case.Sensitive);
        }
    }

    [Test]
    public void CatalogSpellingsAndHelpMetadataAreComplete()
    {
        var spellings = CommandLineOptions.All
            .SelectMany(option => new[] { option.Name, option.Alias })
            .Where(spelling => spelling is not null)
            .ToList();

        spellings.Count.ShouldBe(spellings.Distinct(StringComparer.Ordinal).Count());
        CommandLineOptions.All.Count(option => option.Limit is not null).ShouldBe(14);

        foreach (var option in CommandLineOptions.All)
        {
            option.Name.ShouldStartWith("--", Case.Sensitive);
            option.Description.ShouldNotBeNullOrWhiteSpace();
            (option.ValueLabel is null).ShouldBe(
                option.Arity is CommandLineOptionArity.Flag or CommandLineOptionArity.Informational);
            (option.Limit is not null).ShouldBe(option.HelpGroup == CommandLineHelpGroup.Limits);

            if (option.Alias is not null)
            {
                option.Alias.ShouldStartWith("-", Case.Sensitive);
                option.Alias.ShouldNotStartWith("--", Case.Sensitive);
            }
        }
    }

    /// <summary>
    /// Defaults are recomputed independently from the runtime <see cref="ResourceLimits.Defaults"/>
    /// values. This deliberately does not call the production help formatter: a stale literal or
    /// a broken formatter must make the test red rather than agree with itself.
    /// </summary>
    [Test]
    public void EveryLimitRowShowsItsEffectiveRuntimeDefault()
    {
        foreach (var option in CommandLineOptions.All.Where(option => option.Limit is not null))
        {
            var value = option.Limit!.ReadValue(ResourceLimits.Defaults);
            var expected = FormatExpectedDefault(option.Limit.Kind, value);

            Rows[option.Name].Text.ShouldContain($"Default: {expected}.", Case.Sensitive);
        }
    }

    [Test]
    public void LimitHelpStatesBothValueGrammarsAndTheDepthCeiling()
    {
        var help = HelpText.Render();

        help.ShouldContain("Count and depth values use [1-9][0-9]*.", Case.Sensitive);
        help.ShouldContain(
            "Byte values use [1-9][0-9]* with\n  an optional case-insensitive KiB, MiB or GiB suffix.",
            Case.Sensitive);
        help.ShouldContain(
            $"the --max-depth ceiling is\n  {LimitValue.MaxDepthCeiling.ToString("N0", CultureInfo.InvariantCulture)}",
            Case.Sensitive);
    }

    private static Dictionary<string, HelpOptionRow> ParseRows()
    {
        var rows = new Dictionary<string, HelpOptionRow>(StringComparer.Ordinal);
        HelpOptionRowBuilder? current = null;

        foreach (var line in HelpText.Render().Split('\n'))
        {
            var match = OptionRow().Match(line);
            if (match.Success)
            {
                if (current is not null)
                {
                    rows.Add(current.Name, current.Build());
                }

                current = new HelpOptionRowBuilder(
                    match.Groups["name"].Value,
                    match.Groups["alias"].Success ? match.Groups["alias"].Value : null,
                    match.Groups["value"].Success ? match.Groups["value"].Value : null,
                    line.Trim());
                continue;
            }

            if (current is not null && line.Length >= 38 && string.IsNullOrWhiteSpace(line[..38]))
            {
                current.Append(line.Trim());
                continue;
            }

            if (current is not null)
            {
                rows.Add(current.Name, current.Build());
                current = null;
            }
        }

        if (current is not null)
        {
            rows.Add(current.Name, current.Build());
        }

        return rows;
    }

    private static string? RenderValueLabel(CommandLineOption option)
    {
        if (option.ValueLabel is null)
        {
            return null;
        }

        var suffix = option.Arity == CommandLineOptionArity.List ? "..." : string.Empty;
        return $"<{option.ValueLabel}>{suffix}";
    }

    private static string FormatExpectedDefault(ResourceLimitKind kind, long value)
    {
        if (kind == ResourceLimitKind.Count)
        {
            return value.ToString("N0", CultureInfo.InvariantCulture);
        }

        foreach (var (suffix, scale) in new[]
                 {
                     ("GiB", 1024L * 1024 * 1024),
                     ("MiB", 1024L * 1024),
                     ("KiB", 1024L),
                 })
        {
            if (value % scale == 0)
            {
                return $"{value / scale} {suffix}";
            }
        }

        return $"{value.ToString("N0", CultureInfo.InvariantCulture)} bytes";
    }

    private sealed record HelpOptionRow(string Name, string? Alias, string? ValueLabel, string Text);

    private sealed class HelpOptionRowBuilder(
        string name,
        string? alias,
        string? valueLabel,
        string firstLine)
    {
        private readonly List<string> parts = [firstLine];

        internal string Name { get; } = name;

        internal void Append(string continuation) => parts.Add(continuation);

        internal HelpOptionRow Build() =>
            new(Name, alias, valueLabel, string.Join(' ', parts));
    }
}
