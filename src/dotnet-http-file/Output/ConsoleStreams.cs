using Spectre.Console;

namespace DotNetHttpFile.Output;

/// <summary>
/// The streams a run writes to.
/// </summary>
/// <param name="StandardOutput">The raw standard output, which takes response bytes without any encoding.</param>
/// <param name="Output">The console over standard output.</param>
/// <param name="Error">The console over standard error.</param>
/// <param name="IsInputRedirected">Whether standard input is not a terminal.</param>
/// <param name="IsOutputRedirected">Whether standard output is piped or written to a file.</param>
internal sealed record ConsoleStreams(
    Stream StandardOutput,
    IAnsiConsole Output,
    IAnsiConsole Error,
    bool IsInputRedirected,
    bool IsOutputRedirected);
