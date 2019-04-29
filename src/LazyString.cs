using System;
using System.Collections.Generic;

namespace Namespace2Xml
{
    public sealed class LazyString
    {
        private readonly Lazy<string> lazyString;

        public LazyString(Func<string> stringFactory)
        {
            lazyString = new Lazy<string>(stringFactory);
        }

        public override string ToString() => lazyString.Value;


        public static LazyString Join<T>(string separator, IEnumerable<T> items) =>
            new LazyString(() => string.Join(separator, items));
    }
}
