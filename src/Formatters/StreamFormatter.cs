using Microsoft.Extensions.Logging;
using Namespace2Xml.Semantics;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Namespace2Xml.Formatters
{
    public abstract class StreamFormatter : IFormatter
    {
        protected readonly ILogger<StreamFormatter> logger;
        protected readonly Func<Stream> outputStreamFactory;

        protected StreamFormatter(Func<Stream> outputStreamFactory, ILogger<StreamFormatter> logger)
        {
            this.outputStreamFactory = outputStreamFactory;
            this.logger = logger;
        }

        public async Task Write(ProfileTree tree, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var errors = tree
                .GetAllChildren()
                .Select(tuple => tuple.tree)
                .OfType<ProfileTreeError>()
                .OrderBy(pair => pair.SourceMark)
                .ToList();

            if (errors.Count > 0)
            {
                foreach (var error in errors)
                    logger.LogError("Error reading input: {0}, file: {1}, line: {2}",
                        error.Error,
                        error.SourceMark.FileName,
                        error.SourceMark.LineNumber);

                throw new ApplicationException();
            }

            using var stream = outputStreamFactory();
            await DoWrite(tree, stream, cancellationToken);
        }

        protected abstract Task DoWrite(ProfileTree tree, Stream stream, CancellationToken cancellationToken);
    }
}
