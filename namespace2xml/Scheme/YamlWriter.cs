using Namespace2Xml.Semantics;
using Namespace2Xml.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;

namespace Namespace2Xml.Scheme
{
    public class YamlWriter : JsonYamlWriterBase
    {
        public YamlWriter(
            string fileName,
            [NullGuard.AllowNull] IReadOnlyList<string> outputPrefix,
            IReadOnlyDictionary<QualifiedName, string> keys,
            IReadOnlyList<QualifiedName> hiddenKeys,
            IReadOnlyList<QualifiedName> csvArrays,
            IReadOnlyList<QualifiedName> strings)
            : base(fileName,
                  outputPrefix,
                  keys,
                  hiddenKeys,
                  csvArrays,
                  strings)
        { }

        protected override async Task DoWrite(ProfileTree tree, CancellationToken cancellationToken)
        {
            var serializer = new SerializerBuilder()
                .WithEventEmitter(nextEmitter => new QuoteStringEventEmitter(nextEmitter))
                .Build();

            await File.WriteAllTextAsync(fileName, serializer.Serialize(ToObject(tree, new string[0])));
        }

        private object ToObject(ProfileTree tree, string[] prefix)
        {
            string[] newPrefix = prefix
                                .Concat(new[] { tree.Name })
                                .ToArray();

            switch (tree)
            {
                case ProfileTreeNode node:
                    return hiddenKeys.IsMatch(newPrefix)
                        ? (object)node.Children.Select(
                            child => ToObject(child, newPrefix)).ToArray()
                        : node.Children.ToDictionary(
                            child => child.Name,
                            child => ToObject(child, newPrefix));

                case ProfileTreeLeaf leaf:
                    return ToObjectValue(leaf.Value, newPrefix);

                default:
                    throw new NotSupportedException();
            }
        }

        private object ToObjectValue(string value, string[] prefix) =>
            csvArrays.IsMatch(prefix)
                ? value
                    .Split(',')
                    .Select(part => ToObjectSingleValue(part, prefix))
                    .ToArray()
                : ToObjectSingleValue(value, prefix);

        private object ToObjectSingleValue(string value, string[] prefix)
        {
            if (strings.IsMatch(prefix))
                return value;

            (var typedValue, var success) = TryParse(value);

            return success ? typedValue : value;
        }

        private class QuoteStringEventEmitter : ChainedEventEmitter
        {
            public QuoteStringEventEmitter(IEventEmitter nextEmitter) : base(nextEmitter)
            { }

            public override void Emit(ScalarEventInfo eventInfo, IEmitter emitter)
            {
                if (eventInfo.Source.Value is string value && TryParse(value).success)
                    eventInfo.Style = ScalarStyle.SingleQuoted;
                base.Emit(eventInfo, emitter);
            }
        }
    }
}
