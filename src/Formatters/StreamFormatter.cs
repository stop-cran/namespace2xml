using log4net;
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
        private readonly ILog logger;
        protected readonly Func<Stream> outputStreamFactory;

        protected StreamFormatter(Func<Stream> outputStreamFactory)
        {
            this.outputStreamFactory = outputStreamFactory;
            logger = LogManager.GetLogger(GetType()); // RK TODO: Inject
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
                    logger.Error(new
                    {
                        message = "Error reading input",
                        error = error.Error,
                        fileName = error.SourceMark.FileName,
                        line = error.SourceMark.LineNumber
                    });

                throw new ApplicationException();
            }

            using (var stream = outputStreamFactory())
                await DoWrite(tree, stream, cancellationToken);
        }

        protected abstract Task DoWrite(ProfileTree tree, Stream stream, CancellationToken cancellationToken);
    }
}
