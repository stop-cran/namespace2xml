using CommandLine;
using log4net;
using log4net.Config;
using log4net.Repository.Hierarchy;
using Namespace2Xml.Formatters;
using Namespace2Xml.Semantics;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Unity;
using Unity.Injection;
using Unity.log4net;

namespace Namespace2Xml
{
    public static class Program
    {
        public static IUnityContainer Container { get; } =
            new UnityContainer()
                .AddNewExtension<Log4NetExtension>()
                .RegisterType<IStreamFactory, FileStreamFactory>()
                .RegisterType<IProfileReader, ProfileReader>()
                .RegisterType<ITreeBuilder, TreeBuilder>()
                .RegisterType<IFormatterBuilder, FormatterBuilder>();

        public static async Task<int> Main(string[] args)
        {
            var loggerRepository = LogManager.GetRepository(
                typeof(Program).Assembly);

            XmlConfigurator.Configure(
                loggerRepository,
                new FileInfo(
                    Path.Combine(
                        Path.GetDirectoryName(
                            Assembly.GetExecutingAssembly().Location),
                        "log4net.config")));

            var logger = LogManager.GetLogger(typeof(Program));

            try
            {
                return await Parser.Default
                    .ParseArguments<Arguments>(args)
                    .MapResult(
                        async arguments =>
                        {
                            loggerRepository.Threshold = arguments.LoggingLevel;

                            logger.Info(new
                            {
                                message = "namespace2xml",
                                version = Assembly
                                    .GetEntryAssembly()
                                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                                    ?.InformationalVersion
                            });

                            await Container
                            .RegisterType<FileStreamFactory>(
                                new InjectionConstructor(
                                    arguments.OutputDirectory,
                                    new ResolvedParameter<ILog>()))
                            .Resolve<CompositionRoot>()
                            .Write(arguments, default);

                            logger.Info("Success! Exiting...");

                            return 0;
                        },
                        errors => Task.FromResult(
                            errors.OfType<HelpRequestedError>().Any() ? 0 : 1));
            }
            catch (ApplicationException)
            { }
            catch (Exception ex)
            {
                logger.Error("Unexpected error", ex);
            }

            return 1;
        }
    }
}
