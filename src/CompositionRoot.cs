using log4net;
using Namespace2Xml.Formatters;
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

            await Task.WhenAll(
                from scheme in treeBuilder.BuildScheme(
                    schemes,
                    input.OfType<Payload>()
                        .Select(p => p.Name)
                        .Distinct())
                from pair in formatterBuilder.Build(scheme)
                from tree in treeBuilder.Build(input, scheme.GetSubstituteTypes())
                from subTree in tree.GetSubTrees(pair.prefix) // RK TODO: Logging if no suitable subtrees.
                select pair.formatter.Write(subTree, cancellationToken));
        }
    }
}
