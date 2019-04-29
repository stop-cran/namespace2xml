using log4net;
using Namespace2Xml.Scheme;
using System.IO;

namespace Namespace2Xml.Formatters
{
    public class FileStreamFactory : IStreamFactory
    {
        private readonly string baseOutputDirectory;
        private readonly ILog logger;

        public FileStreamFactory(string baseOutputDirectory, ILog logger)
        {
            this.baseOutputDirectory = Path.GetFullPath(baseOutputDirectory);
            this.logger = logger;
        }

        public Stream CreateInputStream(string name)
        {
            var fileName = Path.GetFullPath(name);

            logger.Info(new
            {
                message = "Reading input...",
                fileName
            });

            return new FileStream(fileName, FileMode.Open, FileAccess.Read);
        }

        public Stream CreateOutputStream(string name, OutputType outputType)
        {
            var fileName = Path.IsPathRooted(name) ? name : Path.Combine(baseOutputDirectory, name);

            logger.Info(new
            {
                message = "Writing output...",
                fileName,
                outputType
            });

            Directory.CreateDirectory(Path.GetDirectoryName(fileName));

            return new FileStream(fileName, FileMode.Create, FileAccess.Write);
        }
    }
}
