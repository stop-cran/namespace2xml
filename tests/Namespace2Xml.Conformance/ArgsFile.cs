using System.Text;

namespace Namespace2Xml.Conformance;

/// <summary>
/// Reader for the <c>args.txt</c> format of specification Appendix C.1: UTF-8 without a BOM, LF
/// line endings, one exact CLI token per physical line, an empty line meaning a blank token, and
/// no environment substitution of any kind.
/// </summary>
public static class ArgsFile
{
    /// <summary>Reads a token vector from disk.</summary>
    public static IReadOnlyList<string> Read(string path) => Parse(File.ReadAllBytes(path), path);

    /// <summary>Parses a token vector from raw bytes.</summary>
    /// <exception cref="ConformanceFormatException">The bytes violate Appendix C.1.</exception>
    public static IReadOnlyList<string> Parse(byte[] bytes, string origin)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            throw new ConformanceFormatException($"{origin}: args files must not carry a byte-order mark.");
        }

        if (bytes.Contains((byte)'\r'))
        {
            throw new ConformanceFormatException($"{origin}: args files must use LF line endings.");
        }

        var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);

        if (text.Length == 0)
        {
            return [];
        }

        if (!text.EndsWith('\n'))
        {
            throw new ConformanceFormatException($"{origin}: args files must end with LF.");
        }

        // The final LF terminates the last token rather than introducing an empty one.
        return text[..^1].Split('\n');
    }
}

/// <summary>A conformance case is malformed, as distinct from the tool behaving incorrectly.</summary>
public sealed class ConformanceFormatException(string message) : Exception(message);
