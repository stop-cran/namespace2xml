using System;
using System.Collections.Generic;
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

        public CompositionRoot(
            IProfileReader profileReader,
            ITreeBuilder treeBuilder,
            IFormatterBuilder formatterBuilder)
        {
            this.profileReader = profileReader;
            this.treeBuilder = treeBuilder;
            this.formatterBuilder = formatterBuilder;
        }

        public async Task Write(Arguments arguments, CancellationToken cancellationToken)
        {
            var profiles = await profileReader.ReadFiles(arguments.Inputs, cancellationToken);
            var input = profiles.Concat(profileReader.ReadVariables(arguments.Variables));
            var schemes = await profileReader.ReadFiles(arguments.Schemes, cancellationToken);
            var usedNames = treeBuilder.ApplyNameSubstitutesLoop(input).OfType<Payload>()
                        .Select(p => p.Name)
                        .Distinct()
                        .ToList();

            // RK TODO: armTemplates.*.resources.type=array
            var schemeTrees = treeBuilder.BuildScheme(schemes, usedNames)
                .Where(x => x
                    .GetAllChildrenAndSelf()
                    .Select(y => y.entry as SchemeNode)
                    .Where(y => y != null)
                    .Any(y => !string.IsNullOrEmpty(y.SingleOrDefaultValue(EntryType.output)))).ToList();
            var resultsToWrite =
                from scheme in schemeTrees.AsParallel()
                from tree in treeBuilder.Build(input, scheme.GetSubstituteTypes())
                from alteredScheme in treeBuilder.BuildScheme(
                    scheme.WithImplicitArrays(tree),
                    usedNames)
                from pair in formatterBuilder.Build(alteredScheme)
                from subTree in tree.GetSubTrees(pair.prefix) // RK TODO: Logging if no suitable subtrees.
                select (tree: subTree, pair.formatter);

            foreach (var pair in resultsToWrite)
            {
                await pair.formatter.Write(pair.tree, cancellationToken);
            }
        }
    }
}
