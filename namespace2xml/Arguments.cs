using CommandLine;
using System.Collections.Generic;

namespace Namespace2Xml
{
    public class Arguments
    {
        public Arguments(
            IReadOnlyList<string> inputs,
            IReadOnlyList<string> schemes,
            string outputDirectory)
        {
            Inputs = inputs;
            Schemes = schemes;
            OutputDirectory = outputDirectory;
        }

        [Option('i', "input", Required = true, Separator = ' ', HelpText = "One or more input files in the namespace format.")]
        public IReadOnlyList<string> Inputs { get; }

        [Option('s', "scheme", Required = true, Separator = ' ', HelpText = "One or more scheme files.")]
        public IReadOnlyList<string> Schemes { get; }

        [Option('o', "output", Required = true, HelpText = "Output directory.")]
        public string OutputDirectory { get; }
    }
}
