using CommandLine;
using Microsoft.Extensions.Logging;
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
            IQualifiedNameMatchDictionary<string> keys,
            IQualifiedNameMatchList arrays,
            IQualifiedNameMatchList strings,
            ILogger<JsonFormatter> logger)
            : base(outputStreamFactory,
                  outputPrefix,
                  keys,
                  arrays,
                  strings,
                  logger)
        { }

        protected override async Task DoWrite(ProfileTree tree, Stream stream, CancellationToken cancellationToken)
        {
            using var writer = new StreamWriter(stream);
            using var jsonWriter = new JsonTextWriter(writer)
            {
                Formatting = Formatting.Indented
            };
            await ToJson(tree, Array.Empty<string>())
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
                    if (keys.TryMatch(newPrefix.ToQualifiedName(), out var key))
                    {
                        var nodes = node.Children
                            .OfType<ProfileTreeNode>()
                            .Select(child =>
                            {
                                var keyPayload = new Payload(
                                    new[] { key }.ToQualifiedName(),
                                    new IValueToken[] { new TextValueToken(child.NameString) },
                                    tree.GetFirstSourceMark());
                                var keyLeaf = new ProfileTreeLeaf(
                                    keyPayload,
                                    Enumerable.Empty<Comment>(),
                                    newPrefix.ToQualifiedName());

                                return ToJson(
                                    new ProfileTreeNode(node.Name, new[] { keyLeaf }.Concat(child.Children)),
                                    newPrefix.Concat(new[] { child.NameString }).ToArray());
                            }).ToArray();
                        return new JArray(nodes);
                    }

                    if (arrays.IsMatch(newPrefix.ToQualifiedName()))
                        return new JArray(
                            ProcessOverrides(node.Children, newPrefix)
                            .Select(x => new
                            {
                                child = x,
                                arrIndex = int.TryParse(x.NameString, out var index) ? index : int.MaxValue,
                            })
                            .OrderBy(x => x.arrIndex)
                            .ThenBy(x => x.child.NameString)
                            .Select(x =>
                                ToJson(
                                    x.child,
                                    newPrefix))
                            .ToArray());

                    return new JObject(ProcessOverrides(node.Children, newPrefix)
                        .Select(child =>
                            new JProperty(
                                child.NameString,
                                ToJson(
                                    child,
                                    newPrefix)))
                        .ToArray());

                case ProfileTreeLeaf leaf:
                    return ToJsonSingleValue(leaf.Value, newPrefix);

                default:
                    throw new NotSupportedException();
            }
        }

        private JToken ToJsonSingleValue(string value, string[] prefix)
        {
            if (strings.IsMatch(prefix.ToQualifiedName()))
                return new JValue(value);

            if (value == "[]")
                return new JArray();

            if (value == "{}")
                return new JObject();

            (var typedValue, var success) = TryParse(value);

            if (success)
                return typedValue == null ? JValue.CreateNull() : new JValue(typedValue);

            return new JValue(value);
        }
    }
}
