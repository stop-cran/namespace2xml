using log4net.Core;
using log4net.Layout.Pattern;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Namespace2Xml
{
    public class MessageObjectConverter : PatternLayoutConverter
    {
        private readonly ConcurrentDictionary<Type, Func<object, string>> cache =
            new ConcurrentDictionary<Type, Func<object, string>>();

        private readonly ParameterExpression objectParameter = Expression.Parameter(typeof(object));

        protected override void Convert(TextWriter writer, LoggingEvent loggingEvent) =>
            writer.Write(
                cache.GetOrAdd(
                    loggingEvent.MessageObject.GetType(),
                    GetFormatter)
                    (loggingEvent.MessageObject));

        private Func<object, string> GetFormatter(Type type)
        {
            if (type == typeof(string))
                return obj => obj as string;

            var properties = type.GetProperties().Where(p => p.GetIndexParameters().Length == 0);
            var messageProperty = properties.SingleOrDefault(p => p.Name == "message");
            Func<object, string> messageFormatter = null;
            var formatters = properties
                .Except(new[] { messageProperty })
                .Select(p => new { p.Name, Format = GetFormatter(type, p) })
                .ToList();

            if (messageProperty != null)
                messageFormatter = message => GetFormatter(type, messageProperty)(message) + "\t";

            return message => messageFormatter?.Invoke(message) + string.Join("\t",
                from formatter in formatters
                let formattedMessage = formatter.Format(message)
                where formattedMessage != null
                select $"{formatter.Name}: {formattedMessage}");
        }

        private Func<object, object> GetFormatter(Type type, PropertyInfo property) =>
            (Func<object, object>)Expression.Lambda(
                Expression.TypeAs(
                    Expression.Property(
                        Expression.TypeAs(objectParameter, type),
                        property),
                    typeof(object)),
                objectParameter).Compile();
    }
}
