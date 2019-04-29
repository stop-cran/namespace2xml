using Namespace2Xml.Syntax;
using System;
using System.Collections.Generic;
using System.IO;

namespace Namespace2Xml.Formatters
{
    public abstract class JsonYamlFormatterBase : StreamFormatter
    {
        protected readonly IReadOnlyList<string> outputPrefix;
        protected readonly IReadOnlyDictionary<QualifiedName, string> keys;
        protected readonly HashSet<QualifiedName> hiddenKeys;
        protected readonly HashSet<QualifiedName> csvArrays;
        protected readonly HashSet<QualifiedName> strings;

        public JsonYamlFormatterBase(
            Func<Stream> outputStreamFactory,
            IReadOnlyList<string> outputPrefix,
            IReadOnlyDictionary<QualifiedName, string> keys,
            IReadOnlyList<QualifiedName> hiddenKeys,
            IReadOnlyList<QualifiedName> csvArrays,
            IReadOnlyList<QualifiedName> strings) : base(outputStreamFactory)
        {
            this.outputPrefix = outputPrefix;
            this.keys = keys;
            this.hiddenKeys = new HashSet<QualifiedName>(hiddenKeys);
            this.csvArrays = new HashSet<QualifiedName>(csvArrays);
            this.strings = new HashSet<QualifiedName>(strings);
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
