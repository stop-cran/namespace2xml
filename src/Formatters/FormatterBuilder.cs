﻿using Microsoft.Extensions.Logging;
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
        private readonly ILoggerFactory loggerFactory;
        private readonly ILogger<FormatterBuilder> logger;

        public FormatterBuilder(IStreamFactory streamFactory, ILoggerFactory loggerFactory)
        {
            this.streamFactory = streamFactory;
            this.loggerFactory = loggerFactory;
            this.logger = loggerFactory.CreateLogger<FormatterBuilder>();
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
                    logger.LogError("Error reading scheme: {0}, file name: {1}, line: {2}",
                        error.Error,
                        error.SourceMark.FileName,
                        error.SourceMark.LineNumber);

                throw new ApplicationException();
            }

            var fileName = node.SingleOrDefaultValue(EntryType.filename);
            var root = node.SingleOrDefaultValue(EntryType.root)?.Split('.') ?? new string[0];
            var delimiter = node.SingleOrDefaultValue(EntryType.delimiter) ?? ".";
            var keys = new QualifiedNameMatchDictionary<string>(from child in node.GetAllChildren()
                                                                let leaf = child.entry as SchemeLeaf
                                                                where leaf?.Type == EntryType.key
                                                                select new KeyValuePair<QualifiedName, string>(child.prefix, leaf.Value));
            var arrays = node.GetNamesOfType(Scheme.ValueType.array);

            switch (outputType)
            {
                case OutputType.@namespace:
                    return new NamespaceFormatter(
                        CreateOutputStream(fileName ?? node.Name + ".properties", outputType),
                        root,
                        delimiter,
                        loggerFactory.CreateLogger<NamespaceFormatter>());

                case OutputType.json:
                    return new JsonFormatter(
                        CreateOutputStream(fileName ?? node.Name + ".json", outputType),
                        root,
                        keys,
                        arrays,
                        node.GetNamesOfType(Scheme.ValueType.@string),
                        loggerFactory.CreateLogger<JsonFormatter>());

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
                        arrays,
                        node.GetNamesOfType(Scheme.ValueType.element),
                        loggerFactory.CreateLogger<XmlFormatter>());

                case OutputType.yaml:
                    return new YamlFormatter(
                        CreateOutputStream(fileName ?? node.Name + ".yml", outputType),
                        root,
                        keys,
                        arrays,
                        node.GetNamesOfType(Scheme.ValueType.@string),
                        loggerFactory.CreateLogger<YamlFormatter>());

                case OutputType.ini:
                    return new IniFormatter(
                        CreateOutputStream(fileName ?? node.Name + ".ini", outputType),
                        root,
                        delimiter,
                        loggerFactory.CreateLogger<IniFormatter>());

                default:
                    throw new ArgumentException($"Ouput type {outputType} is not supported.");
            }
        }
    }
}
