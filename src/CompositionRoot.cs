using log4net;
using Namespace2Xml.Formatters;
using Namespace2Xml.Scheme;
using Namespace2Xml.Semantics;
using Namespace2Xml.Syntax;
using Sprache;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Namespace2Xml
{
    public class CompositionRoot
    {
        private readonly IProfileReader profileReader;
        private readonly ITreeBuilder treeBuilder;
        private readonly IFormatterBuilder formatterBuilder;
        private readonly ILog logger;

        public CompositionRoot(
            IProfileReader profileReader,
            ITreeBuilder treeBuilder,
            IFormatterBuilder formatterBuilder,
            ILog logger)
        {
            this.profileReader = profileReader;
            this.treeBuilder = treeBuilder;
            this.formatterBuilder = formatterBuilder;
            this.logger = logger;
        }

        public async Task Write(Arguments arguments, CancellationToken cancellationToken)
        {
            var profiles = await profileReader.ReadFiles(arguments.Inputs, cancellationToken);
            var input = profiles.Concat(profileReader.ReadVariables(arguments.Variables));
            var schemes = await profileReader.ReadFiles(arguments.Schemes, cancellationToken);
            var usedNames = input.OfType<Payload>()
                        .Select(p => p.Name)
                        .Distinct()
                        .ToList();

            logger.Info("Analyzing...");

            var treesAndSchemes = await Task.WhenAll(
                treeBuilder.BuildScheme(schemes, usedNames)
                .Select(scheme => Task.Run(() => (scheme, trees: treeBuilder.Build(input, scheme.GetSubstituteTypes())))));

            await Task.WhenAll(
                from treesAndScheme in treesAndSchemes
                from tree in treesAndScheme.trees
                from alteredScheme in treeBuilder.BuildScheme(
                    treesAndScheme.scheme.WithImplicitHiddenKeys(tree),
                    usedNames)
                from pair in formatterBuilder.Build(alteredScheme)
                from subTree in tree.GetSubTrees(pair.prefix) // RK TODO: Logging if no suitable subtrees.
                select pair.formatter.Write(subTree, cancellationToken));
        }
    }
}
