using Namespace2Xml.Scheme;
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

namespace Namespace2Xml.Formatters
{
    public class XmlFormatter : StreamFormatter
    {
        private readonly IReadOnlyList<string> outputPrefix;
        private readonly XmlOptions xmlOptions;
        private readonly IReadOnlyDictionary<QualifiedName, string> keys;
        private readonly HashSet<QualifiedName> hiddenKeys;
        private readonly HashSet<QualifiedName> csvArrays;
        private readonly HashSet<QualifiedName> xmlElements;

        public XmlFormatter(
            Func<Stream> outputStreamFactory,
            IReadOnlyList<string> outputPrefix,
            XmlOptions xmlOptions,
            IReadOnlyDictionary<QualifiedName, string> keys,
            IReadOnlyList<QualifiedName> hiddenKeys,
            IReadOnlyList<QualifiedName> csvArrays,
            IReadOnlyList<QualifiedName> xmlElements)
            : base(outputStreamFactory)
        {
            this.outputPrefix = outputPrefix;
            this.xmlOptions = xmlOptions;
            this.keys = keys;
            this.hiddenKeys = new HashSet<QualifiedName>(hiddenKeys);
            this.csvArrays = new HashSet<QualifiedName>(csvArrays);
            this.xmlElements = new HashSet<QualifiedName>(xmlElements);
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

        protected override async Task DoWrite(ProfileTree tree, Stream stream, CancellationToken cancellationToken)
        {
            var xmlNamespaces = (from pair in tree.GetLeafs()
                                 where pair.leaf.NameString.StartsWith("xmlns:")
                                 select new { Key = pair.leaf.NameString.Substring(6), pair.leaf.Value })
                                 .ToDictionary(x => x.Key, x => XNamespace.Get(x.Value));

            using (var writer = new StreamWriter(stream))
            using (var xmlWriter = XmlWriter.Create(writer, new XmlWriterSettings
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

            return csvArrays.Contains(childPrefix.ToQualifiedName())
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

            return xmlElements.Contains(childPrefix.ToQualifiedName())
                ? new XElement(xName, value)
                : (XObject)new XAttribute(xName, value);
        }

        private (XObject node, SourceMark firstSourceMark)[] ToXml(ProfileTree tree1, string[] prefix, string parentXmlns,
            IReadOnlyDictionary<string, XNamespace> xmlNamespaces)
        {
            if (tree1 is ProfileTreeLeaf leaf)
                return ToXmlValue(leaf.NameString, leaf.Value, prefix, xmlNamespaces)
                    .Select(obj => (obj, leaf.SourceMark))
                    .ToArray();

            var tree = (ProfileTreeNode)tree1;

            var xmlns = tree?.Children
                .OfType<ProfileTreeLeaf>()
                .SingleOrDefault(child => child.NameString == "xmlns")
                ?.Value ?? parentXmlns;

            var newPrefix = prefix.Concat(new[] { tree.NameString }).ToArray();
            IEnumerable<(XObject node, SourceMark firstSourceMark)> nodes;
            var comments = tree.Children.OfType<ProfileTreeLeaf>()
                .SelectMany(l => l.LeadingComments
                    .Select<Comment, (XObject node, SourceMark firstSourceMark)>(comment => (new XComment(comment.Text), l.SourceMark)));
            var leafs = tree.Children
                .OfType<ProfileTreeLeaf>()
                .Where(child => child.NameString != "xmlns")
                .SelectMany(child => ToXml(child, prefix, xmlns, xmlNamespaces));

            bool wrap = false;

            if (keys.TryGetValue(newPrefix.ToQualifiedName(), out var key))
                nodes = tree.Children
                    .OfType<ProfileTreeNode>()
                    .SelectMany(child =>
                    {
                        var xx = ToXml(new ProfileTreeNode(tree.Name, child.Children),
                            newPrefix, xmlns, xmlNamespaces);
                        var elem = xx.Select(pair => pair.node).OfType<XElement>().Single();

                        foreach (var item in ToXmlValue(key, child.NameString, newPrefix, xmlNamespaces))
                            if (item is XAttribute attr)
                            {
                                var attibutes = elem.Attributes().ToList();

                                attibutes.Insert(0, attr);

                                elem.ReplaceAttributes(attibutes);
                            }
                            else
                                elem.AddFirst(item);

                        return xx;
                    });
            else if (hiddenKeys.Contains(newPrefix.ToQualifiedName()))
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
                    .OrderBy(pair => pair.firstSourceMark))
                .ToArray();

            return wrap ? new[]
            {
                ((XObject)new XElement(
                string.IsNullOrEmpty(xmlns) ? XName.Get(tree.NameString) : XName.Get(tree.NameString, xmlns),
                content.Select(pair => pair.node).ToArray<object>()), content.First().firstSourceMark)
            } : content;
        }
    }
}
