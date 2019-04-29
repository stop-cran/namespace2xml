using Namespace2Xml.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace Namespace2Xml
{
    public static class MoreEnumerable
    {
        public static IEnumerable<IEnumerable<T>> Cartesian<T>(this IEnumerable<IEnumerable<T>> source) =>
                source.Any()
                ? source.Skip(1).Any()
                ? from head in source.First()
                  from tail in Cartesian(source.Skip(1))
                  select new[] { head }.Concat(tail)
                : source.First().Select(item => new[] { item })
                : Enumerable.Empty<IEnumerable<T>>();

        public static IEnumerable<IEnumerable<T>> SequenceDistinct<T>(this IEnumerable<IEnumerable<T>> source) =>
            source.Distinct(new SequenceEqualityComparer<T>());

        public static bool InsertAfterIfNotExists(this IList<IProfileEntry> entries, Payload originalItem, Payload newItem)
        {
            if (ReferenceEquals(originalItem, newItem) ||
                entries.OfType<Payload>().Any(entry =>
                    entry.Name.Equals(newItem.Name) &&
                    entry.Value.SequenceEqual(newItem.Value)))
                return false;

            entries.Insert(entries.IndexOf(originalItem) + 1, newItem);
            return true;
        }


        private class SequenceEqualityComparer<T> : EqualityComparer<IEnumerable<T>>
        {
            public override bool Equals(IEnumerable<T> x, IEnumerable<T> y) =>
                x.SequenceEqual(y);

            public override int GetHashCode(IEnumerable<T> obj) =>
                obj.Aggregate(1049, (seed, item) => seed + 397 * item.GetHashCode());
        }
    }
}
