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
    public async Task PickAsync_EnterOnTheFirstChoice_ReturnsTheFirstRequest()
    {
        using TestConsole console = InteractiveConsole(ConsoleKey.Enter);

        RequestDefinition? picked = await PickAsync(console, s_first, s_second);

        Assert.Same(s_first, picked);
    }

    [Fact]
    public async Task PickAsync_DownThenEnter_ReturnsTheSecondRequest()
    {
        using TestConsole console = InteractiveConsole(ConsoleKey.DownArrow, ConsoleKey.Enter);

        RequestDefinition? picked = await PickAsync(console, s_first, s_second);

        Assert.Same(s_second, picked);
    }

    [Fact]
    public async Task PickAsync_Escape_ReturnsNull()
    {
        using TestConsole console = InteractiveConsole(ConsoleKey.Escape);

        RequestDefinition? picked = await PickAsync(console, s_first, s_second);

        Assert.Null(picked);
    }

    [Fact]
    public async Task PickAsync_LabelWithMarkupCharacters_PrintsThemLiterally()
    {
        using TestConsole console = InteractiveConsole(ConsoleKey.Enter);

        await PickAsync(console, s_first, s_second);

        Assert.Contains("todos  GET  {{host}}/v1/todos", console.Output, StringComparison.Ordinal);
        Assert.Contains("-  POST  https://localhost/[red]items[/]", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PickAsync_CancelledToken_Throws()
    {
        using TestConsole console = InteractiveConsole();
        SpectreRequestPicker picker = new(console);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => picker.PickAsync([s_first, s_second], new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task PickAsync_CancelledToken_ShowsTheCursorAgainOnALineOfItsOwn()
    {
        using TestConsole console = InteractiveConsole().EmitAnsiSequences();
        SpectreRequestPicker picker = new(console);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => picker.PickAsync([s_first, s_second], new CancellationToken(canceled: true)));

        Assert.EndsWith($"{Escape}[?25h{Environment.NewLine}", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PickAsync_PromptFails_ShowsTheCursorAgainOnALineOfItsOwn()
    {
        using TestConsole console = InteractiveConsole().EmitAnsiSequences();
        SpectreRequestPicker picker = new(console);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => picker.PickAsync([s_first, s_second], TestContext.Current.CancellationToken));

        Assert.EndsWith($"{Escape}[?25h{Environment.NewLine}", console.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    public void CanPick_Terminal_NeedsInteractionAndAnsi(bool isInteractive, bool supportsAnsi, bool expected)
    {
        using TestConsole console = new TestConsole().SupportsAnsi(supportsAnsi);
        console.Profile.Capabilities.Interactive = isInteractive;

        bool canPick = new SpectreRequestPicker(console).CanPick;

        Assert.Equal(expected, canPick);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task PickAsync_TerminalThatCannotPick_IsRefusedBySpectre(bool isInteractive, bool supportsAnsi)
    {
        using TestConsole console = new TestConsole().SupportsAnsi(supportsAnsi);
        console.Profile.Capabilities.Interactive = isInteractive;
        SpectreRequestPicker picker = new(console);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => picker.PickAsync([s_first, s_second], TestContext.Current.CancellationToken));
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
    public async Task PickAsync_LabelWithControlCharacters_DoesNotWriteThem()
    {
        using TestConsole console = InteractiveConsole(ConsoleKey.Enter);
        RequestDefinition request = Request("clear", "GET", $"http://localhost/{Escape}[2J");

        await PickAsync(console, request, s_first);

        Assert.Contains(@"http://localhost/\x1B[2J", console.Output, StringComparison.Ordinal);
        Assert.DoesNotContain($"{Escape}[2J", console.Output, StringComparison.Ordinal);
    }

    private static RequestDefinition Request(string? name, string method, string url) =>
        new(method, Template.Parse(url), Headers: [], Line: 1) { Name = name };

    private static Task<RequestDefinition?> PickAsync(TestConsole console, params RequestDefinition[] requests) =>
        new SpectreRequestPicker(console).PickAsync(requests, TestContext.Current.CancellationToken);

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
