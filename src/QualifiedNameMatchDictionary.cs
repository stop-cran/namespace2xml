using Namespace2Xml.Semantics;
using Namespace2Xml.Syntax;
using NullGuard;
using System;
using System.Collections.Generic;
using System.Linq;
using MoreLinq;
using Namespace2Xml.Formatters;

namespace Namespace2Xml
{
    public class QualifiedNameMatchDictionary<T> : IQualifiedNameMatchDictionary<T>
    {
        private readonly Dictionary<QualifiedName, T> strict = new();
        private readonly Dictionary<string, QualifiedNameMatchDictionary<T>> nested = new();
        private readonly Dictionary<string, (string keyTextPrefix, string keyTextSuffix, NamePart middlePattern, QualifiedNameMatchDictionary<T> nested)> nonStrict = new();

        public QualifiedNameMatchDictionary() { }

        public QualifiedNameMatchDictionary(IEnumerable<KeyValuePair<QualifiedName, T>> values)
        {
            foreach ((var key, var value) in values)
                Add(key, value);
        }

        public void Add(QualifiedName key, T value)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (key.Parts.Count == 0)
                throw new ArgumentException("The key is empty", nameof(key));

            var keyHead = key.Parts[0];

            if (keyHead.IsTextOnly())
            {
                if (key.IsTextOnly())
                    strict.Add(key, value);
                else
                {
                    var keyTail = new QualifiedName(key.Parts.Skip(1));

                    if (!nested.TryGetValue(keyHead.ToString(), out var nestedValue))
                    {
                        nestedValue = new QualifiedNameMatchDictionary<T>();
                        nested.Add(keyHead.ToString(), nestedValue);
                    }

                    nestedValue.Add(keyTail, value);
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
                        new QualifiedNameMatchDictionary<T>());
                    nonStrict.Add(keyHead.ToString(), nonStrictValue);
                }

                nonStrictValue.nested.Add(keyTail, value);
            }
        }

        public bool TryMatch(QualifiedName key, [AllowNull] out T value)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (key.Parts.Count == 0)
            {
                value = default;
                return false;
            }

            if (strict.TryGetValue(key, out value))
                return true;

            var keyHead = key.Parts[0].ToString();
            var keyTail = new QualifiedName(key.Parts.Skip(1));

            if (nested.TryGetValue(keyHead, out var nestedValue) && nestedValue.TryMatch(keyTail, out value))
                return true;

            foreach ((var prefix, var suffix, var middlePattern, var nested) in nonStrict.Values)
                if (keyHead.StartsWith(prefix) &&
                    keyHead.EndsWith(suffix) &&
                    middlePattern.IsMatch(keyHead.Substring(prefix.Length, keyHead.Length - prefix.Length - suffix.Length)) &&
                    nested.TryMatch(keyTail, out value))
                    return true;

            if (key.HasSubstitute() && strict.Keys.Any())
            {
                foreach (var kvp in strict)
                {
                    var strictName = kvp.Key;
                    var match = strictName.GetFullMatch(key);
                    if (match != null)
                    {
                        value = kvp.Value;
                        return true;
                    }
                }
            }

            return false;
        }
    }

    public interface IQualifiedNameMatchDictionary<T>
    {
        bool TryMatch(QualifiedName key, out T value);
    }
}
