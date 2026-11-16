using DotNetHttpFile.Output;
using Spectre.Console;
using Spectre.Console.Testing;

namespace DotNetHttpFile.Tests.Output;

public sealed class ConsoleTextTests
{
    private const char Escape = (char)0x1B;

    private static readonly Style s_nameStyle = new(foreground: Color.Teal);

    [Theory]
    [InlineData("Name: v", "\u001B[36mName\u001B[0m: v")]
    [InlineData("Name: ", "\u001B[36mName\u001B[0m: ")]
    [InlineData(": v", ": v")]
    [InlineData("Name v", "Name v")]
    [InlineData("Name: a: b", "\u001B[36mName\u001B[0m: a: b")]
    public void WriteHeader_ColourEnabled_StylesOnlyTheNameBeforeTheFirstSeparator(string header, string expected)
    {
        using TestConsole console = ColourConsole();

        ConsoleText.WriteHeader(console, header, s_nameStyle);

        Assert.Equal(expected + "\n", console.Output.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public void WriteHeader_SeparatorAfterLineBreak_WritesTheLineUnstyled()
    {
        using TestConsole console = ColourConsole();

        ConsoleText.WriteHeader(console, "Name\nnext: v", s_nameStyle);

        Assert.DoesNotContain(Escape, console.Output);
    }

    [Theory]
    [InlineData("a\u001B[2Jb", @"a\x1B[2Jb")]
    [InlineData("a\u0000b", @"a\x00b")]
    [InlineData("a\u007Fb", @"a\x7Fb")]
    [InlineData("a\u009Bb", @"a\x9Bb")]
    public void WithVisibleControlCharacters_ControlCharacter_ShowsItsCode(string text, string expected)
    {
        string visible = ConsoleText.WithVisibleControlCharacters(text);

        Assert.Equal(expected, visible);
    }

    [Theory]
    [InlineData('\u202A', @"\u202A")]
    [InlineData('\u202B', @"\u202B")]
    [InlineData('\u202C', @"\u202C")]
    [InlineData('\u202D', @"\u202D")]
    [InlineData('\u202E', @"\u202E")]
    [InlineData('\u2066', @"\u2066")]
    [InlineData('\u2067', @"\u2067")]
    [InlineData('\u2068', @"\u2068")]
    [InlineData('\u2069', @"\u2069")]
    public void WithVisibleControlCharacters_BidiEmbeddingOverrideOrIsolate_ShowsItsCode(char control, string code)
    {
        string visible = ConsoleText.WithVisibleControlCharacters($"user{control}txt.exe");

        Assert.Equal($"user{code}txt.exe", visible);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WithVisibleControlCharacters_BidiControlAndLineBreak_ShowsTheBidiCode(bool keepLineBreaks)
    {
        string visible = ConsoleText.WithVisibleControlCharacters("a\u202Eb\nc", keepLineBreaks);

        Assert.Equal(keepLineBreaks ? "a\\u202Eb\nc" : @"a\u202Eb\x0Ac", visible);
    }

    [Theory]
    [InlineData("a\tb\nc\r\nd")]
    [InlineData("\u05E9\u05DC\u05D5\u05DD \u0645\u0631\u062D\u0628\u0627")]
    [InlineData("\u2029\u2065\u206A")]
    public void WithVisibleControlCharacters_TextWithoutHiddenControls_ReturnsTheSameText(string text)
    {
        string visible = ConsoleText.WithVisibleControlCharacters(text);

        Assert.Same(text, visible);
    }

    [Theory]
    [InlineData('\u200E')]
    [InlineData('\u200F')]
    [InlineData('\u061C')]
    public void WithVisibleControlCharacters_DirectionalMark_IsKept(char mark)
    {
        string text = $"\u05E9\u05DC\u05D5\u05DD{mark} abc";

        string visible = ConsoleText.WithVisibleControlCharacters(text);

        Assert.Same(text, visible);
    }

    [Fact]
    public void WithVisibleControlCharacters_CarriageReturnWithoutLineFeed_ShowsItsCode()
    {
        string visible = ConsoleText.WithVisibleControlCharacters("secret\rpublic");

        Assert.Equal(@"secret\x0Dpublic", visible);
    }

    private static TestConsole ColourConsole()
    {
        TestConsole console = new();
        console.EmitAnsiSequences();
        console.Profile.Capabilities.ColorSystem = ColorSystem.Standard;

        return console;
    }
}
