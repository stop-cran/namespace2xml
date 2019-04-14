using Namespace2Xml.Semantics;
using Namespace2Xml.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Namespace2Xml.Scheme
{
    public class XmlWriter : FileWriter
    {
        private readonly IReadOnlyList<string> outputPrefix;
        private readonly XmlOptions xmlOptions;
        private readonly IReadOnlyDictionary<QualifiedName, string> keys;
        private readonly IReadOnlyList<QualifiedName> hiddenKeys;
        private readonly IReadOnlyList<QualifiedName> csvArrays;
        private readonly IReadOnlyList<QualifiedName> xmlElements;

        public XmlWriter(
            string fileName,
            IReadOnlyList<string> outputPrefix,
            XmlOptions xmlOptions,
            IReadOnlyDictionary<QualifiedName, string> keys,
            IReadOnlyList<QualifiedName> hiddenKeys,
            IReadOnlyList<QualifiedName> csvArrays,
            IReadOnlyList<QualifiedName> xmlElements)
            : base(fileName)
        {
            this.outputPrefix = outputPrefix;
            this.xmlOptions = xmlOptions;
            this.keys = keys;
            this.hiddenKeys = hiddenKeys;
            this.csvArrays = csvArrays;
            this.xmlElements = xmlElements;
        }

        private XElement ApplyOutputPrefix(XElement element)
        {
            if (outputPrefix.Count == 0)
                return element;

            element.Name = string.IsNullOrEmpty(element.Name.NamespaceName)
                ? XName.Get(outputPrefix.Last())
                : XName.Get(outputPrefix.Last(), element.Name.NamespaceName);

            foreach (var prefix in outputPrefix.Reverse().Skip(1))
                element = new XElement(prefix, new object[] { element });

            return element;
        }

        protected override async Task DoWrite(ProfileTree tree, CancellationToken cancellationToken)
        {
            var xmlNamespaces = (from pair in tree.GetLeafs()
                                 let parts = pair.leaf.Name.Split(':')
                                 where parts.Length == 2 && parts[0] == "xmlns"
                                 select new { Key = parts[1], pair.leaf.Value })
                                 .ToDictionary(x => x.Key, x => XNamespace.Get(x.Value));

            using (var file = new FileStream(fileName, FileMode.Create, FileAccess.Write))
            using (var writer = new StreamWriter(file))
            using (var xmlWriter = System.Xml.XmlWriter.Create(writer, new XmlWriterSettings
            {
                Async = true,
                Indent = (xmlOptions & XmlOptions.NoIndent) == XmlOptions.None,
                NewLineOnAttributes = (xmlOptions & XmlOptions.NewLineOnAttributes) == XmlOptions.NewLineOnAttributes
            }))
                await ApplyOutputPrefix(
                        (XElement)ToXml(tree, new string[0], "", xmlNamespaces)
                        .Single()
                        .node)
                    .WriteToAsync(xmlWriter, cancellationToken);
        }

        private IEnumerable<XObject> ToXmlValue(string name, string value, string[] prefix,
            IReadOnlyDictionary<string, XNamespace> xmlNamespaces)
        {
            var childPrefix = prefix.Concat(new[] { name }).ToArray();

            return csvArrays.IsMatch(childPrefix)
                ? value.Split(",").Select(part => ToXmlValueSingle(name, part, prefix, xmlNamespaces))
                : new[] { ToXmlValueSingle(name, value, prefix, xmlNamespaces) };
        }

        private XObject ToXmlValueSingle(string name, string value, string[] prefix,
            IReadOnlyDictionary<string, XNamespace> xmlNamespaces)
        {
            var childPrefix = prefix.Concat(new[] { name }).ToArray();

            XName xName;

            if (name.StartsWith("xmlns:"))
                xName = XNamespace.Xmlns + name.Substring(6);
            else if (name.Contains(':'))
            {
                var parts = name.Split(':');

                xName = xmlNamespaces[parts[0]] + parts[1];
            }
            else
                xName = name;

            return xmlElements.IsMatch(childPrefix.ToArray())
                ? new XElement(xName, value)
                : (XObject)new XAttribute(xName, value);
        }

        private (XObject node, int firstLineNumber)[] ToXml(ProfileTree tree1, string[] prefix, string parentXmlns,
            IReadOnlyDictionary<string, XNamespace> xmlNamespaces)
        {
            if (tree1 is ProfileTreeLeaf leaf)
                return ToXmlValue(leaf.Name, leaf.Value, prefix, xmlNamespaces)
                    .Select(obj => (obj, leaf.LineNumber))
                    .ToArray();

            var tree = (ProfileTreeNode)tree1;

            var xmlns = tree?.Children
                .OfType<ProfileTreeLeaf>()
                .SingleOrDefault(child => child.Name == "xmlns")
                ?.Value ?? parentXmlns;

            var newPrefix = prefix.Concat(new[] { tree.Name }).ToArray();
            IEnumerable<(XObject node, int firstLineNumber)> nodes;
            var comments = tree.Children.OfType<ProfileTreeLeaf>()
                .SelectMany(l => l.LeadingComments
                    .Select<string, (XObject node, int firstLineNumber)>(comment => (new XComment(comment), l.LineNumber)));
            var leafs = tree.Children
                .OfType<ProfileTreeLeaf>()
                .Where(child => child.Name != "xmlns")
                .SelectMany(child => ToXml(child, prefix, xmlns, xmlNamespaces));

            var key = keys.GetMatch(newPrefix);
            bool wrap = false;

            if (key != null)
                nodes = tree.Children
                    .OfType<ProfileTreeNode>()
                    .SelectMany(child =>
                    {
                        var xx = ToXml(new ProfileTreeNode(tree.Name, child.Children),
                            newPrefix, xmlns, xmlNamespaces);
                        var elem = xx.Select(pair => pair.node).OfType<XElement>().Single();

                        foreach (var item in ToXmlValue(key, child.Name, newPrefix, xmlNamespaces))
                            elem.Add(item);

                        return xx;
                    });
            else if (hiddenKeys.IsMatch(newPrefix))
                nodes = tree.Children
                    .OfType<ProfileTreeNode>()
                    .SelectMany(child =>
                        ToXml(new ProfileTreeNode(tree.Name, child.Children),
                            newPrefix, xmlns, xmlNamespaces));
            else
            {
                nodes = tree.Children
                    .OfType<ProfileTreeNode>()
                    .SelectMany(child => ToXml(child, newPrefix, xmlns, xmlNamespaces));
                wrap = true;
            }

            var content =
                comments
                .Concat(nodes
                    .Concat(leafs)
                    .OrderBy(pair => pair.firstLineNumber))
                .ToArray();

            return wrap ? new[]
            {
                ((XObject)new XElement(
                string.IsNullOrEmpty(xmlns) ? XName.Get(tree.Name) : XName.Get(tree.Name, xmlns),
                content.Select(pair => pair.node).ToArray<object>()), content.First().firstLineNumber)
            } : content;
        }
    }
}
