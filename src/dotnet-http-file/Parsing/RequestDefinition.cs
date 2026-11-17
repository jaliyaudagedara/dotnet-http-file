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

    /// <summary>
    /// The name, else the title, or null when the request has neither.
    /// </summary>
    public string? NameOrTitle => Name ?? Title;

    /// <summary>
    /// What the tool calls the request: <see cref="NameOrTitle"/>, else the request line as written.
    /// </summary>
    /// <remarks>
    /// The request line is the text of the file, so a variable in the URL is not expanded and no value can show.
    /// </remarks>
    public string DisplayName => NameOrTitle ?? $"{Method} {Url.Raw}";

    /// <summary>
    /// The templates of the request: the URL, the header values and the body when there is one.
    /// </summary>
    public IEnumerable<Template> Templates
    {
        get
        {
            yield return Url;

            foreach (HeaderDefinition header in Headers)
            {
                yield return header.Value;
            }

            if (Body is not null)
            {
                yield return Body;
            }
        }
    }
}
