using System.Text;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using DotNetHttpFile.Cli;
using DotNetHttpFile.Execution;
using DotNetHttpFile.Output;
using DotNetHttpFile.Secrets;
using Spectre.Console;

// A Windows console in its default code page prints the arrow and the middle dot of the output as '?'.
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

// Not disposed: the Ctrl+C handler can still run while the process is exiting.
CancellationTokenSource cancellation = new();
Console.CancelKeyPress += (_, eventArgs) =>
{
    // The first Ctrl+C stops the run and lets it report that; a second one ends the process as usual.
    if (!cancellation.IsCancellationRequested)
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    }
};

ConsoleStreams console = new(
    Console.OpenStandardOutput(),
    AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Out) }),
    AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Error) }),
    Console.IsInputRedirected,
    Console.IsOutputRedirected);

Lazy<TokenCredential> credential = new(AzureCredentialFactory.Create);
SecretProviderRegistry providers = new(
[
    new AspnetUserSecretsProvider(AspnetUserSecretsProvider.DefaultRoot()),
    new AzureKeyVaultProvider(
        credential,
        static (vaultUri, tokenCredential) => new SecretClient(vaultUri, tokenCredential)),
    new EncryptedProvider(),
    new EntraAccessTokenProvider(credential),
]);

HttpFileRunner runner = new(
    console,
    new SpectreRequestPicker(console.Output),
    providers,
    ToolHttpClient.CreateHandler,
    TimeProvider.System,
    Environment.GetEnvironmentVariable);

return await CommandLine.InvokeAsync(args, runner.RunAsync, console, cancellation.Token);
