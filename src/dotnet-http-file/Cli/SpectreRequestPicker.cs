using DotNetHttpFile.Output;
using DotNetHttpFile.Parsing;
using Spectre.Console;

namespace DotNetHttpFile.Cli;

/// <summary>
/// An arrow-key list of the requests in a file. Escape leaves it without a choice.
/// </summary>
internal sealed class SpectreRequestPicker(IAnsiConsole console) : IRequestPicker
{
    private const string UnnamedRequest = "-";
    private const string ColumnSeparator = "  ";
    private const int NoSelection = -1;

    public RequestDefinition? Pick(IReadOnlyList<RequestDefinition> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);

        // The prompt cannot hold a null choice, so it selects by index and Escape yields an index of its own.
        // A label comes from the file, where [ and ] are plain text and not Spectre markup.
        SelectionPrompt<int> prompt = new SelectionPrompt<int>()
            .Title(Markup.Escape(Resources.PickerTitle))
            .UseConverter(index => Markup.Escape(Label(requests[index])))
            .AddChoices(Enumerable.Range(0, requests.Count))
            .AddCancelResult(NoSelection);

        int selected = console.Prompt(prompt);

        return selected == NoSelection ? null : requests[selected];
    }

    /// <summary>
    /// The line shown for <paramref name="request"/>: its name or title, its method and its URL as written.
    /// </summary>
    public static string Label(RequestDefinition request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string label = string.Join(
            ColumnSeparator,
            request.NameOrTitle ?? UnnamedRequest,
            request.Method,
            request.Url.Raw);

        // The URL is the text of the file as written, which can hold an escape sequence.
        return ConsoleText.WithVisibleControlCharacters(label);
    }
}
