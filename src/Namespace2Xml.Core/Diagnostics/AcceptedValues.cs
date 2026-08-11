using System.Globalization;

namespace Namespace2Xml.Diagnostics;

/// <summary>
/// Renders the accepted members of a closed set into the sentence that refuses one.
/// </summary>
/// <remarks>
/// A refusal that names only the rejected token leaves the caller with nowhere to go but the
/// specification, and the code deciding to refuse already holds the answer. Every list here is
/// derived from the table the parser matched against rather than written out beside it, so a value
/// added to the parser cannot fail to appear in the message that rejects its neighbours.
/// </remarks>
internal static class AcceptedValues
{
    /// <summary>Renders a closed set as a sentence naming every member.</summary>
    /// <param name="values">The accepted spellings, in the order the specification lists them.</param>
    internal static string Sentence(IEnumerable<string> values)
    {
        var quoted = values.Select(value => $"'{value}'").ToList();

        return quoted.Count switch
        {
            0 => string.Empty,
            1 => $" The only accepted value is {quoted[0]}.",
            _ => string.Format(
                CultureInfo.InvariantCulture,
                " The accepted values are {0} and {1}.",
                string.Join(", ", quoted.Take(quoted.Count - 1)),
                quoted[^1]),
        };
    }

    /// <summary>
    /// Renders the members of a flag enumeration, which is how Sections 16.8 and 16.9 spell every
    /// format option set.
    /// </summary>
    /// <typeparam name="TFlags">The option enumeration.</typeparam>
    /// <remarks>
    /// <c>None</c> is excluded because it is the empty selection rather than a value an author may
    /// write, and every one of these parsers already refuses it. Declaration order is preserved, and
    /// each of these enumerations is declared in the order its Section 16.9 bullet list uses.
    /// </remarks>
    internal static string OfFlags<TFlags>()
        where TFlags : struct, Enum =>
        Sentence(Enum.GetNames<TFlags>().Where(name => name != "None"));
}
