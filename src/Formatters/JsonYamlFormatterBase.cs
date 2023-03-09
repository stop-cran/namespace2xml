using Microsoft.Extensions.Logging;
using Namespace2Xml.Syntax;
using System;
using System.Collections.Generic;
using System.IO;

namespace Namespace2Xml.Formatters
{
    public abstract class JsonYamlFormatterBase : StreamFormatter
    {
        protected readonly IReadOnlyList<string> outputPrefix;
        protected readonly IQualifiedNameMatchDictionary<string> keys;
        protected readonly IQualifiedNameMatchList arrays;
        protected readonly IQualifiedNameMatchList strings;

        public JsonYamlFormatterBase(
            Func<Stream> outputStreamFactory,
            IReadOnlyList<string> outputPrefix,
            IQualifiedNameMatchDictionary<string> keys,
            IQualifiedNameMatchList arrays,
            IQualifiedNameMatchList strings,
            ILogger<JsonYamlFormatterBase> logger) : base(outputStreamFactory, logger)
        {
            this.outputPrefix = outputPrefix;
            this.keys = keys;
            this.arrays = arrays;
            this.strings = strings;
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

            if (TimeSpan.TryParse(value, out TimeSpan t))
                return (t, true);

            if (DateTime.TryParse(value, out DateTime dt))
                return (dt, true);

            return (null, false);
        }
    }
}
