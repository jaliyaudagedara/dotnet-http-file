using System.Text;
using DotNetHttpFile.Parsing;
using DotNetHttpFile.Templates;

namespace DotNetHttpFile.Resolution;

internal static class DependencyPlanner
{
    private const string CycleSeparator = " -> ";

    private static readonly CompositeFormat s_requestDependencyCycleFormat =
        CompositeFormat.Parse(Resources.RequestDependencyCycle);

    private static readonly CompositeFormat s_requestNotFoundFormat =
        CompositeFormat.Parse(Resources.RequestNotFound);

    /// <summary>
    /// Lists the requests to send for <paramref name="target"/>: every named request it references, directly,
    /// through a file variable or through another dependency, and then the target itself.
    /// </summary>
    /// <returns>
    /// Dependencies in the order they are first referenced, each once, with the target last.
    /// </returns>
    /// <exception cref="HttpFileException">A reference names no request, or the references form a cycle.</exception>
    public static IReadOnlyList<RequestDefinition> Plan(HttpFile file, RequestDefinition target)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(target);

        Dictionary<string, RequestDefinition> requestsByName = [with(StringComparer.Ordinal)];
        foreach (RequestDefinition request in file.Requests)
        {
            if (request.Name is not null)
            {
                requestsByName.TryAdd(request.Name, request);
            }
        }

        List<RequestDefinition> plan = [];
        Visit(file, target, requestsByName, path: [], plan);

        return plan;
    }

    private static void Visit(
        HttpFile file,
        RequestDefinition request,
        Dictionary<string, RequestDefinition> requestsByName,
        List<RequestDefinition> path,
        List<RequestDefinition> plan)
    {
        path.Add(request);

        foreach (RequestReference reference in FindReferences(file, request))
        {
            if (!requestsByName.TryGetValue(reference.RequestName, out RequestDefinition? dependency))
            {
                throw new HttpFileException(Messages.Format(
                    s_requestNotFoundFormat,
                    reference.RequestName,
                    reference.Line));
            }

            int cycleStart = path.FindIndex(step => ReferenceEquals(step, dependency));
            if (cycleStart >= 0)
            {
                IEnumerable<string?> names = path.Skip(cycleStart).Append(dependency).Select(step => step.Name);

                throw new HttpFileException(Messages.Format(
                    s_requestDependencyCycleFormat,
                    string.Join(CycleSeparator, names)));
            }

            if (!plan.Exists(planned => ReferenceEquals(planned, dependency)))
            {
                Visit(file, dependency, requestsByName, path, plan);
            }
        }

        path.RemoveAt(path.Count - 1);
        plan.Add(request);
    }

    private static List<RequestReference> FindReferences(HttpFile file, RequestDefinition request)
    {
        List<RequestReference> references = [];
        HashSet<FileVariable> scannedVariables = [with(ReferenceEqualityComparer.Instance)];

        CollectReferences(file, request.Url, request.Line, scannedVariables, references);
        foreach (HeaderDefinition header in request.Headers)
        {
            CollectReferences(file, header.Value, header.Line, scannedVariables, references);
        }

        if (request.Body is not null)
        {
            CollectReferences(file, request.Body, request.BodyLine ?? request.Line, scannedVariables, references);
        }

        return references;
    }

    private static void CollectReferences(
        HttpFile file,
        Template template,
        int line,
        HashSet<FileVariable> scannedVariables,
        List<RequestReference> references)
    {
        IReadOnlyDictionary<string, FileVariable>? visibleVariables = null;
        for (int index = 0; index < template.Segments.Count; index++)
        {
            switch (template.Segments[index])
            {
                case RequestReferenceSegment reference:
                    references.Add(
                        new RequestReference(reference.RequestName, TemplateLines.LineOf(template, index, line)));
                    break;

                case VariableSegment variable:
                    visibleVariables ??= file.VariablesVisibleAt(line);

                    // A file variable sees only the definitions above it, so this recursion cannot loop.
                    if (visibleVariables.TryGetValue(variable.Name, out FileVariable? definition)
                        && scannedVariables.Add(definition))
                    {
                        CollectReferences(file, definition.Value, definition.Line, scannedVariables, references);
                    }

                    break;
            }
        }
    }

    private readonly record struct RequestReference(string RequestName, int Line);
}
