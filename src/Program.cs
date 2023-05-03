using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Namespace2Xml.Formatters;
using Namespace2Xml.Semantics;
using NullGuard;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Namespace2Xml.Syntax;

namespace Namespace2Xml
{
    public class Program
    {
        [AllowNull]
        public static Action<IServiceCollection> ServiceOverrides { get; set; }

        public static async Task<int> Main(string[] args)
        {
            try
            {
                return await Parser.Default
                    .ParseArguments<Arguments>(args)
                    .MapResult(
                        async arguments =>
                        {
                            var servicerCollection = new ServiceCollection()
                                .AddLogging(logging => logging.AddConsole().SetMinimumLevel(arguments.LoggingLevel))
                                .AddTransient<IStreamFactory, FileStreamFactory>()
                                .AddTransient<IProfileReader, ProfileReader>()
                                .AddTransient<ITreeBuilder, TreeBuilder>()
                                .AddTransient<IFormatterBuilder, FormatterBuilder>()
                                .AddTransient<CompositionRoot>()
                                .Configure<FileStreamFactoryOptions>(options => options.BaseOutputDirectory = arguments.OutputDirectory)
                                .Configure<QualifiedNameOptions>(options => options.ImplicitRoot = "ImplicitRoot");

                            ServiceOverrides?.Invoke(servicerCollection);

                            var servicerProvider = servicerCollection.BuildServiceProvider();
                            var logger = servicerProvider.GetRequiredService<ILogger<Program>>();
                            logger.LogInformation("namespace2xml, version {0}",
                                Assembly
                                    .GetEntryAssembly()
                                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                                    ?.InformationalVersion);

                            await servicerProvider.GetRequiredService<CompositionRoot>()
                                .Write(arguments, default);

                            logger.LogInformation("Success! Exiting...");

                            return 0;
                        },
                        errors => Task.FromResult(
                            errors.OfType<HelpRequestedError>().Any() ? 0 : 1));
            }
            catch (ApplicationException)
            {
                return 1;
            }
        }
    }
}
