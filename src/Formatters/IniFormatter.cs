using IniFileParser;
using IniFileParser.Model;
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
    public class IniFormatter : StreamFormatter
    {
        private readonly IReadOnlyList<string> outputPrefix;
        private readonly string delimiter;

        public IniFormatter(Func<Stream> outputStreamFactory, IReadOnlyList<string> outputPrefix, string delimiter)
            : base(outputStreamFactory)
        {
            this.outputPrefix = outputPrefix;
            this.delimiter = delimiter;
        }

        protected override async Task DoWrite(ProfileTree tree, Stream stream, CancellationToken cancellationToken)
        {
            using (var memoryStream = new MemoryStream())
            using (var writer = new StreamWriter(stream))
            {
                var parser = new IniStreamParser();
                var data = new IniData();

                foreach (var (prefix, leaf) in tree.GetLeafs())
                {
                    var names = prefix.Parts
                        .Select(part => part.Tokens.Cast<TextNameToken>().Single().Text)
                        .Skip(1);

                    if (outputPrefix != null)
                        names = outputPrefix.Concat(names);

                    if (delimiter == ".")
                        names = names.Select(name => name.Replace(".", "\\."));

                    data[names.First()][string.Join(delimiter, names.Skip(1))] = leaf.Value;
                }

                parser.WriteData(writer, data);

                await writer.FlushAsync();

                memoryStream.Seek(0, SeekOrigin.Begin);

                await memoryStream.CopyToAsync(stream);
            }
        }
    }
}
