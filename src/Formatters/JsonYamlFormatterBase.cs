using Microsoft.Extensions.Logging;
using Namespace2Xml.Semantics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Namespace2Xml.Formatters
{
    public abstract class JsonYamlFormatterBase : StreamFormatter
    {
        protected readonly IReadOnlyList<string> outputPrefix;
        protected readonly IQualifiedNameMatchDictionary<string> keys;
        protected readonly IQualifiedNameMatchList arrays;
        protected readonly IQualifiedNameMatchList strings;
        protected readonly IQualifiedNameMatchList multiline;

        public JsonYamlFormatterBase(
            Func<Stream> outputStreamFactory,
            IReadOnlyList<string> outputPrefix,
            IQualifiedNameMatchDictionary<string> keys,
            IQualifiedNameMatchList arrays,
            IQualifiedNameMatchList strings,
            IQualifiedNameMatchList multiline,
            ILogger<JsonYamlFormatterBase> logger) : base(outputStreamFactory, logger)
        {
            this.outputPrefix = outputPrefix;
            this.keys = keys;
            this.arrays = arrays;
            this.strings = strings;
            this.multiline = multiline;
        }

        protected static (object typedValue, bool success) TryParse(string value)
        {
            if (value == null || value == "null")
                return (null, true);

            if (long.TryParse(value, out long i))
                return (i, true);

            if (double.TryParse(value, out double d))
                return (d, true);

            if (bool.TryParse(value, out bool b))
                return (b, true);

            return (null, false);
        }

        protected IEnumerable<ProfileTree> ProcessOverrides(IEnumerable<ProfileTree> entries, string[] prefix) =>
            entries.GroupBy(x => x.NameString).Select(children => ProcessOverridesForChildren(children, prefix));

        private ProfileTree ProcessOverridesForChildren(IEnumerable<ProfileTree> entries, string[] prefix)
        {
            ProfileTree result = null;

            foreach (var entry in entries)
            {
                if (result != null)
                {
                    if (result is ProfileTreeNode childNode && entry is ProfileTreeLeaf leaf)
                    {
                        if (leaf.Value == "[]" && arrays.IsMatch(prefix.Append(leaf.NameString).ToQualifiedName()))
                        {
                            logger.LogDebug("Entry has not been overridden in JSON, name: {name}", entry.NameString);
                            continue;
                        }
                        if (leaf.Value == "{}" && !arrays.IsMatch(prefix.Append(leaf.NameString).ToQualifiedName()))
                        {
                            logger.LogDebug("Entry has not been overridden in JSON, name: {name}", entry.NameString);
                            continue;
                        }
                    }

                    logger.LogDebug("Entry has been overridden in JSON, name: {name}", result.NameString);
                }

                result = entry;
            }

            return result;
        }
    }
}
