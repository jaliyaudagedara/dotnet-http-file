using DotNetHttpFile.Cli;
using DotNetHttpFile.Parsing;
using DotNetHttpFile.Templates;
using Spectre.Console.Testing;

namespace DotNetHttpFile.Tests.Cli;

public sealed class SpectreRequestPickerTests
{
    private const char Escape = (char)0x1B;
    private const char Bell = (char)0x07;

    private static readonly RequestDefinition s_first = Request("todos", "GET", "{{host}}/v1/todos");
    private static readonly RequestDefinition s_second = Request(null, "POST", "https://localhost/[red]items[/]");

    [Fact]
    public void Pick_EnterOnTheFirstChoice_ReturnsTheFirstRequest()
    {
        using TestConsole console = InteractiveConsole(ConsoleKey.Enter);

        RequestDefinition? picked = new SpectreRequestPicker(console).Pick([s_first, s_second]);

        Assert.Same(s_first, picked);
    }

    [Fact]
    public void Pick_DownThenEnter_ReturnsTheSecondRequest()
    {
        using TestConsole console = InteractiveConsole(ConsoleKey.DownArrow, ConsoleKey.Enter);

        RequestDefinition? picked = new SpectreRequestPicker(console).Pick([s_first, s_second]);

        Assert.Same(s_second, picked);
    }

    [Fact]
    public void Pick_Escape_ReturnsNull()
    {
        using TestConsole console = InteractiveConsole(ConsoleKey.Escape);

        RequestDefinition? picked = new SpectreRequestPicker(console).Pick([s_first, s_second]);

        Assert.Null(picked);
    }

    [Fact]
    public void Pick_LabelWithMarkupCharacters_PrintsThemLiterally()
    {
        using TestConsole console = InteractiveConsole(ConsoleKey.Enter);

        new SpectreRequestPicker(console).Pick([s_first, s_second]);

        Assert.Contains("todos  GET  {{host}}/v1/todos", console.Output, StringComparison.Ordinal);
        Assert.Contains("-  POST  https://localhost/[red]items[/]", console.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("todos", "todos  GET  {{host}}/v1/todos")]
    [InlineData(null, "-  GET  {{host}}/v1/todos")]
    public void Label_Request_JoinsNameMethodAndUnresolvedUrl(string? name, string expected)
    {
        string label = SpectreRequestPicker.Label(Request(name, "GET", "{{host}}/v1/todos"));

        Assert.Equal(expected, label);
    }

    [Theory]
    [InlineData("todos", "List", "todos  GET  {{host}}/v1/todos")]
    [InlineData(null, "List", "List  GET  {{host}}/v1/todos")]
    public void Label_RequestWithTitle_ShowsNameBeforeTitle(string? name, string title, string expected)
    {
        RequestDefinition request = Request(name, "GET", "{{host}}/v1/todos") with { Title = title };

        Assert.Equal(expected, SpectreRequestPicker.Label(request));
    }

    [Fact]
    public void Label_UrlWithControlCharacters_MakesThemVisible()
    {
        string label = SpectreRequestPicker.Label(Request(null, "GET", $"http://localhost/{Escape}[2J{Bell}"));

        Assert.Equal(@"-  GET  http://localhost/\x1B[2J\x07", label);
    }

    [Fact]
    public void Pick_LabelWithControlCharacters_DoesNotWriteThem()
    {
        using TestConsole console = InteractiveConsole(ConsoleKey.Enter);
        RequestDefinition request = Request("clear", "GET", $"http://localhost/{Escape}[2J");

        new SpectreRequestPicker(console).Pick([request, s_first]);

        Assert.Contains(@"http://localhost/\x1B[2J", console.Output, StringComparison.Ordinal);
        Assert.DoesNotContain($"{Escape}[2J", console.Output, StringComparison.Ordinal);
    }

    private static RequestDefinition Request(string? name, string method, string url) =>
        new(method, Template.Parse(url), Headers: [], Line: 1) { Name = name };

    private static TestConsole InteractiveConsole(params ConsoleKey[] keys)
    {
        TestConsole console = new();
        console.Profile.Width = 200;
        console.Interactive();
        foreach (ConsoleKey key in keys)
        {
            console.Input.PushKey(key);
        }

        return console;
    }
}
