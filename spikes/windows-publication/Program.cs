using Namespace2Xml.Spikes.WindowsPublication;

// SPIKE: Windows secure publication (namespace2xml v3). See FINDINGS.md for the verdict & analysis.
//
// Proves a TOCTOU-safe, spec-conformant secure writer on Windows/net10.0 using the NT native API
// (handle-relative, no-follow opens), plus the string-layer validator, exercised against an
// adversarial corpus (junctions, symlinks, hardlinks, traversal, device names, ADS, MAX_PATH).

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("namespace2xml v3 SPIKE — Windows secure publication");
Console.WriteLine($"OS: {Environment.OSVersion}   .NET: {Environment.Version}   64-bit: {Environment.Is64BitProcess}");

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("This spike targets Windows publication and must run on Windows.");
    return 2;
}

return AdversarialHarness.Run();
