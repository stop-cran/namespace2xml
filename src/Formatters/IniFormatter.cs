using IniFileParser;
using IniFileParser.Model;
using Microsoft.Extensions.Logging;
using Namespace2Xml.Semantics;
using Namespace2Xml.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IniFileParser.Model.Configuration;
using IniFileParser.Model.Formatting;

namespace Namespace2Xml.Formatters
{
    public class IniFormatter : StreamFormatter
    {
        private readonly IReadOnlyList<string> outputPrefix;
        private readonly string delimiter;

        public IniFormatter(Func<Stream> outputStreamFactory, IReadOnlyList<string> outputPrefix, string delimiter, ILogger<IniFormatter> logger)
            : base(outputStreamFactory, logger)
        {
            this.outputPrefix = outputPrefix;
            this.delimiter = delimiter;
        }

        protected override async Task DoWrite(ProfileTree tree, Stream stream, CancellationToken cancellationToken)
        {
            using var memoryStream = new MemoryStream();
            using var writer = new StreamWriter(stream);
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

                if (names.Count() > 1)
                    data[names.First()][string.Join(delimiter, names.Skip(1))] = leaf.Value;
                else
                {
                    var collection = new KeyDataCollection();
                    collection.AddKey(names.First(), leaf.Value);
                    data.MergeGlobal(collection);
                }
            }

            parser.WriteData(writer, data, new DefaultIniDataFormatter(new IniParserConfiguration() { AssigmentSpacer = String.Empty } ));

            await writer.FlushAsync();

            memoryStream.Seek(0, SeekOrigin.Begin);

            await memoryStream.CopyToAsync(stream, cancellationToken);
        }
    }
}
