using System;
using System.Collections.Generic;
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

        private readonly Dictionary<string, OutputType> outputFileNamesCache = new();

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

            Directory.CreateDirectory(Path.GetDirectoryName(fileName));

            switch (outputType)
            {
                case OutputType.@namespace:
                case OutputType.quotednamespace:
                case OutputType.yaml:
                    if (outputFileNamesCache.TryGetValue(fileName, out var cachedType))
                    {
                        if (cachedType == outputType)
                        {
                            logger.LogInformation("Appending output {0} {1}...", fileName, outputType);
                            return new FileStream(fileName, FileMode.Append, FileAccess.Write);
                        }

                        logger.LogInformation("Overriding output {0} {1}...", fileName, outputType);
                    }
                    else
                    {
                        logger.LogInformation("Writing output {0} {1}...", fileName, outputType);
                        outputFileNamesCache.Add(fileName, outputType);
                    }

                    return new FileStream(fileName, FileMode.Create, FileAccess.Write);
                default:
                    if (outputFileNamesCache.ContainsKey(fileName))
                        logger.LogInformation("Overriding output {0} {1}...", fileName, outputType);
                    else
                    {
                        logger.LogInformation("Writing output {0} {1}...", fileName, outputType);
                        outputFileNamesCache.Add(fileName, outputType);
                    }

                    return new FileStream(fileName, FileMode.Create, FileAccess.Write);
            }
        }
    }

    public class FileStreamFactoryOptions
    {
        public string BaseOutputDirectory { get; set; }
    }
}
