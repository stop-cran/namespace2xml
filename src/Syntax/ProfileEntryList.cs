using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Namespace2Xml.Syntax
{
    public sealed class ProfileEntryList : IReadOnlyList<IProfileEntry>
    {
        private readonly List<IProfileEntry> content;
        private readonly Dictionary<QualifiedName, List<Payload>> payloadsByName;

        public ProfileEntryList(IEnumerable<IProfileEntry> items)
        {
            content = items.ToList();
            payloadsByName = content.OfType<Payload>().GroupBy(p => p.Name).ToDictionary(g => g.Key, g => g.ToList());
        }

        public IProfileEntry this[int index] { get => content[index]; set => content[index] = value; }

        public int Count => content.Count;

        public bool Contains(IProfileEntry item) => (!(item is Payload p) || payloadsByName.ContainsKey(p.Name)) && content.Contains(item);

        public bool ContainsPayload(Payload p) => payloadsByName.TryGetValue(p.Name, out var payload) &&
            payload.Any(pp => pp.Value.SequenceEqual(p.Value));

        public IEnumerator<IProfileEntry> GetEnumerator() => content.GetEnumerator();

        public int IndexOf(IProfileEntry item) => content.IndexOf(item);

        public void Insert(int index, IProfileEntry item)
        {
            content.Insert(index, item);
            if (item is Payload p)
                if (payloadsByName.TryGetValue(p.Name, out var l))
                    l.Add(p);
                else
                    payloadsByName.Add(p.Name, new List<Payload> { p });
        }

        public void Remove(IProfileEntry item)
        {
            content.Remove(item);
            if (item is Payload payload)
            {
                payloadsByName.Remove(payload.Name);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => content.GetEnumerator();
    }
}
