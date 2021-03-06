﻿using Namespace2Xml;
using Namespace2Xml.Semantics;
using Namespace2Xml.Syntax;
using System.Linq;

namespace Namespace2Xml.Tests
{
    public static class Helpers
    {
        public static ProfileTree ToTree(object value)
        {
            var p = value.GetType()
                .GetProperties()
                .Single();

            return ToTree(p.Name, p.GetValue(value));
        }

        private static ProfileTree ToTree(string name, object value) =>
            value is string s
            ? (ProfileTree)new ProfileTreeLeaf(
                CreatePayload(name, s),
                Enumerable.Empty<Comment>(),
                QualifiedName.Empty)
            : new ProfileTreeNode(
                name.ToNamePart(),
                value.GetType().GetProperties().Select(p => ToTree(p.Name, p.GetValue(value))));

        public static NamePart ToNamePart(this string name) =>
            new NamePart(new[] { new TextNameToken(name) });


        public static Payload CreatePayload(string name, string value) =>
            new Payload(new QualifiedName(new[] { name.ToNamePart() }), new[] { new TextValueToken(value) }, new SourceMark(0, "<test>", 1));
    }
}
