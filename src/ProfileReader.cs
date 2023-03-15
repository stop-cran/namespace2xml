using Microsoft.Extensions.Logging;
using MoreLinq;
using Namespace2Xml.Formatters;
using Namespace2Xml.Syntax;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NullGuard;
using Sprache;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NodeTypeResolvers;

namespace Namespace2Xml
{
    public class ProfileReader : IProfileReader
    {
        private readonly IStreamFactory streamFactory;
        private readonly ILogger<ProfileReader> logger;
        private readonly Dictionary<QualifiedName, int> implicitIndices = new Dictionary<QualifiedName, int>();

        public ProfileReader(
            IStreamFactory streamFactory,
            ILogger<ProfileReader> logger)
        {
            this.streamFactory = streamFactory;
            this.logger = logger;
        }

        public IReadOnlyList<IProfileEntry> ReadVariables(
            IEnumerable<string> variables) =>
            CheckErrorsAndMerge(variables
                .Select(variable => TryParse(variable, int.MaxValue, "<command line>")));

        public async Task<IReadOnlyList<IProfileEntry>> ReadFiles(
            IEnumerable<string> files,
            CancellationToken cancellationToken)
        {
            var entries = await Task.WhenAll(files.Select(ReadInput));

            return CheckErrorsAndMerge(entries);
        }

        private (IResult<IEnumerable<IProfileEntry>> result, string fileName) TryParse(string input, int fileNumber, string fileName) =>
            (Parsers.GetProfileParser(fileNumber, fileName).TryParse(input), fileName);

        private IReadOnlyList<T> CheckErrorsAndMerge<T>(IEnumerable<(IResult<IEnumerable<T>> result, string fileName)> results)
        {
            var resultsList = results.ToList();

            var errors = from tuple in resultsList
                         where !tuple.result?.WasSuccessful ?? true
                         select tuple.result == null ? null :
                         new
                         {
                             message = tuple.result.Message,
                             tuple.fileName,
                             line = tuple.result.Remainder?.Line,
                             column = tuple.result.Remainder?.Column
                         };

            if (errors.Any())
            {
                foreach (var error in errors.Where(error => error != null))
                    logger.LogError("Error parsing input: {0}, file: {1}, line: {2}, column: {3}",
                        error.message, error.fileName, error.line, error.column);
                throw new ApplicationException();
            }

            return resultsList
                .SelectMany(result => result.result.Value)
                .ToList();
        }

        private async Task<(IResult<IEnumerable<IProfileEntry>> result, string fileName)> ReadInput(string fileName, int fileNumber)
        {
            try
            {
                using var stream = streamFactory.CreateInputStream(fileName);
                using var reader = new StreamReader(stream);

                switch (Path.GetExtension(fileName))
                {
                    case ".json":
                        using (var jsonReader = new JsonTextReader(reader))
                        {
                            var json = await JObject.LoadAsync(jsonReader);

                            return (Result.Success(JsonToProfileEntries(Array.Empty<string>(), json, fileName, fileNumber), null), fileName);
                        }

                    case ".yml":
                    case ".yaml":

                        var deserializer = new DeserializerBuilder()
                            .WithNodeTypeResolver(
                                  new ExpandoNodeTypeResolver(),
                                  ls => ls.InsteadOf<DefaultContainersNodeTypeResolver>())
                            .Build();

                        dynamic yaml = deserializer.Deserialize(reader);

                        return (Result.Success(YamlToProfileEntries(Array.Empty<string>(), yaml, fileName, fileNumber), null), fileName);

                    case ".xml":
                        var xml = XDocument.Load(reader);
                        var res = (Result.Success(XmlToProfileEntries(new[] { GetNameWithNamespacePrefix(xml.Root) }, xml.Root, fileName, fileNumber), null), fileName);

                        return res;

                    default:
                        return TryParse(await reader.ReadToEndAsync(), fileNumber, fileName);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error reading input from file {0}", fileName);

                throw new ApplicationException();
            }
        }

        public class ExpandoNodeTypeResolver : INodeTypeResolver
        {
            public bool Resolve(NodeEvent nodeEvent, ref Type currentType)
            {
                if (currentType == typeof(object))
                {
                    if (nodeEvent is SequenceStart)
                    {
                        currentType = typeof(List<object>);
                        return true;
                    }
                    if (nodeEvent is MappingStart)
                    {
                        currentType = typeof(ExpandoObject);
                        return true;
                    }
                }

                return false;
            }
        }

        static Parser<NamePart> nameParser = Parsers.GetNamePartParser();
        static Parser<IEnumerable<IValueToken>> valueParser = Parsers.GetValueParser();

        static Payload ParsePayload(string[] nameParts, string value, string fileName, int fileNumber)
        {
            var sourceMark = new SourceMark(fileNumber, fileName, 0);

            var parsedParts = nameParts.Select(part => nameParser.Parse(part)).ToList();
            var parsedValue = valueParser.Parse(value);

            return new Payload(new QualifiedName(parsedParts), parsedValue, sourceMark);
        }

        private IEnumerable<IProfileEntry> JsonToProfileEntries(string[] prefix, JToken json, string fileName, int fileNumber)
        {
            if (json is JObject jObject)
            {
                if (jObject.Properties().Any())
                    return jObject.Properties().SelectMany(property => JsonToProfileEntries(prefix.Concat(new[] { property.Name }).ToArray(), property.Value, fileName, fileNumber))
                        .ToList();
                else
                    return new[] { new Payload(prefix.ToQualifiedName(), new[] { new TextValueToken("{}") }, new SourceMark(fileNumber, fileName, 0)) };
            }
            if (json is JArray jArray)
            {
                if (jArray.Any())
                {
                    implicitIndices.TryGetValue(prefix.ToQualifiedName(), out int baseIndex);
                    implicitIndices[prefix.ToQualifiedName()] = baseIndex + jArray.Count;

                    return jArray.SelectMany((value, index) => JsonToProfileEntries(prefix.Concat(new[] { (baseIndex + index).ToString() }).ToArray(), value, fileName, fileNumber))
                        .ToList();
                }
                else
                    return new[] { new Payload(prefix.ToQualifiedName(), new[] { new TextValueToken("[]") }, new SourceMark(fileNumber, fileName, 0)) };
            }
            if (json is JValue jValue)
            {
                var payload = ParsePayload(prefix, jValue.Value?.ToString() ?? "null", fileName, fileNumber);

                return new[] { payload };
            }
            throw new ApplicationException();
        }

        private IEnumerable<Payload> YamlToProfileEntries(string[] prefix, object yaml, string fileName, int fileNumber)
        {
            if (yaml is ExpandoObject expandoObject)
            {
                if (expandoObject.Any())
                    return expandoObject
                        .SelectMany(property => YamlToProfileEntries(prefix.Concat(new[] { property.Key }).ToArray(), property.Value, fileName, fileNumber))
                        .ToList();
                else
                    return new[] { new Payload(prefix.ToQualifiedName(), new[] { new TextValueToken("{}") }, new SourceMark(fileNumber, fileName, 0)) };
            }
            if (yaml is List<object> yamlArray)
            {
                if (yamlArray.Any())
                {
                    implicitIndices.TryGetValue(prefix.ToQualifiedName(), out int baseIndex);
                    implicitIndices[prefix.ToQualifiedName()] = baseIndex + yamlArray.Count;

                    return yamlArray
                        .SelectMany((value, index) => YamlToProfileEntries(prefix.Concat(new[] { (baseIndex + index).ToString() }).ToArray(), value, fileName, fileNumber))
                        .ToList();
                }
                else
                    return new[] { new Payload(prefix.ToQualifiedName(), new[] { new TextValueToken("[]") }, new SourceMark(fileNumber, fileName, 0)) };
            }

            var payload = ParsePayload(prefix, yaml?.ToString() ?? "null", fileName, fileNumber);

            return new[] { payload };
        }
        static string GetNameWithNamespacePrefix(XElement element)
        {
            var prefix = element.GetPrefixOfNamespace(element.Name.Namespace);
            return prefix == null ? element.Name.LocalName : prefix + ':' + element.Name.LocalName;
        }
        static string GetNameWithNamespacePrefix(XAttribute attribute)
        {
            var prefix = attribute.Parent.GetPrefixOfNamespace(attribute.Name.Namespace);
            return prefix == null ? attribute.Name.LocalName : prefix + ':' + attribute.Name.LocalName;
        }

        private IEnumerable<Payload> XmlToProfileEntries(string[] prefix, XElement xml, string fileName, int fileNumber)
        {
            if (xml.HasAttributes || xml.HasElements)
            {
                bool isArray = xml.Elements().Count() > 1 && xml.Elements().Select(x => x.Name.ToString()).Distinct().Count() == 1;
                int baseIndex = 0;

                if (isArray)
                {
                    implicitIndices.TryGetValue(prefix.ToQualifiedName(), out baseIndex);
                    implicitIndices[prefix.ToQualifiedName()] = baseIndex + xml.Elements().Count();
                }

                return xml.Attributes().Select(attribute =>
                ParsePayload(prefix.Concat(new[] { GetNameWithNamespacePrefix(attribute) }).ToArray(), attribute.Value, fileName, fileNumber)).Concat(
                isArray
                ? xml.Elements().SelectMany((childElement, index) => XmlToProfileEntries(prefix.Concat(new[] { GetNameWithNamespacePrefix(childElement), (baseIndex + index).ToString() }).ToArray(), childElement, fileName, fileNumber))
                : xml.Elements().SelectMany(childElement => XmlToProfileEntries(prefix.Concat(new[] { GetNameWithNamespacePrefix(childElement) }).ToArray(), childElement, fileName, fileNumber)))
                .ToList();
            }
            else
                return new[] { new Payload(prefix.ToQualifiedName(), new[] { new TextValueToken("") }, new SourceMark(fileNumber, fileName, 0)) };
        }
    }
}
