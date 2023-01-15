using Namespace2Xml.Semantics;
using Namespace2Xml.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Namespace2Xml
{
    public class QualifiedNameMatchList : IQualifiedNameMatchList
    {
        private readonly HashSet<string> strict = new();
        private readonly Dictionary<string, QualifiedNameMatchList> nested = new();
        private readonly Dictionary<string, (string keyTextPrefix, string keyTextSuffix, NamePart middlePattern, QualifiedNameMatchList nested)> nonStrict = new();

        public QualifiedNameMatchList() { }

        public QualifiedNameMatchList(IEnumerable<QualifiedName> values)
        {
            foreach (var value in values)
                Add(value);
        }

        public void Add(QualifiedName key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (key.Parts.Count == 0)
                throw new ArgumentException("The key is empty", nameof(key));

            var keyHead = key.Parts[0];

            if (keyHead.IsTextOnly())
            {
                if (key.IsTextOnly())
                    strict.Add(key.ToString());
                else
                {
                    var keyTail = new QualifiedName(key.Parts.Skip(1));

                    if (!nested.TryGetValue(keyHead.ToString(), out var nestedValue))
                    {
                        nestedValue = new();
                        nested.Add(keyHead.ToString(), nestedValue);
                    }

                    nestedValue.Add(keyTail);
                }
            }
            else
            {
                var keyTail = new QualifiedName(key.Parts.Skip(1));

                if (!nonStrict.TryGetValue(keyHead.ToString(), out var nonStrictValue))
                {
                    nonStrictValue = (string.Join("", keyHead.Tokens.TakeWhile(t => t is TextNameToken)),
                        string.Join("", keyHead.Tokens.Reverse().TakeWhile(t => t is TextNameToken).Reverse()),
                        new NamePart(keyHead.Tokens.SkipWhile(t => t is TextNameToken).Reverse().SkipWhile(t => t is TextNameToken).Reverse()),
                        new QualifiedNameMatchList());
                    nonStrict.Add(keyHead.ToString(), nonStrictValue);
                }

                nonStrictValue.nested.Add(keyTail);
            }
        }

        public bool IsMatch(QualifiedName key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (key.Parts.Count == 0)
                throw new ArgumentException("The key is empty", nameof(key));

            if (strict.Contains(key.ToString()))
                return true;

            var keyHead = key.Parts[0].ToString();
            var keyTail = new QualifiedName(key.Parts.Skip(1));

            if (nested.TryGetValue(keyHead, out var nestedValue) && nestedValue.IsMatch(keyTail))
                return true;

            foreach ((var prefix, var suffix, var middlePattern, var nested) in nonStrict.Values)
                if (keyHead.StartsWith(prefix) &&
                    keyHead.EndsWith(suffix) &&
                    middlePattern.IsMatch(keyHead.Substring(prefix.Length, keyHead.Length - prefix.Length - suffix.Length)) &&
                    nested.IsMatch(keyTail))
                    return true;

            return false;
        }
    }

    public interface IQualifiedNameMatchList
    {
        bool IsMatch(QualifiedName key);
    }
}
