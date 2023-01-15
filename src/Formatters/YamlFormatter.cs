using Microsoft.Extensions.Logging;
using Namespace2Xml.Semantics;
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
            IQualifiedNameMatchDictionary<string> keys,
            IQualifiedNameMatchList arrays,
            IQualifiedNameMatchList strings,
            ILogger<YamlFormatter> logger)
            : base(outputStreamFactory,
                  outputPrefix,
                  keys,
                  arrays,
                  strings,
                  logger)
        { }

        protected override async Task DoWrite(ProfileTree tree, Stream stream, CancellationToken cancellationToken)
        {
            var serializer = new SerializerBuilder()
                .WithEventEmitter(nextEmitter => new QuoteStringEventEmitter(nextEmitter))
                .Build();

            using var writer = new StreamWriter(stream);

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

            return tree switch
            {
                ProfileTreeNode node => arrays.IsMatch(newPrefix.ToQualifiedName())
                                        ? node.Children.Select(
                                            child => ToObject(child, newPrefix)).ToArray()
                                        : node.Children.ToDictionary(
                                            child => child.NameString,
                                            child => ToObject(child, newPrefix)),
                ProfileTreeLeaf leaf => ToObjectSingleValue(leaf.Value, newPrefix),
                _ => throw new NotSupportedException(),
            };
        }

        private object ToObjectSingleValue(string value, string[] prefix)
        {
            if (strings.IsMatch(prefix.ToQualifiedName()))
                return value;

            if (value == "[]")
                return new string[0];

            if (value == "{}")
                return new Dictionary<string, string>();

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
