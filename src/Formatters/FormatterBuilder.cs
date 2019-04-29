using log4net;
using Namespace2Xml.Scheme;
using Namespace2Xml.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Namespace2Xml.Formatters
{
    public class FormatterBuilder : IFormatterBuilder
    {
        private readonly IStreamFactory streamFactory;
        private readonly ILog logger;

        public FormatterBuilder(IStreamFactory streamFactory, ILog logger)
        {
            this.streamFactory = streamFactory;
            this.logger = logger;
        }

        private Func<Stream> CreateOutputStream(string name, OutputType type) =>
            () => streamFactory.CreateOutputStream(name, type);

        public IEnumerable<(QualifiedName prefix, IFormatter formatter)> Build(SchemeNode node) =>
            from tuple in node.GetAllChildrenAndSelf()
            let childNode = tuple.entry as SchemeNode
            where childNode != null
            from formatter in BuildFormatters(childNode)
            select (new QualifiedName(tuple.prefix.Parts.Concat(new[] { childNode.Name })),
                (IFormatter)new IgnoreFormatter(formatter, childNode.GetNamesOfType(Scheme.ValueType.ignore)));

        private IEnumerable<IFormatter> BuildFormatters(SchemeNode node)
        {
            var types = node.SingleOrDefaultValue(EntryType.output);

            if (types == null)
                return Enumerable.Empty<IFormatter>();

            return types.Split(',').Select(type =>
                Enum.TryParse<OutputType>(type, out var outputType)
                ? BuildFormatter(node, outputType)
                : throw new ArgumentException($"Unsupported output type: {type}."));
        }

        private IFormatter BuildFormatter(SchemeNode node, OutputType outputType)
        {
            var errors = node.GetAllChildren()
                .Select(tuple => tuple.entry)
                .OfType<SchemeError>()
                .ToList();

            if (errors.Any())
            {
                foreach (var error in errors)
                    logger.Error(new
                    {
                        message = "Error reading scheme",
                        error = error.Error,
                        fileName = error.SourceMark.FileName,
                        line = error.SourceMark.LineNumber
                    });

                throw new ApplicationException();
            }

            var fileName = node.SingleOrDefaultValue(EntryType.filename);
            var root = node.SingleOrDefaultValue(EntryType.root)?.Split('.') ?? new string[0];
            var delimiter = node.SingleOrDefaultValue(EntryType.delimiter) ?? ".";
            var keys = (from child in node.GetAllChildren()
                        let leaf = child.entry as SchemeLeaf
                        where leaf?.Type == EntryType.key
                        select new
                        {
                            child.prefix,
                            leaf.Value
                        }).ToDictionary(
                            tuple => tuple.prefix,
                            tuple => tuple.Value);
            var hiddenKeys = node.GetNamesOfType(Scheme.ValueType.hiddenKey);

            switch (outputType)
            {
                case OutputType.@namespace:
                    return new NamespaceFormatter(
                        CreateOutputStream(fileName ?? node.Name + ".properties", outputType),
                        root,
                        delimiter);

                case OutputType.json:
                    return new JsonFormatter(
                        CreateOutputStream(fileName ?? node.Name + ".json", outputType),
                        root,
                        keys,
                        hiddenKeys,
                        node.GetNamesOfType(Scheme.ValueType.csv),
                        node.GetNamesOfType(Scheme.ValueType.@string));

                case OutputType.xml:
                    var xmlOptions = node.SingleOrDefaultValue(EntryType.xmloptions);

                    return new XmlFormatter(
                        CreateOutputStream(fileName ?? node.Name + ".xml", outputType),
                        root,
                        xmlOptions == null
                            ? XmlOptions.None
                            : Enum.TryParse<XmlOptions>(xmlOptions, out var options)
                                ? options
                                : throw new ArgumentException($"Unsupported XML options: {xmlOptions}."),
                        keys,
                        hiddenKeys,
                        node.GetNamesOfType(Scheme.ValueType.csv),
                        node.GetNamesOfType(Scheme.ValueType.element));

                case OutputType.yaml:
                    return new YamlFormatter(
                        CreateOutputStream(fileName ?? node.Name + ".yml", outputType),
                        root,
                        keys,
                        hiddenKeys,
                        node.GetNamesOfType(Scheme.ValueType.csv),
                        node.GetNamesOfType(Scheme.ValueType.@string));

                case OutputType.ini:
                    return new IniFormatter(
                        CreateOutputStream(fileName ?? node.Name + ".ini", outputType),
                        root,
                        delimiter);

                default:
                    throw new ArgumentException($"Ouput type {outputType} is not supported.");
            }
        }
    }
}
