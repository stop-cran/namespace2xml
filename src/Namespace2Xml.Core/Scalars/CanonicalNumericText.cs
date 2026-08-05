using System.Globalization;
using System.Numerics;

namespace Namespace2Xml.Scalars;

/// <summary>
/// Renders the base-10 integer text required by specification Section 18.
/// </summary>
/// <remarks>
/// Section 18 closes with the requirement that canonical decimal text and base-10 integer text are used
/// by <b>every</b> output format and by interpolation, and Section 24 requires the result to be
/// independent of the runtime locale. 2.4.0 diverged here — divergence 261 records that its JSON and
/// YAML numeric handling depended on the runtime culture — so the conversion lives in one place that
/// pins <see cref="CultureInfo.InvariantCulture"/>, rather than at each of the call sites that could
/// each forget. <see cref="BigDecimal.ToCanonicalText"/> is the decimal half of the same contract.
/// </remarks>
public static class CanonicalNumericText
{
    /// <summary>
    /// Renders the base-10 integer text of Section 18 grammar rule 3: a minus sign only when negative,
    /// no thousands separators, no leading zeros, and no dependence on the current culture.
    /// </summary>
    public static string ToCanonicalText(this BigInteger value) =>
        value.ToString(CultureInfo.InvariantCulture);
}
