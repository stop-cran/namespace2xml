using Namespace2Xml.Scheme;
using System.IO;

namespace Namespace2Xml.Formatters
{
    public interface IStreamFactory
    {
        Stream CreateInputStream(string name);

        Stream CreateOutputStream(string name, OutputType type);
    }
}
