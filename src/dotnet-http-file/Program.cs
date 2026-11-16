using System.Text;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using DotNetHttpFile.Cli;
using DotNetHttpFile.Execution;
using DotNetHttpFile.Output;
using DotNetHttpFile.Secrets;
using Spectre.Console;

// A Windows console in its default code page prints the arrow and the middle dot of the output as '?'. The code
// page belongs to the console and not to this process, so it is put back on the way out: left as UTF-8 it would
// change the output of whatever runs next in the same window.
Encoding consoleOutputEncoding = Console.OutputEncoding;
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
try
{
    // Not disposed: the Ctrl+C handler can still run while the process is exiting.
    CancellationTokenSource cancellation = new();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        // The first Ctrl+C stops the run and lets it report that; a second one ends the process as usual,
        // without running the finally below.
        if (!cancellation.IsCancellationRequested)
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        }
        else
        {
            Console.OutputEncoding = consoleOutputEncoding;
        }
    };

    ConsoleStreams console = new(
        Console.OpenStandardOutput(),
        AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Out) }),
        AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Error) }),
        Console.IsInputRedirected,
        Console.IsOutputRedirected);

    Lazy<TokenCredential> credential = new(AzureCredentialFactory.Create);

    HttpFileRunner runner = new(
        console,
        new SpectreRequestPicker(console.Output),
        log => new SecretProviderRegistry(
        [
            new AspnetUserSecretsProvider(AspnetUserSecretsProvider.DefaultRoot()),
            new AzureKeyVaultProvider(
                credential,
                static (vaultUri, tokenCredential) => new SecretClient(vaultUri, tokenCredential),
                log),
            new EncryptedProvider(),
            new EntraAccessTokenProvider(credential, log),
        ]),
        ToolHttpClient.CreateHandler,
        TimeProvider.System,
        Environment.GetEnvironmentVariable);

    return await CommandLine.InvokeAsync(args, runner.RunAsync, console, cancellation.Token);
}
finally
{
    Console.OutputEncoding = consoleOutputEncoding;
}
