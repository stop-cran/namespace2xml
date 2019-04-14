using IniParser;
using IniParser.Model;
using Namespace2Xml.Semantics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Namespace2Xml.Scheme
{
    public class IniWriter : FileWriter
    {
        private readonly string delimiter;

        public IniWriter(string fileName, string delimiter)
            : base(fileName)
        {
            this.delimiter = delimiter;
        }

        protected override async Task DoWrite(ProfileTree tree, CancellationToken cancellationToken)
        {
            using (var stream = new MemoryStream())
            using (var writer = new StreamWriter(stream))
            {
                var parser = new StreamIniDataParser();
                var data = new IniData();

                foreach (var tuple in tree.GetLeafs())
                    data[tuple.prefix.First()][string.Join(delimiter, tuple.prefix.Skip(1).Concat(new[] { tuple.leaf.Name }))] = tuple.leaf.Value;

                parser.WriteData(writer, data);

                await writer.FlushAsync();

                stream.Seek(0, SeekOrigin.Begin);

                using (var fileStream = new FileStream(fileName, FileMode.Create, FileAccess.Write))
                    await stream.CopyToAsync(fileStream);
            }
        }
    }
}
