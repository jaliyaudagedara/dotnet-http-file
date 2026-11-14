using DotNetHttpFile.Parsing;

namespace DotNetHttpFile.Cli;

internal interface IRequestPicker
{
    /// <summary>Asks the user which of <paramref name="requests"/> to run.</summary>
    /// <returns>The chosen request, or null when the user leaves without choosing.</returns>
    RequestDefinition? Pick(IReadOnlyList<RequestDefinition> requests);
}
