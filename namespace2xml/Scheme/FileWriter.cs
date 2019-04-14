using Namespace2Xml.Semantics;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Namespace2Xml.Scheme
{
    public abstract class FileWriter : IOutputWriter
    {
        protected readonly string fileName;

        protected FileWriter(string fileName)
        {
            this.fileName = fileName;
        }

        public async Task Write(ProfileTree tree, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var errors = tree
                .GetErrors()
                .OrderBy(pair => pair.lineNumber)
                .ToList();

            if (errors.Count > 0)
            {
                // RK TODO: log errors.
                throw new ApplicationException();
            }

            var dir = Path.GetDirectoryName(fileName);

            if (!string.IsNullOrEmpty(dir.Trim(Path.DirectorySeparatorChar).Trim(Path.AltDirectorySeparatorChar)))
                Directory.CreateDirectory(dir);

            await DoWrite(tree, cancellationToken);
        }

        protected abstract Task DoWrite(ProfileTree tree, CancellationToken cancellationToken);
    }
}
