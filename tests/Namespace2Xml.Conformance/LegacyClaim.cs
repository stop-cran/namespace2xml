using System.Text.RegularExpressions;

namespace Namespace2Xml.Conformance;

/// <summary>The closed Appendix C.6 verdict vocabulary.</summary>
internal enum LegacyVerdict
{
    /// <summary>2.4.0 produces the case's expected tree and exit code.</summary>
    Agrees,

    /// <summary>2.4.0 produces something else, and Section 3.2 says why that is correct.</summary>
    Differs,

    /// <summary>2.4.0 terminates abnormally.</summary>
    Crashes,

    /// <summary>2.4.0 does not produce the same result on repeated runs of the same case.</summary>
    Nondeterministic,
}

/// <summary>
/// The Appendix C.6 verdict a case declares about namespace2xml 2.4.0, read from its
/// <c>legacy.md</c>.
/// </summary>
/// <param name="Verdict">The declared verdict.</param>
/// <param name="Line">The verdict line as written, for use in failure messages.</param>
internal sealed record LegacyClaim(LegacyVerdict Verdict, string Line)
{
    /// <summary>
    /// Matches the Appendix C.6 verdict line. The pattern is deliberately narrow: a bold run
    /// anywhere in the file would match far too much prose, and a case whose verdict was picked up
    /// from a sentence about something else would be checked against a claim nobody made.
    /// </summary>
    private static readonly Regex Pattern = new(
        @"^- namespace2xml 2\.4\.0: \*\*(?<verdict>[a-z]+)\*\*",
        RegexOptions.Multiline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// Reads the verdict a case declares, or <see langword="null"/> when it carries no
    /// <c>legacy.md</c> at all and is therefore not a differential case.
    /// </summary>
    /// <param name="conformanceCase">The case to read.</param>
    /// <exception cref="ConformanceFormatException">
    /// The file declares no verdict, more than one verdict, or a word outside the closed
    /// vocabulary. All three are silent-coverage-loss failures: each would otherwise read as
    /// "no claim", which Appendix C.6 treats as a claim that the baseline agrees.
    /// </exception>
    internal static LegacyClaim? Read(ConformanceCase conformanceCase)
    {
        ArgumentNullException.ThrowIfNull(conformanceCase);

        var path = Path.Combine(conformanceCase.Directory, "legacy.md");

        if (!File.Exists(path))
        {
            return null;
        }

        var matches = Pattern.Matches(File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal));

        if (matches.Count == 0)
        {
            throw new ConformanceFormatException(
                $"{conformanceCase.Name}: legacy.md declares no Appendix C.6 verdict. A case that " +
                "carries the file but no verdict line reads as a claim that the baseline agrees, " +
                "which is the opposite of what an author who wrote the file meant. The line is " +
                "`- namespace2xml 2.4.0: **<verdict>**`, and the bold run must close at the " +
                "verdict word itself.");
        }

        if (matches.Count > 1)
        {
            throw new ConformanceFormatException(
                $"{conformanceCase.Name}: legacy.md declares {matches.Count} verdicts, but Appendix C.6 " +
                "allows at most one.");
        }

        var word = matches[0].Groups["verdict"].Value;

        var verdict = word switch
        {
            "agrees" => LegacyVerdict.Agrees,
            "differs" => LegacyVerdict.Differs,
            "crashes" => LegacyVerdict.Crashes,
            "nondeterministic" => LegacyVerdict.Nondeterministic,
            _ => throw new ConformanceFormatException(
                $"{conformanceCase.Name}: legacy.md declares verdict '{word}', which is outside the " +
                "closed Appendix C.6 vocabulary (agrees, differs, crashes, nondeterministic)."),
        };

        return new LegacyClaim(verdict, matches[0].Value);
    }

    /// <summary>Whether the verdict asserts that the baseline reproduces the expected result.</summary>
    internal bool ExpectsAgreement => Verdict == LegacyVerdict.Agrees;
}
