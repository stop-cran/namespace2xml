using log4net;
using Namespace2Xml.Formatters;
using Namespace2Xml.Semantics;
using Namespace2Xml.Syntax;
using Sprache;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Namespace2Xml
{
    public class ProfileReader : IProfileReader
    {
        private readonly IStreamFactory streamFactory;
        private readonly ILog logger;

        public ProfileReader(
            IStreamFactory streamFactory,
            ILog logger)
        {
            this.streamFactory = streamFactory;
            this.logger = logger;
        }

        public IReadOnlyList<IProfileEntry> ReadVariables(
            IEnumerable<string> variables) =>
            CheckErrorsAndMerge(variables
                .Select(variable => TryParse(variable, int.MaxValue, "<command line>")));

        public async Task<IReadOnlyList<IProfileEntry>> ReadFiles(
            IEnumerable<string> files,
            CancellationToken cancellationToken) =>
            CheckErrorsAndMerge(
                await Task.WhenAll(
                    files.Select(ReadInput)));

        private (IResult<IEnumerable<IProfileEntry>> result, string fileName) TryParse(string input, int fileNumber, string fileName) =>
            (Parsers.GetProfile(fileNumber, fileName).TryParse(input), fileName);

        private IReadOnlyList<T> CheckErrorsAndMerge<T>(IEnumerable<(IResult<IEnumerable<T>> result, string fileName)> results)
        {
            var resultsList = results.ToList();

            var errors = from tuple in resultsList
                         where !tuple.result?.WasSuccessful ?? true
                         select tuple.result == null ? null :
                         new
                         {
                             message = "Error parsing input.",
                             error = tuple.result.Message,
                             tuple.fileName,
                             line = tuple.result.Remainder?.Line,
                             column = tuple.result.Remainder?.Column
                         };

            if (errors.Any())
            {
                foreach (var error in errors.Where(error => error != null))
                    logger.Error(error);
                throw new ApplicationException();
            }

            return resultsList
                .SelectMany(result => result.result.Value)
                .ToList();
        }

        private async Task<(IResult<IEnumerable<IProfileEntry>> result, string fileName)> ReadInput(string fileName, int fileNumber)
        {
            try
            {
                using (var stream = streamFactory.CreateInputStream(fileName))
                using (var reader = new StreamReader(stream))
                    return TryParse(await reader.ReadToEndAsync(), fileNumber, fileName); // RK TODO: cancellation support
            }
            catch (Exception ex)
            {
                logger.Error(new
                {
                    message = "Error reading input.",
                    fileName
                }, ex);

                throw new ApplicationException();
            }
        }
    }
}
