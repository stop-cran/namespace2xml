using Namespace2Xml.Semantics;
using Namespace2Xml.Syntax;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Namespace2Xml.Formatters
{
    public class JsonFormatter : JsonYamlFormatterBase
    {
        public JsonFormatter(
            Func<Stream> outputStreamFactory,
            [NullGuard.AllowNull] IReadOnlyList<string> outputPrefix,
            IReadOnlyDictionary<QualifiedName, string> keys,
            IReadOnlyList<QualifiedName> hiddenKeys,
            IReadOnlyList<QualifiedName> csvArrays,
            IReadOnlyList<QualifiedName> strings)
            : base(outputStreamFactory,
                  outputPrefix,
                  keys,
                  hiddenKeys,
                  csvArrays,
                  strings)
        { }

        protected override async Task DoWrite(ProfileTree tree, Stream stream, CancellationToken cancellationToken)
        {
            using (var writer = new StreamWriter(stream))
            using (var jsonWriter = new JsonTextWriter(writer)
            {
                Formatting = Formatting.Indented
            })
                await ToJson(tree, new string[0])
                    .WriteToAsync(jsonWriter);
        }

        private JToken ToJson(ProfileTree tree, string[] prefix)
        {
            string[] newPrefix = prefix
                                .Concat(new[] { tree.NameString })
                                .ToArray();

            switch (tree)
            {
                case ProfileTreeNode node:
                    return hiddenKeys.Contains(newPrefix.ToQualifiedName())
                        ? (JToken)new JArray(node.Children
                        .Select(child =>
                                ToJson(
                                    child,
                                    newPrefix)).ToArray())
                        : new JObject(node.Children
                        .Select(child =>
                            new JProperty(
                                child.NameString,
                                ToJson(
                                    child,
                                    newPrefix)))
                        .ToArray());

                case ProfileTreeLeaf leaf:
                    return ToJsonValue(leaf.Value, newPrefix);

                default:
                    throw new NotSupportedException();
            }
        }

        private JToken ToJsonValue(string value, string[] prefix)
        {
            if (csvArrays.Contains(prefix.ToQualifiedName()))
                return new JArray(value.Split(',').Select(part => ToJsonSingleValue(part, prefix)));

            return ToJsonSingleValue(value, prefix);
        }

        private JToken ToJsonSingleValue(string value, string[] prefix)
        {
            if (strings.Contains(prefix.ToQualifiedName()))
                return new JValue(value);

            (var typedValue, var success) = TryParse(value);

            if (success)
                return typedValue == null ? JValue.CreateNull() : new JValue(typedValue);

            return new JValue(value);
        }
    }
}
