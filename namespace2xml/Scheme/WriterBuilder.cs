using Namespace2Xml.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Namespace2Xml.Scheme
{
    public class WriterBuilder
    {
        private readonly string baseDirectory;

        public WriterBuilder(string baseDirectory)
        {
            this.baseDirectory = baseDirectory;
        }

        // RK TODO: Inject streams, rather than file names for testing.
        public IEnumerable<(IReadOnlyList<string> prefix, IOutputWriter writer)> Build(IEnumerable<Payload> schemeEntries)
        {
            var schemeEntriesList = schemeEntries
                .Select(payload => new
                {
                    Type = Enum.Parse<EntryType>(payload.Name.Parts.Last().Tokens.Cast<TextNameToken>().Single().Text),
                    Name = new QualifiedName(payload.Name.Parts.Take(payload.Name.Parts.Count - 1)),
                    Value = payload.Value.Cast<TextValueToken>().Single().Text,
                    payload.LineNumber
                })
                .ToList();

            return from entry in schemeEntriesList
                   where entry.Type == EntryType.output
                   select ((IReadOnlyList<string>)entry.Name.Parts.Select(part => part.ToString()).ToList().AsReadOnly(),
                        Build(Enum.TryParse<OutputType>(entry.Value, out var type)
                            ? type
                            : throw new NotSupportedException($"Ouput type {type} is not supported."),
                        new QualifiedName(entry.Name.Parts.AsEnumerable().Reverse().Take(1)),
                        (from entry2 in schemeEntriesList
                         where entry2.Name.Parts.Count >= entry.Name.Parts.Count &&
                             entry.Name.Equals(new QualifiedName(entry2.Name.Parts.Take(entry.Name.Parts.Count)))
                         select (new Payload(
                             new QualifiedName(entry2.Name.Parts.Skip(entry.Name.Parts.Count - 1)),
                             new[] { new TextValueToken(entry2.Value) },
                             entry2.LineNumber), entry2.Type)).ToList().AsReadOnly()));
        }

        private IOutputWriter Build(OutputType type, QualifiedName prefix, IEnumerable<(Payload Payload, EntryType Type)> entries) =>
            type == OutputType.ignore ? (IOutputWriter)new NullWriter()
            : new IgnoreWriter(BuildInner(type, prefix, entries),
                entries.GetNamesOfType(ValueType.ignore));

        private IOutputWriter BuildInner(OutputType type, QualifiedName prefix, IEnumerable<(Payload Payload, EntryType Type)> entries)
        {
            string fileName = entries.SingleOrDefaultValue(prefix, EntryType.filename);
            var rootString = entries.SingleOrDefaultValue(prefix, EntryType.root);
            var root = rootString == null ? new string[0] : rootString.Split('.');
            var delimiter = entries.SingleOrDefaultValue(prefix, EntryType.namespacedelimiter) ?? ".";

            switch (type)
            {
                case OutputType.@namespace:
                    return new NamespaceWriter(
                        Path.Combine(baseDirectory, fileName ?? prefix.Parts.Last().ToString() + ".properties"),
                        root,
                        delimiter);

                case OutputType.json:
                    return new JsonWriter(
                        Path.Combine(baseDirectory, fileName ?? prefix.Parts.Last().ToString() + ".json"),
                        root,
                        entries.GetKeys(),
                        entries.GetHiddenKeys(),
                        entries.GetNamesOfType(ValueType.csv),
                        entries.GetNamesOfType(ValueType.@string));

                case OutputType.xml:
                    return new XmlWriter(
                        Path.Combine(baseDirectory, fileName ?? prefix.Parts.Last().ToString() + ".xml"),
                        root,
                        Enum.Parse<XmlOptions>(entries.SingleOrDefaultValue(prefix, EntryType.xmloptions) ?? "None"),
                        entries.GetKeys(),
                        entries.GetHiddenKeys(),
                        entries.GetNamesOfType(ValueType.csv),
                        entries.GetNamesOfType(ValueType.element));

                case OutputType.yaml:
                    return new YamlWriter(
                        Path.Combine(baseDirectory, fileName ?? prefix.Parts.Last().ToString() + ".yaml"),
                        root,
                        entries.GetKeys(),
                        entries.GetHiddenKeys(),
                        entries.GetNamesOfType(ValueType.csv),
                        entries.GetNamesOfType(ValueType.@string));

                case OutputType.ini:
                    return new IniWriter(Path.Combine(baseDirectory, fileName ?? prefix.Parts.Last().ToString() + ".ini"),
                        delimiter);

                default:
                    throw new NotSupportedException($"Ouput type {type} is not supported.");
            }
        }
    }
}
