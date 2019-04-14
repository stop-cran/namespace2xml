using CommandLine;
using Namespace2Xml.Scheme;
using Namespace2Xml.Semantics;
using Namespace2Xml.Syntax;
using Sprache;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Namespace2Xml
{
    public static class Program
    {
        public static async Task<int> Main(string[] args) =>
            await Parser.Default
                .ParseArguments<Arguments>(args)
                .MapResult(arguments => DoWork(arguments, default),
                errors => Task.FromResult(1));

        public static async Task<int> DoWork(Arguments arguments, CancellationToken token)
        {
            var builder = new TreeBuilder();
            var trees = (await Task.WhenAll(arguments
                            .Inputs
                            .Select(input => File.ReadAllTextAsync(input, token))))
                            .Select(entries => Parsers.Profile.TryParse(entries))
                            .ToList();
            var treeValues = trees.SelectMany(tree => tree.Value).ToList();
            var semanticTree = builder.Build(treeValues);

            var schemeTrees = (await Task.WhenAll(arguments
                            .Schemes
                            .Select(input => File.ReadAllTextAsync(input, token))))
                            .Select(entries => Parsers.Profile.TryParse(entries))
                            .ToList();
            var schemeTreesValues = schemeTrees.SelectMany(tree => tree.Value).ToList();

            await Task.WhenAll(
                from pair in new WriterBuilder(
                    Path.GetFullPath(arguments.OutputDirectory))
                    .Build(builder
                        .Preprocess(schemeTreesValues)
                        .OfType<Payload>())
                let tree = semanticTree
                    .Select(t => t.GetSubTree(pair.prefix))
                    .SingleOrDefault(t => t != null)
                where tree != null // RK TODO: Logging if tree == null (no suitable input).
                select pair.writer.Write(tree, token));

            return 0;
        }
    }
}
