using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Namespace2Xml.Scheme;
using System.IO;

namespace Namespace2Xml.Formatters
{
    public class FileStreamFactory : IStreamFactory
    {
        private readonly string baseOutputDirectory;
        private readonly ILogger<FileStreamFactory> logger;

        public FileStreamFactory(IOptions<FileStreamFactoryOptions> options, ILogger<FileStreamFactory> logger)
        {
            this.baseOutputDirectory = Path.GetFullPath(options.Value.BaseOutputDirectory);
            this.logger = logger;
        }

        public Stream CreateInputStream(string name)
        {
            var fileName = Path.GetFullPath(name);

            logger.LogInformation("Reading input {0}...", fileName);

            return new FileStream(fileName, FileMode.Open, FileAccess.Read);
        }

        public Stream CreateOutputStream(string name, OutputType outputType)
        {
            var fileName = Path.IsPathRooted(name) ? name : Path.Combine(baseOutputDirectory, name);

            logger.LogInformation("Writing output {0} {1}...", fileName, outputType);

            Directory.CreateDirectory(Path.GetDirectoryName(fileName));

            return new FileStream(fileName, FileMode.Create, FileAccess.Write);
        }
    }

    public class FileStreamFactoryOptions
    {
        public string BaseOutputDirectory { get; set; }
    }
}
