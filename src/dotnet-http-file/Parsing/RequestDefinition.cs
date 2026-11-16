using DotNetHttpFile.Templates;

namespace DotNetHttpFile.Parsing;

internal sealed record HeaderDefinition(string Name, Template Value, int Line);

internal sealed record RequestDefinition(
    string Method,
    Template Url,
    IReadOnlyList<HeaderDefinition> Headers,
    int Line)
{
    /// <summary>
    /// The name given with <c>@name</c>, or null when the request has none.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// The text after <c>###</c> on the separator line above the request, or null when there is none.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// The version written on the request line, or null.
    /// </summary>
    public string? HttpVersion { get; init; }

    /// <summary>
    /// The body, or null when the request has none.
    /// </summary>
    public Template? Body { get; init; }

    /// <summary>
    /// The line the body starts on, or null when the request has no body.
    /// </summary>
    public int? BodyLine { get; init; }
}
