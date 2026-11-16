using DotNetHttpFile.Templates;

namespace DotNetHttpFile.Parsing;

internal sealed record FileVariable(string Name, Template Value, int Line);

internal sealed record HttpFile(
    string Path,
    IReadOnlyList<FileVariable> Variables,
    IReadOnlyList<RequestDefinition> Requests)
{
    /// <summary>
    /// Returns the last definition of each file variable defined above <paramref name="line"/>.
    /// </summary>
    public IReadOnlyDictionary<string, FileVariable> VariablesVisibleAt(int line)
    {
        Dictionary<string, FileVariable> visible = [with(StringComparer.Ordinal)];
        foreach (FileVariable variable in Variables)
        {
            if (variable.Line < line)
            {
                visible[variable.Name] = variable;
            }
        }

        return visible;
    }
}
