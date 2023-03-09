using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Namespace2Xml.Formatters;

public class QuotedNamespaceFormatter : NamespaceFormatter
{
    public QuotedNamespaceFormatter(
        Func<Stream> outputStreamFactory,
        IReadOnlyList<string> outputPrefix,
        string delimiter,
        ILogger<NamespaceFormatter> logger)
        : base(outputStreamFactory, outputPrefix, delimiter, logger)
    {
    }

    protected override string FormatValue(string value)
    {
        return '"' + value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("$", "\\$")
            .Replace("`", "\\`")
            .Replace("!", "\\!") + '"';
    }
}