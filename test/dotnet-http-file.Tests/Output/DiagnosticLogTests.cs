using DotNetHttpFile.Output;
using DotNetHttpFile.Resolution;
using Spectre.Console.Testing;

namespace DotNetHttpFile.Tests.Output;

public sealed class DiagnosticLogTests
{
    private const char Escape = (char)0x1B;

    [Fact]
    public void Write_Disabled_WritesNothing()
    {
        using TestConsole error = new();
        DiagnosticLog log = new(error);

        log.Write("HTTP file: a.http");

        Assert.Empty(error.Output);
    }

    [Fact]
    public void Write_Enabled_WritesPrefixedLineToError()
    {
        using TestConsole error = new();
        DiagnosticLog log = new(error) { IsEnabled = true };

        log.Write("HTTP file: a.http");

        Assert.Equal("verbose: HTTP file: a.http\n", Normalize(error.Output));
    }

    [Fact]
    public void Write_MasksSecrets()
    {
        using TestConsole error = new();
        SecretMask mask = new();
        mask.Add("s3cret");
        DiagnosticLog log = new(error, mask) { IsEnabled = true };

        log.Write("x s3cret");

        Assert.Equal("verbose: x ***\n", Normalize(error.Output));
    }

    [Fact]
    public void Write_ControlCharacters_AreMadeVisible()
    {
        using TestConsole error = new();
        DiagnosticLog log = new(error) { IsEnabled = true };

        log.Write($"a{Escape}[2Jb");

        Assert.Equal("verbose: a\\x1B[2Jb\n", Normalize(error.Output));
    }

    [Fact]
    public void Write_LineBreaks_StayOnOneLine()
    {
        using TestConsole error = new();
        DiagnosticLog log = new(error) { IsEnabled = true };

        log.Write("Plan: a\r\nb\nc\rd");

        Assert.Equal("verbose: Plan: a\\x0D\\x0Ab\\x0Ac\\x0Dd\n", Normalize(error.Output));
    }

    [Fact]
    public void Write_SecretWithLineBreak_IsMaskedBeforeTheBreakIsShown()
    {
        using TestConsole error = new();
        SecretMask mask = new();
        mask.Add("s3\ncret");
        DiagnosticLog log = new(error, mask) { IsEnabled = true };

        log.Write("x s3\ncret");

        Assert.Equal("verbose: x ***\n", Normalize(error.Output));
    }

    [Fact]
    public void Write_LongLine_IsNotWrapped()
    {
        using TestConsole error = new();
        error.Profile.Width = 20;
        DiagnosticLog log = new(error) { IsEnabled = true };
        string message = new('a', 50);

        log.Write(message);

        Assert.Equal($"verbose: {message}\n", Normalize(error.Output));
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
