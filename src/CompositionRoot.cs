using Microsoft.Extensions.Logging;
using Namespace2Xml.Formatters;
using Namespace2Xml.Scheme;
using Namespace2Xml.Semantics;
using Namespace2Xml.Syntax;
using Sprache;
using System.Collections.Generic;
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
        private readonly ILogger<CompositionRoot> logger;

        public CompositionRoot(
            IProfileReader profileReader,
            ITreeBuilder treeBuilder,
            IFormatterBuilder formatterBuilder,
            ILogger<CompositionRoot> logger)
        {
            this.profileReader = profileReader;
            this.treeBuilder = treeBuilder;
            this.formatterBuilder = formatterBuilder;
            this.logger = logger;
        }

        public async Task Write(Arguments arguments, CancellationToken cancellationToken)
        {
            var profiles = await profileReader.ReadFiles(arguments.Inputs, cancellationToken);
            var input = profiles.Concat(profileReader.ReadVariables(arguments.Variables)).ToList().AsReadOnly();
            var schemes = (await profileReader.ReadFiles(arguments.Schemes, cancellationToken))
                .WithIgnores(input);

            var outputNames = schemes
                .Where(x =>
                    x is Payload payload && payload.Name.Parts.Last().Tokens[0] is TextNameToken { Text: "output" })
                .Select(x => ((Payload)x).Name);
            var filteredInputs = new List<IProfileEntry>();
            foreach (var outputName in outputNames)
            {
                filteredInputs.AddRange(input
                    .Where(x =>
                        x is NamedProfileEntry namedProfileEntry
                        && namedProfileEntry.Name.Parts
                            .Zip(
                                outputName.Parts.SkipLast(1),
                                (profileNamePart, outputNamePart) =>
                                    profileNamePart.HasSubstitutes
                                    //|| profileNamePart.ToString() == outputNamePart.ToString())
                                    || outputNamePart.IsMatch(profileNamePart.ToString()))
                            .All(y => y)));
            }

            var usedNames = treeBuilder.ApplyNameSubstitutesLoop(input).OfType<Payload>()
                        .Select(p => p.Name)
                        .Distinct()
                        .ToList();

            var schemeTrees = treeBuilder.BuildScheme(schemes, usedNames)
                .Where(x => x
                    .GetAllChildrenAndSelf()
                    .Select(y => y.entry as SchemeNode)
                    .Where(y => y != null)
                    .Any(y => !string.IsNullOrEmpty(y.SingleOrDefaultValue(EntryType.output)))).ToList();

            var substituteTypes = schemeTrees.GetSubstituteTypes();

            var profileTrees = treeBuilder.Build(input, substituteTypes);

            var resultsToWrite =
                from scheme in schemeTrees.AsParallel()
                from tree in profileTrees
                from alteredScheme in treeBuilder.BuildScheme(
                    scheme.WithImplicitArrays(tree),
                    usedNames)
                from pair in formatterBuilder.Build(alteredScheme)
                from subTree in GetSubTrees(tree, pair.prefix)
                select (tree: subTree, pair.formatter);

            foreach (var (tree, formatter) in resultsToWrite)
            {
                await formatter.Write(tree, cancellationToken);
            }
        }

        private IEnumerable<ProfileTree> GetSubTrees(ProfileTree tree, QualifiedName prefix)
        {
            var subTrees = tree.GetSubTrees(prefix).ToList();

            if (subTrees.Count == 0)
            {
                logger.LogDebug("No entries to output from {name}, prefix: {prefix}", tree.NameString, prefix);
            }

            return subTrees;
        }
    }
}
