using Namespace2Xml.Syntax;
using System;
using System.Collections.Generic;

namespace Namespace2Xml.Scheme
{
    public abstract class JsonYamlWriterBase : FileWriter
    {
        protected readonly IReadOnlyList<string> outputPrefix;
        protected readonly IReadOnlyDictionary<QualifiedName, string> keys;
        protected readonly IReadOnlyList<QualifiedName> hiddenKeys;
        protected readonly IReadOnlyList<QualifiedName> csvArrays;
        protected readonly IReadOnlyList<QualifiedName> strings;

        public JsonYamlWriterBase(
            string fileName,
            [NullGuard.AllowNull] IReadOnlyList<string> outputPrefix,
            IReadOnlyDictionary<QualifiedName, string> keys,
            IReadOnlyList<QualifiedName> hiddenKeys,
            IReadOnlyList<QualifiedName> csvArrays,
            IReadOnlyList<QualifiedName> strings) : base(fileName)
        {
            this.outputPrefix = outputPrefix;
            this.keys = keys;
            this.hiddenKeys = hiddenKeys;
            this.csvArrays = csvArrays;
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
