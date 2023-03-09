using Microsoft.Extensions.Logging;
using Namespace2Xml.Semantics;
using Namespace2Xml.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Namespace2Xml.Formatters
{
    public class NamespaceFormatter : StreamFormatter
    {
        private readonly IReadOnlyList<string> outputPrefix;
        private readonly string delimiter;

        public NamespaceFormatter(Func<Stream> outputStreamFactory, IReadOnlyList<string> outputPrefix, string delimiter, ILogger<NamespaceFormatter> logger)
            : base(outputStreamFactory, logger)
        {
            this.outputPrefix = outputPrefix;
            this.delimiter = delimiter;
        }

        protected override async Task DoWrite(ProfileTree tree, Stream stream, CancellationToken cancellationToken)
        {
            using (var writer = new StreamWriter(stream))
                foreach (var line in tree
                        .GetLeafs()
                        .OrderBy(pair => pair.leaf.SourceMark)
                        .SelectMany(pair => FormatEntry(pair.prefix, pair.leaf)))
                    await writer.WriteLineAsync(line.ToCharArray(), cancellationToken);
        }

        private IEnumerable<string> FormatEntry(QualifiedName prefix, ProfileTreeLeaf leaf)
        {
            var names = prefix.Parts
                .Select(part => part.Tokens.Cast<TextNameToken>().Single().Text)
                .Skip(1);

            if (outputPrefix != null)
                names = outputPrefix.Concat(names);

            if (delimiter == ".")
                names = names.Select(name => name.Replace(".", "\\."));

            return leaf
                .LeadingComments
                .Select(comment => comment.ToString())
                .Concat(new[] { $"{string.Join(delimiter, names)}={FormatValue(leaf.Value)}" });
        }

        protected virtual string FormatValue(string value) => value;
    }
}
