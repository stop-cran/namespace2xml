using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;

namespace Namespace2Xml.Cli;

/// <summary>A fully parsed and validated command line, specification Section 6.2.</summary>
/// <remarks>
/// The compiler-generated equality on this record is not structural: <c>ImmutableArray&lt;T&gt;</c>
/// compares its underlying array by reference, so two identically-populated results compare
/// unequal. Compare the components, not the record.
/// </remarks>
/// <param name="Inputs">Ordered input file paths, concatenated across every occurrence.</param>
/// <param name="Schemes">Ordered scheme file paths, concatenated across every occurrence.</param>
/// <param name="Variables">Ordered namespace records applied after all input files.</param>
/// <param name="OutputRoot">Output root directory. The current directory when not supplied.</param>
/// <param name="Verbosity">Output threshold.</param>
/// <param name="DiagnosticsFormat">Encoding of the diagnostic stream.</param>
/// <param name="Limits">Resource bounds.</param>
public sealed record CommandLine(
    ImmutableArray<string> Inputs,
    ImmutableArray<string> Schemes,
    ImmutableArray<string> Variables,
    string OutputRoot,
    Verbosity Verbosity,
    DiagnosticFormat DiagnosticsFormat,
    ResourceLimits Limits);
