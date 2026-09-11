using System.Reflection;

namespace Namespace2Xml.Gatekeeper;

internal static class Program
{
    internal static int Main(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], "validate", StringComparison.Ordinal))
        {
            Console.Error.WriteLine(
                "usage: Namespace2Xml.Gatekeeper validate [--root <repository-root>]");
            return 2;
        }

        string root;
        if (args.Length == 1)
        {
            root = Directory.GetCurrentDirectory();
        }
        else if (args.Length == 3 && string.Equals(args[1], "--root", StringComparison.Ordinal))
        {
            root = Path.GetFullPath(args[2]);
        }
        else
        {
            Console.Error.WriteLine(
                "usage: Namespace2Xml.Gatekeeper validate [--root <repository-root>]");
            return 2;
        }

        try
        {
            StaticGateValidator.ValidateRepository(
                root,
                Assembly.GetExecutingAssembly().Location);
            Console.WriteLine("All manifest gate identities resolve exactly.");
            return 0;
        }
        catch (InvalidDataException error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
        catch (FormatException error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
        catch (IOException error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
        catch (UnauthorizedAccessException error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }
}
