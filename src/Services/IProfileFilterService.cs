using System.Collections.Generic;
using Namespace2Xml.Syntax;

namespace Namespace2Xml.Services;

public interface IProfileFilterService
{
    IReadOnlyCollection<IProfileEntry> FilterByOutput(IReadOnlyList<IProfileEntry> inputs, IReadOnlyList<IProfileEntry> schemes);
}