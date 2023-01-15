using CommandLine;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Namespace2Xml
{
    public class Arguments
    {
        private static readonly IReadOnlyList<string> Emtpy = new List<string>().AsReadOnly();
        public Arguments(
            [NullGuard.AllowNull]IReadOnlyList<string> inputs,
            [NullGuard.AllowNull]IReadOnlyList<string> schemes,
            [NullGuard.AllowNull]string outputDirectory,
            [NullGuard.AllowNull]string verbosity,
            [NullGuard.AllowNull]IReadOnlyList<string> variables)
        {
            Inputs = inputs ?? Emtpy;
            Schemes = schemes ?? Emtpy;
            OutputDirectory = outputDirectory ?? ".";
            Verbosity = verbosity;

            if (verbosity == null)
                LoggingLevel = LogLevel.Information;
            else
            {
                var levelField = typeof(LogLevel)
                    .GetFields(BindingFlags.Static | BindingFlags.Public)
                    .SingleOrDefault(level => level.Name.ToLowerInvariant() == verbosity);

                if (levelField == null)
                    throw new ArgumentException(nameof(verbosity));

                LoggingLevel = (LogLevel)levelField.GetValue(null);
            }

            Variables = variables ?? Emtpy;
        }

        [Option('i', "input", Required = true, Separator = ' ', HelpText = "One or more input files in the namespace format.")]
        public IReadOnlyList<string> Inputs { get; }

        [Option('s', "scheme", Required = true, Separator = ' ', HelpText = "One or more scheme files.")]
        public IReadOnlyList<string> Schemes { get; }

        [Option('o', "output", HelpText = "Output directory.")]
        public string OutputDirectory { get; }

        [NullGuard.AllowNull]
        [Option("verbosity", HelpText = "Logging verbosity - debug, information, warning or error.")]
        public string Verbosity { get; }

        public LogLevel LoggingLevel { get; }

        [Option('v', "variables", Separator = ' ', HelpText = "Additional namespace-formatted entries to override the input.")]
        public IReadOnlyList<string> Variables { get; }
    }
}
