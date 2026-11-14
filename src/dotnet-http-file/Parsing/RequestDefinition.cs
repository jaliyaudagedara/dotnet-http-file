using DotNetHttpFile.Templates;

namespace DotNetHttpFile.Parsing;

internal sealed record HeaderDefinition(string Name, Template Value, int Line);

internal sealed record RequestDefinition(
    string? Name,
    string Method,
    Template Url,
    string? HttpVersion,
    IReadOnlyList<HeaderDefinition> Headers,
    Template? Body,
    int Line)
{
    /// <summary>The line the body starts on, or null when the request has no body.</summary>
    public int? BodyLine { get; init; }
}
