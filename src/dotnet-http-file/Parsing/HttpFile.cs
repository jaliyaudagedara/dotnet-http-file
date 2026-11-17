using DotNetHttpFile.Templates;

namespace DotNetHttpFile.Parsing;

internal sealed record FileVariable(string Name, Template Value, int Line)
{
    /// <summary>
    /// Tells whether <paramref name="name"/> can name a variable: it is not empty and has no whitespace.
    /// </summary>
    /// <returns>
    /// True when <paramref name="name"/> is a valid variable name.
    /// </returns>
    public static bool IsValidName(string name) => name.Length > 0 && !name.Any(char.IsWhiteSpace);
}

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

    /// <summary>
    /// Returns the names of the plain variables, as <c>{{name}}</c>, that the file value of a variable or any
    /// request refers to. Request references and dynamic variables name no variable.
    /// </summary>
    public HashSet<string> ReferencedVariableNames()
    {
        HashSet<string> names = [with(StringComparer.Ordinal)];
        IEnumerable<Template> templates = Variables.Select(variable => variable.Value)
            .Concat(Requests.SelectMany(request => request.Templates));
        foreach (Template template in templates)
        {
            names.UnionWith(template.Segments.OfType<VariableSegment>().Select(segment => segment.Name));
        }

        return names;
    }
}
