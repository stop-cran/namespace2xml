using Namespace2Xml.Semantics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Namespace2Xml.Scheme
{
    public class NamespaceWriter : FileWriter
    {
        private readonly IReadOnlyList<string> outputPrefix;
        private readonly string delimiter;

        public NamespaceWriter(string fileName, IReadOnlyList<string> outputPrefix, string delimiter)
            : base(fileName)
        {
            this.outputPrefix = outputPrefix;
            this.delimiter = delimiter;
        }

        protected override async Task DoWrite(ProfileTree tree, CancellationToken cancellationToken)
        {
            await File.WriteAllLinesAsync(fileName,
                tree
                    .GetLeafs()
                    .OrderBy(pair => pair.leaf.LineNumber)
                    .Select(pair => FormatEntry(pair.prefix, pair.leaf)),
                cancellationToken);
        }

        private string FormatEntry(IEnumerable<string> prefix, ProfileTreeLeaf leaf)
        {
            var names = prefix.Concat(new[] { leaf.Name });

            if (outputPrefix != null)
                names = outputPrefix.Concat(names);

            if (delimiter == ".")
                names = names.Select(name => name.Replace(".", "\\."));

            return $"{string.Join(delimiter, names)}={leaf.Value}";
        }
    }
}
