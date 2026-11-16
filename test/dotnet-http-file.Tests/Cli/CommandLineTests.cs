using System.Reflection;
using DotNetHttpFile.Cli;
using DotNetHttpFile.Output;
using Spectre.Console.Testing;

namespace DotNetHttpFile.Tests.Cli;

public sealed class CommandLineTests
{
    private const string HttpFileName = "MyHttp.http";

    [Fact]
    public async Task Main_FileOnly_RunsWithDefaults()
    {
        using Harness harness = new();
        string path = await harness.WriteFileAsync(HttpFileName);

        int exitCode = await harness.InvokeAsync(path);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(new RunOptions(path), Assert.Single(harness.Runs));
        Assert.Empty(harness.Error.Output);
    }

    [Fact]
    public async Task Main_LongOptions_PassesThemToTheRun()
    {
        using Harness harness = new();
        string path = await harness.WriteFileAsync(HttpFileName);

        await harness.InvokeAsync(path, "--name", "todos", "--env", "dev", "--verbose", "--insecure");

        RunOptions expected = new(path)
        {
            RequestName = "todos",
            EnvironmentName = "dev",
            ForcedMode = OutputMode.Full,
            Insecure = true,
        };
        Assert.Equal(expected, Assert.Single(harness.Runs));
    }

    [Fact]
    public async Task Main_ShortOptions_PassesThemToTheRun()
    {
        using Harness harness = new();
        string path = await harness.WriteFileAsync(HttpFileName);

        await harness.InvokeAsync("-n", "todos", "-e", "dev", "--show", "body", "-k", path);

        RunOptions expected = new(path)
        {
            RequestName = "todos",
            EnvironmentName = "dev",
            ForcedMode = OutputMode.Body,
            Insecure = true,
        };
        Assert.Equal(expected, Assert.Single(harness.Runs));
    }

    [Fact]
    public async Task Main_VerboseShortOption_ForcesFullMode()
    {
        using Harness harness = new();
        string path = await harness.WriteFileAsync(HttpFileName);

        await harness.InvokeAsync(path, "-v");

        Assert.Equal(OutputMode.Full, Assert.Single(harness.Runs).ForcedMode);
    }

    [Theory]
    [InlineData("requests.rest")]
    [InlineData("REQUESTS.HTTP")]
    [InlineData("requests.Rest")]
    public async Task Main_SupportedExtension_Runs(string fileName)
    {
        using Harness harness = new();
        string path = await harness.WriteFileAsync(fileName);

        int exitCode = await harness.InvokeAsync(path);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Single(harness.Runs);
    }

    [Theory]
    [InlineData(ExitCodes.ErrorStatus)]
    [InlineData(ExitCodes.NotSent)]
    [InlineData(ExitCodes.Cancelled)]
    public async Task Main_RunReturnsExitCode_ReturnsIt(int runExitCode)
    {
        using Harness harness = new() { Run = _ => Task.FromResult(runExitCode) };
        string path = await harness.WriteFileAsync(HttpFileName);

        int exitCode = await harness.InvokeAsync(path);

        Assert.Equal(runExitCode, exitCode);
    }

    [Fact]
    public async Task Main_CancellationToken_IsPassedToTheRun()
    {
        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        TaskCompletionSource runStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using Harness harness = new()
        {
            CancellationToken = cancellation.Token,
            Run = async runCancellationToken =>
            {
                runStarted.SetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, runCancellationToken);
                }
                catch (OperationCanceledException)
                {
                }

                return ExitCodes.Cancelled;
            },
        };
        string path = await harness.WriteFileAsync(HttpFileName);

        Task<int> invocation = harness.InvokeAsync(path);
        await runStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        int exitCode = await invocation;

        Assert.Equal(ExitCodes.Cancelled, exitCode);
    }

    [Fact]
    public async Task Main_UnknownOption_ReturnsNotSent()
    {
        using Harness harness = new();
        string path = await harness.WriteFileAsync(HttpFileName);

        int exitCode = await harness.InvokeAsync(path, "--bogus");

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Contains("--bogus", Assert.Single(harness.ErrorLines), StringComparison.Ordinal);
        Assert.StartsWith("error: ", harness.ErrorLines[0], StringComparison.Ordinal);
        Assert.Empty(harness.Runs);
        Assert.Empty(harness.Output.Output);
    }

    [Fact]
    public async Task Main_UnknownOptionBeforeTheFile_ReportsTheOption()
    {
        using Harness harness = new();
        string path = await harness.WriteFileAsync(HttpFileName);

        int exitCode = await harness.InvokeAsync("--bogus", path);

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Equal(["error: Unrecognized option '--bogus'."], harness.ErrorLines);
        Assert.Empty(harness.Runs);
    }

    [Fact]
    public async Task Main_MisspelledOptionWithValueBeforeTheFile_ReportsOnlyTheOption()
    {
        using Harness harness = new();
        string path = await harness.WriteFileAsync(HttpFileName);

        int exitCode = await harness.InvokeAsync("--nmae", "todos", path);

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Equal(["error: Unrecognized option '--nmae'."], harness.ErrorLines);
        Assert.Empty(harness.Runs);
    }

    [Fact]
    public async Task Main_UnknownOptionAlone_ReportsTheOption()
    {
        using Harness harness = new();

        int exitCode = await harness.InvokeAsync("--bogus");

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Equal(["error: Unrecognized option '--bogus'."], harness.ErrorLines);
    }

    [Fact]
    public async Task Main_ValueStartingWithAtSign_IsNotReadAsResponseFile()
    {
        using Harness harness = new();
        string path = await harness.WriteFileAsync(HttpFileName);
        RunOptions expected = new(path)
        {
            RequestName = "@x",
            EnvironmentName = "@dev",
        };

        int exitCode = await harness.InvokeAsync(path, "--name", "@x", "--env", "@dev");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(expected, Assert.Single(harness.Runs));
        Assert.Empty(harness.Error.Output);
    }

    [Theory]
    [InlineData("full", OutputMode.Full)]
    [InlineData("body", OutputMode.Body)]
    [InlineData("BODY", OutputMode.Body)]
    [InlineData("headers", OutputMode.Headers)]
    [InlineData("Headers", OutputMode.Headers)]
    [InlineData("status", OutputMode.Status)]
    [InlineData("STATUS", OutputMode.Status)]
    public async Task Main_Show_ForcesTheMode(string value, object expected)
    {
        using Harness harness = new();
        string path = await harness.WriteFileAsync(HttpFileName);

        int exitCode = await harness.InvokeAsync(path, "--show", value);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal((OutputMode)expected, Assert.Single(harness.Runs).ForcedMode);
        Assert.Empty(harness.Error.Output);
    }

    [Fact]
    public async Task Main_ShowUnknownValue_ReturnsNotSentWithTheParserMessage()
    {
        using Harness harness = new();
        string path = await harness.WriteFileAsync(HttpFileName);

        int exitCode = await harness.InvokeAsync(path, "--show", "bogus");

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.StartsWith("error: ", harness.ErrorLines[0], StringComparison.Ordinal);
        Assert.Contains("'bogus'", harness.ErrorLines[0], StringComparison.Ordinal);
        Assert.Empty(harness.Runs);
        Assert.Empty(harness.Output.Output);
    }

    [Fact]
    public async Task Main_ShowWithoutValue_ReturnsNotSent()
    {
        using Harness harness = new();
        string path = await harness.WriteFileAsync(HttpFileName);

        int exitCode = await harness.InvokeAsync(path, "--show");

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Contains("--show", Assert.Single(harness.ErrorLines), StringComparison.Ordinal);
        Assert.Empty(harness.Runs);
    }

    [Theory]
    [InlineData("-b")]
    [InlineData("--body")]
    [InlineData("--body-only")]
    [InlineData("--output")]
    [InlineData("-o")]
    public async Task Main_ReservedOption_IsNotRecognized(string option)
    {
        using Harness harness = new();
        string path = await harness.WriteFileAsync(HttpFileName);

        int exitCode = await harness.InvokeAsync(path, option);

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Contains(option, Assert.Single(harness.ErrorLines), StringComparison.Ordinal);
        Assert.Empty(harness.Runs);
    }

    [Theory]
    [InlineData("body")]
    [InlineData("full")]
    [InlineData("headers")]
    [InlineData("status")]
    public async Task Main_VerboseAndShow_ReturnsNotSent(string value)
    {
        using Harness harness = new();
        string path = await harness.WriteFileAsync(HttpFileName);

        int exitCode = await harness.InvokeAsync(path, "--verbose", "--show", value);

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Equal(["error: --verbose and --show cannot be used together."], harness.ErrorLines);
        Assert.Empty(harness.Runs);
    }

    [Fact]
    public async Task Main_MissingFile_ReturnsNotSent()
    {
        using Harness harness = new();

        int exitCode = await harness.InvokeAsync("--name", "todos");

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.StartsWith("error: ", Assert.Single(harness.ErrorLines), StringComparison.Ordinal);
        Assert.Empty(harness.Runs);
    }

    [Fact]
    public async Task Main_NoArguments_ReturnsNotSent()
    {
        using Harness harness = new();

        int exitCode = await harness.InvokeAsync();

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Single(harness.ErrorLines);
        Assert.Empty(harness.Runs);
    }

    [Fact]
    public async Task Main_FileDoesNotExist_ReturnsNotSent()
    {
        using Harness harness = new();
        string path = Path.Combine(harness.Directory.DirectoryPath, HttpFileName);

        int exitCode = await harness.InvokeAsync(path);

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Equal([$"error: File '{path}' was not found."], harness.ErrorLines);
        Assert.Empty(harness.Runs);
    }

    [Fact]
    public async Task Main_FileIsDirectory_ReturnsNotSent()
    {
        using Harness harness = new();
        string path = harness.Directory.CreateDirectory(HttpFileName);

        int exitCode = await harness.InvokeAsync(path);

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Equal([$"error: File '{path}' was not found."], harness.ErrorLines);
    }

    [Fact]
    public async Task Main_UnsupportedExtension_ReturnsNotSent()
    {
        using Harness harness = new();
        string path = await harness.WriteFileAsync("requests.txt");

        int exitCode = await harness.InvokeAsync(path);

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Equal([$"error: File '{path}' must have the extension .http or .rest."], harness.ErrorLines);
        Assert.Empty(harness.Runs);
    }

    [Fact]
    public async Task Main_NameWithoutValue_ReturnsNotSent()
    {
        using Harness harness = new();
        string path = await harness.WriteFileAsync(HttpFileName);

        int exitCode = await harness.InvokeAsync(path, "--name");

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Contains("--name", Assert.Single(harness.ErrorLines), StringComparison.Ordinal);
        Assert.Empty(harness.Runs);
    }

    [Fact]
    public async Task Main_ParseErrorWithMarkup_PrintsItLiterally()
    {
        using Harness harness = new();
        string path = await harness.WriteFileAsync(HttpFileName);

        int exitCode = await harness.InvokeAsync(path, "--[red]bogus[/]");

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Contains("--[red]bogus[/]", Assert.Single(harness.ErrorLines), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-?")]
    public async Task Main_Help_PrintsUsageAndReturnsSuccess(string option)
    {
        using Harness harness = new();

        int exitCode = await harness.InvokeAsync(option);

        Assert.Equal(ExitCodes.Success, exitCode);
        string help = harness.Output.Output;
        Assert.Contains("<file>", help, StringComparison.Ordinal);
        Assert.Contains("-n, --name", help, StringComparison.Ordinal);
        Assert.Contains("-e, --env", help, StringComparison.Ordinal);
        Assert.Contains("-v, --verbose", help, StringComparison.Ordinal);
        Assert.Contains("  --show <full|body|headers|status>", help, StringComparison.Ordinal);
        Assert.DoesNotContain("--body", help, StringComparison.Ordinal);
        Assert.DoesNotContain("--output", help, StringComparison.Ordinal);
        Assert.Contains("-k, --insecure", help, StringComparison.Ordinal);
        Assert.Empty(harness.Error.Output);
        Assert.Empty(harness.Runs);
    }

    [Fact]
    public async Task Main_Version_PrintsVersionAndReturnsSuccess()
    {
        using Harness harness = new();

        int exitCode = await harness.InvokeAsync("--version");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.NotEmpty(harness.Output.Output.Trim());
        Assert.Empty(harness.Error.Output);
        Assert.Empty(harness.Runs);
    }

    // The tool prints this attribute for --version, and here the entry assembly is the test host's.
    [Fact]
    public void InformationalVersion_ToolAssembly_IsBareVersion()
    {
        AssemblyInformationalVersionAttribute? attribute = typeof(CommandLine).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        Assert.Equal("0.1.0", attribute?.InformationalVersion);
    }

    private sealed class Harness : IDisposable
    {
        private const int Width = 400;

        public Harness()
        {
            Output.Profile.Width = Width;
            Error.Profile.Width = Width;
        }

        public TemporaryDirectory Directory { get; } = new();

        public TestConsole Output { get; } = new();

        public TestConsole Error { get; } = new();

        public MemoryStream StandardOutput { get; } = new();

        public List<RunOptions> Runs { get; } = [];

        /// <summary>
        /// What a run does with the token it is given. By default it returns success at once.
        /// </summary>
        public Func<CancellationToken, Task<int>> Run { get; init; } = _ => Task.FromResult(ExitCodes.Success);

        /// <summary>
        /// The token given to the command line.
        /// </summary>
        public CancellationToken CancellationToken { get; init; } = TestContext.Current.CancellationToken;

        public string[] ErrorLines
        {
            get
            {
                // Every rendered line ends with a line break, so the text after the last one is not a line.
                string[] lines = Error.Output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

                return lines[..^1];
            }
        }

        public Task<string> WriteFileAsync(string fileName) =>
            Directory.WriteFileAsync(fileName, "GET http://localhost/", TestContext.Current.CancellationToken);

        public Task<int> InvokeAsync(params string[] args) =>
            CommandLine.InvokeAsync(
                args,
                (options, runCancellationToken) =>
                {
                    Runs.Add(options);

                    return Run(runCancellationToken);
                },
                new ConsoleStreams(StandardOutput, Output, Error, IsInputRedirected: false, IsOutputRedirected: false),
                CancellationToken);

        public void Dispose()
        {
            Output.Dispose();
            Error.Dispose();
            StandardOutput.Dispose();
            Directory.Dispose();
        }
    }
}
