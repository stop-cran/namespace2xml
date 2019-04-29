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

namespace Namespace2Xml.Formatters
{
    public class YamlFormatter : JsonYamlFormatterBase
    {
        public YamlFormatter(
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
            var serializer = new SerializerBuilder()
                .WithEventEmitter(nextEmitter => new QuoteStringEventEmitter(nextEmitter))
                .Build();

            using (var writer = new StreamWriter(stream))
                await writer.WriteAsync(
                    serializer
                        .Serialize(ToObject(tree, new string[0]))
                        .ToCharArray(),
                    cancellationToken);
        }

        private object ToObject(ProfileTree tree, string[] prefix)
        {
            string[] newPrefix = prefix
                                .Concat(new[] { tree.NameString })
                                .ToArray();

            switch (tree)
            {
                case ProfileTreeNode node:
                    return hiddenKeys.Contains(newPrefix.ToQualifiedName())
                        ? (object)node.Children.Select(
                            child => ToObject(child, newPrefix)).ToArray()
                        : node.Children.ToDictionary(
                            child => child.NameString,
                            child => ToObject(child, newPrefix));

                case ProfileTreeLeaf leaf:
                    return ToObjectValue(leaf.Value, newPrefix);

                default:
                    throw new NotSupportedException();
            }
        }

        private object ToObjectValue(string value, string[] prefix) =>
            csvArrays.Contains(prefix.ToQualifiedName())
                ? value
                    .Split(',')
                    .Select(part => ToObjectSingleValue(part, prefix))
                    .ToArray()
                : ToObjectSingleValue(value, prefix);

        private object ToObjectSingleValue(string value, string[] prefix)
        {
            if (strings.Contains(prefix.ToQualifiedName()))
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
