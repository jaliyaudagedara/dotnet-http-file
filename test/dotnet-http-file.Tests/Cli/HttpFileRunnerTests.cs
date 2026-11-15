using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using DotNetHttpFile.Cli;
using DotNetHttpFile.Environments;
using DotNetHttpFile.Execution;
using DotNetHttpFile.Output;
using DotNetHttpFile.Parsing;
using DotNetHttpFile.Secrets;
using DotNetHttpFile.Tests.Support;
using Spectre.Console.Testing;

namespace DotNetHttpFile.Tests.Cli;

public sealed class HttpFileRunnerTests
{
    private const string HttpFileName = "MyHttp.http";
    private const string AccessTokenProviderName = "EntraAccessToken";
    private const string KeyVaultProviderName = "AzureKeyVault";
    private const string Token = "real-token-value-123";
    private const string AuthorizationHeader = "Authorization";
    private const string InnerMarker = "inner-marker-4711";

    [Fact]
    public async Task RunAsync_NamedRequest_SendsAndReturnsSuccess()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new();
        string path = await harness.WriteHttpFileAsync(
            $"""
            # @name todos
            GET {server.BaseUri}json
            Accept: application/json

            ###

            # @name other
            GET {server.BaseUri}other
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, "todos", null, null, Insecure: false));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(["GET /json HTTP/1.1"], server.Requests.Select(request => request.RequestLine));
        Assert.Equal($"GET {server.BaseUri}json", harness.OutputLines[0]);
        Assert.Contains(harness.OutputLines, line => line.StartsWith("HTTP/1.1 200 OK", StringComparison.Ordinal));
        Assert.Empty(harness.ErrorLines);
    }

    [Fact]
    public async Task RunAsync_ErrorStatus_ReturnsErrorStatus()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new();
        string path = await harness.WriteHttpFileAsync(
            $"""
            # @name missing
            GET {server.BaseUri}not-found
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, "missing", null, null, Insecure: false));

        Assert.Equal(ExitCodes.ErrorStatus, exitCode);
        Assert.Contains(
            harness.OutputLines,
            line => line.StartsWith("HTTP/1.1 404 Not Found", StringComparison.Ordinal));
        Assert.Contains("\"error\": \"not found\"", harness.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_DependencyReferenced_SendsDependencyFirst()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new();
        string path = await harness.WriteHttpFileAsync(
            $$$"""
            # @name createToDo
            POST {{{server.BaseUri}}}json
            Content-Type: application/json

            {"title": "Write blog post"}

            ###

            # @name getToDo
            GET {{{server.BaseUri}}}todos/{{createToDo.response.body.$.items[1].id}}
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, "getToDo", null, null, Insecure: false));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(
            ["POST /json HTTP/1.1", "GET /todos/2 HTTP/1.1"],
            server.Requests.Select(request => request.RequestLine));
        Assert.StartsWith(
            $"\u2192 createToDo  POST {server.BaseUri}json  200 OK  ",
            harness.OutputLines[0],
            StringComparison.Ordinal);
        Assert.Equal($"GET {server.BaseUri}todos/2", harness.OutputLines[1]);
    }

    [Fact]
    public async Task RunAsync_DependencyFails_ReturnsErrorStatusAndSkipsTarget()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new();
        string path = await harness.WriteHttpFileAsync(
            $$$"""
            # @name createToDo
            POST {{{server.BaseUri}}}not-found

            ###

            # @name getToDo
            GET {{{server.BaseUri}}}todos/{{createToDo.response.body.$.id}}
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, "getToDo", null, null, Insecure: false));

        Assert.Equal(ExitCodes.ErrorStatus, exitCode);
        Assert.Equal(["POST /not-found HTTP/1.1"], server.Requests.Select(request => request.RequestLine));
        Assert.StartsWith("\u2192 createToDo  POST ", Assert.Single(harness.OutputLines), StringComparison.Ordinal);
        Assert.Equal(
            ["error: Dependency 'createToDo' returned 404 Not Found; 'getToDo' was not sent."],
            harness.ErrorLines);
    }

    [Fact]
    public async Task RunAsync_GzipDependency_ChainsFromTheDecodedBody()
    {
        using Harness harness = new();
        List<string> sent = [];
        harness.CreateHandler = _ => new StubHandler((message, _) =>
        {
            sent.Add(message.RequestUri?.AbsolutePath ?? string.Empty);

            return Task.FromResult(sent.Count == 1
                ? GzipResponse(HttpStatusCode.OK, Compression.Gzip("""{"id": 7}"""u8))
                : new HttpResponseMessage(HttpStatusCode.OK));
        });
        string path = await harness.WriteHttpFileAsync(
            """
            # @name createToDo
            POST http://localhost/todos

            ###

            # @name getToDo
            GET http://localhost/todos/{{createToDo.response.body.$.id}}
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, "getToDo", null, null, Insecure: false));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(["/todos", "/todos/7"], sent);
    }

    [Fact]
    public async Task RunAsync_ResponseThatCannotBeDecoded_ReturnsErrorStatusAndPrintsOnlyTheError()
    {
        using Harness harness = new();
        harness.CreateHandler = _ => new StubHandler((_, _) =>
            Task.FromResult(GzipResponse(HttpStatusCode.OK, "not gzip"u8.ToArray())));
        string path = await harness.WriteHttpFileAsync("GET http://localhost/todos");

        int exitCode = await harness.RunAsync(new RunOptions(path, null, null, null, Insecure: false));

        Assert.Equal(ExitCodes.ErrorStatus, exitCode);
        Assert.Empty(harness.OutputLines);
        Assert.Equal(
            [
                "error: Could not decode the response to GET http://localhost/todos (200): "
                + "the body is not valid gzip data.",
            ],
            harness.ErrorLines);
    }

    [Fact]
    public async Task RunAsync_DependencyThatCannotBeDecoded_ReturnsErrorStatusAndSkipsTarget()
    {
        using Harness harness = new();
        List<string> sent = [];
        harness.CreateHandler = _ => new StubHandler((message, _) =>
        {
            sent.Add(message.RequestUri?.AbsolutePath ?? string.Empty);

            return Task.FromResult(GzipResponse(HttpStatusCode.OK, "not gzip"u8.ToArray()));
        });
        string path = await harness.WriteHttpFileAsync(
            """
            # @name createToDo
            POST http://localhost/todos

            ###

            # @name getToDo
            GET http://localhost/todos/{{createToDo.response.body.$.id}}
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, "getToDo", null, null, Insecure: false));

        Assert.Equal(ExitCodes.ErrorStatus, exitCode);
        Assert.Equal(["/todos"], sent);
        Assert.Empty(harness.OutputLines);
        Assert.Equal(
            [
                "error: Could not decode the response to POST http://localhost/todos (200): "
                + "the body is not valid gzip data.",
            ],
            harness.ErrorLines);
    }

    [Fact]
    public async Task RunAsync_NoNameRedirected_ReturnsNotSentWithNames()
    {
        using Harness harness = new() { IsOutputRedirected = true };
        string path = await harness.WriteHttpFileAsync(
            """
            # @name beta
            GET http://localhost/b

            ###

            GET http://localhost/unnamed

            ###

            # @name alpha
            GET http://localhost/a
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, null, null, null, Insecure: false));

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Equal(
            [
                $"error: {path} has 3 requests; pick one with --name. Named requests: beta, alpha. "
                    + "Requests without a name can't be picked from a script; add '# @name <name>' above them.",
            ],
            harness.ErrorLines);
        Assert.Empty(harness.Picker.Offered);
        Assert.Equal(0, harness.HandlersCreated);
    }

    [Fact]
    public async Task RunAsync_NoNameInputRedirected_DoesNotUsePicker()
    {
        using Harness harness = new() { IsInputRedirected = true };
        string path = await harness.WriteHttpFileAsync(
            """
            GET http://localhost/a

            ###

            GET http://localhost/b
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, null, null, null, Insecure: false));

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Equal(
            [
                $"error: {path} has 2 requests and none has a name; "
                    + "add '# @name <name>' above one and pick it with --name.",
            ],
            harness.ErrorLines);
        Assert.Empty(harness.Picker.Offered);
    }

    [Fact]
    public async Task RunAsync_NoNameInteractive_UsesPicker()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new();
        harness.Picker.Choose = requests => requests[1];
        string path = await harness.WriteHttpFileAsync(
            $"""
            # @name first
            GET {server.BaseUri}first

            ###

            GET {server.BaseUri}second
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, null, null, null, Insecure: false));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(["first", null], harness.Picker.Offered.Select(request => request.Name));
        Assert.Equal(["GET /second HTTP/1.1"], server.Requests.Select(request => request.RequestLine));
    }

    [Fact]
    public async Task RunAsync_PickerAborted_ReturnsNotSent()
    {
        using Harness harness = new();
        harness.Picker.Choose = _ => null;
        string path = await harness.WriteHttpFileAsync(
            """
            GET http://localhost/a

            ###

            GET http://localhost/b
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, null, null, null, Insecure: false));

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Equal(["error: No request was selected."], harness.ErrorLines);
        Assert.Equal(0, harness.HandlersCreated);
    }

    [Fact]
    public async Task RunAsync_SingleRequestNoName_RunsIt()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new() { IsInputRedirected = true };
        string path = await harness.WriteHttpFileAsync($"GET {server.BaseUri}only");

        int exitCode = await harness.RunAsync(new RunOptions(path, null, null, null, Insecure: false));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(["GET /only HTTP/1.1"], server.Requests.Select(request => request.RequestLine));
        Assert.Empty(harness.Picker.Offered);
    }

    [Fact]
    public async Task RunAsync_UnknownName_ReturnsNotSent()
    {
        using Harness harness = new();
        string path = await harness.WriteHttpFileAsync(
            """
            # @name alpha
            GET http://localhost/a

            ###

            # @name beta
            GET http://localhost/b
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, "Alpha", null, null, Insecure: false));

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Equal(
            [$"error: No request named 'Alpha' in {path}. Named requests: alpha, beta."],
            harness.ErrorLines);
        Assert.Equal(0, harness.HandlersCreated);
    }

    [Fact]
    public async Task RunAsync_UnknownNameNoNamedRequests_ReturnsNotSent()
    {
        using Harness harness = new();
        string path = await harness.WriteHttpFileAsync("GET http://localhost/a");

        int exitCode = await harness.RunAsync(new RunOptions(path, "todos", null, null, Insecure: false));

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Equal(
            [$"error: No request named 'todos' in {path}, which has no named requests."],
            harness.ErrorLines);
    }

    [Fact]
    public async Task RunAsync_NoRequests_ReturnsNotSent()
    {
        using Harness harness = new();
        string path = await harness.WriteHttpFileAsync("@host = http://localhost");

        int exitCode = await harness.RunAsync(new RunOptions(path, null, null, null, Insecure: false));

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Equal([$"error: {path} has no requests."], harness.ErrorLines);
    }

    [Fact]
    public async Task RunAsync_ProviderVariable_UsesEnvironmentAndMasks()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new();
        harness.Providers.Add(new FakeSecretProvider(AccessTokenProviderName, _ => Token));
        await harness.WriteFileAsync(
            EnvironmentFileLocator.FileName,
            """
            {
              "dev": {
                "token": {
                  "provider": "EntraAccessToken",
                  "scope": "api://todos/.default"
                }
              }
            }
            """);
        string path = await harness.WriteHttpFileAsync(
            $$$"""
            # @name todos
            GET {{{server.BaseUri}}}echo
            Authorization: Bearer {{token}}
            X-Api-Key: {{token}}
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, "todos", "dev", null, Insecure: false));

        Assert.Equal(ExitCodes.Success, exitCode);
        RecordedRequest received = Assert.Single(server.Requests);
        Assert.Equal([$"Bearer {Token}"], received.HeaderValues(AuthorizationHeader));
        Assert.Equal("Authorization: Bearer ***", harness.OutputLines[1]);
        Assert.Equal("X-Api-Key: ***", harness.OutputLines[2]);
        Assert.DoesNotContain(Token, harness.Output.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, harness.Error.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_CancelledDuringSend_ReturnsCancelled()
    {
        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        TaskCompletionSource sendStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using Harness harness = new() { CancellationToken = cancellation.Token };
        harness.CreateHandler = _ => new StubHandler(async (_, cancellationToken) =>
        {
            sendStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        string path = await harness.WriteHttpFileAsync("GET http://localhost/slow");

        Task<int> run = harness.RunAsync(new RunOptions(path, null, null, null, Insecure: false));
        await sendStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        int exitCode = await run;

        Assert.Equal(ExitCodes.Cancelled, exitCode);
        Assert.Equal(["error: cancelled"], harness.ErrorLines);
        Assert.Empty(harness.OutputLines);
    }

    [Fact]
    public async Task RunAsync_HttpFileExceptionWithInnerException_PrintsOnlyTheMessage()
    {
        using Harness harness = new();
        harness.Providers.Add(new FakeSecretProvider(
            AccessTokenProviderName,
            _ => throw new HttpFileException("Could not get a token.", new InvalidOperationException(InnerMarker))));
        string path = await harness.WriteProviderFilesAsync();

        int exitCode = await harness.RunAsync(new RunOptions(path, null, "dev", null, Insecure: false));

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Equal(["error: Could not get a token."], harness.ErrorLines);
        Assert.DoesNotContain(InnerMarker, harness.Error.Output + harness.Output.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_UnexpectedException_PrintsTypeAndMessageWithoutStackTrace()
    {
        using Harness harness = new();
        harness.Providers.Add(new FakeSecretProvider(
            AccessTokenProviderName,
            _ => throw new InvalidOperationException("Something broke.")));
        string path = await harness.WriteProviderFilesAsync();

        int exitCode = await harness.RunAsync(new RunOptions(path, null, "dev", null, Insecure: false));

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Equal(["error: unexpected InvalidOperationException: Something broke."], harness.ErrorLines);
    }

    [Fact]
    public async Task RunAsync_MissingHttpFile_ReturnsNotSentNamingTheFile()
    {
        using Harness harness = new();
        string path = Path.Combine(harness.Directory.DirectoryPath, HttpFileName);

        int exitCode = await harness.RunAsync(new RunOptions(path, null, null, null, Insecure: false));

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Equal([$"error: File '{path}' was not found."], harness.ErrorLines);
    }

    [Fact]
    public async Task RunAsync_HttpFileIsDirectory_ReturnsNotSentNamingTheFile()
    {
        using Harness harness = new();
        string path = harness.Directory.CreateDirectory(HttpFileName);

        int exitCode = await harness.RunAsync(new RunOptions(path, null, null, null, Insecure: false));

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Equal([$"error: File '{path}' was not found."], harness.ErrorLines);
    }

    [Fact]
    public async Task RunAsync_UnreadableHttpFile_ReturnsNotSentNamingTheFile()
    {
        const string ContentMarker = "content-marker-9000";
        using Harness harness = new();
        string path = await harness.WriteHttpFileAsync($"GET http://localhost/{ContentMarker}");

        int exitCode;
        using (MakeUnreadable(path))
        {
            exitCode = await harness.RunAsync(new RunOptions(path, null, null, null, Insecure: false));
        }

        Assert.Equal(ExitCodes.NotSent, exitCode);
        string error = Assert.Single(harness.ErrorLines);
        Assert.StartsWith("error: Could not read a file: ", error, StringComparison.Ordinal);
        Assert.Contains(path, error, StringComparison.Ordinal);
        Assert.DoesNotContain(ContentMarker, harness.Error.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_UnreadableEnvironmentFile_ReturnsNotSentNamingTheFile()
    {
        using Harness harness = new();
        string environmentFilePath = await harness.WriteFileAsync(
            EnvironmentFileLocator.FileName,
            """{ "dev": { "host": "http://localhost" } }""");
        string path = await harness.WriteHttpFileAsync("GET {{host}}/a");

        int exitCode;
        using (MakeUnreadable(environmentFilePath))
        {
            exitCode = await harness.RunAsync(new RunOptions(path, null, "dev", null, Insecure: false));
        }

        Assert.Equal(ExitCodes.NotSent, exitCode);
        string error = Assert.Single(harness.ErrorLines);
        Assert.StartsWith("error: Could not read a file: ", error, StringComparison.Ordinal);
        Assert.Contains(environmentFilePath, error, StringComparison.Ordinal);
        Assert.Equal(0, harness.HandlersCreated);
    }

    [Fact]
    public async Task RunAsync_UnreadableDotEnvFile_ReturnsNotSentNamingTheFile()
    {
        using Harness harness = new();
        string dotEnvPath = await harness.WriteFileAsync(".env", "TODO_ID=17");
        string path = await harness.WriteHttpFileAsync("GET http://localhost/todos/{{$dotenv TODO_ID}}");

        int exitCode;
        using (MakeUnreadable(dotEnvPath))
        {
            exitCode = await harness.RunAsync(new RunOptions(path, null, null, null, Insecure: false));
        }

        Assert.Equal(ExitCodes.NotSent, exitCode);
        string error = Assert.Single(harness.ErrorLines);
        Assert.StartsWith("error: Could not read a file: ", error, StringComparison.Ordinal);
        Assert.Contains(dotEnvPath, error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_AggregateException_PrintsInnerTypeNamesOnly()
    {
        using Harness harness = new();
        harness.Providers.Add(new FakeSecretProvider(
            AccessTokenProviderName,
            _ => throw new AggregateException(
                new InvalidOperationException(InnerMarker),
                new AggregateException(new TimeoutException(InnerMarker)))));
        string path = await harness.WriteProviderFilesAsync();

        int exitCode = await harness.RunAsync(new RunOptions(path, null, "dev", null, Insecure: false));

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Equal(
            ["error: unexpected AggregateException: InvalidOperationException, TimeoutException"],
            harness.ErrorLines);
        Assert.DoesNotContain(InnerMarker, harness.Error.Output + harness.Output.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "error: Vault said ***.")]
    [InlineData(false, "error: unexpected InvalidOperationException: Vault said ***.")]
    public async Task RunAsync_ErrorMessageHoldsResolvedSecret_MasksIt(bool isExpectedFailure, string expected)
    {
        using Harness harness = new();
        harness.Providers.Add(new FakeSecretProvider(AccessTokenProviderName, _ => Token));
        harness.Providers.Add(new FakeSecretProvider(
            KeyVaultProviderName,
            _ => throw (isExpectedFailure
                ? new HttpFileException($"Vault said {Token}.")
                : new InvalidOperationException($"Vault said {Token}."))));
        await harness.WriteFileAsync(
            EnvironmentFileLocator.FileName,
            """
            {
              "dev": {
                "token": { "provider": "EntraAccessToken" },
                "apiKey": { "provider": "AzureKeyVault" }
              }
            }
            """);
        string path = await harness.WriteHttpFileAsync(
            """
            GET http://localhost/a
            Authorization: Bearer {{token}}
            X-Api-Key: {{apiKey}}
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, null, "dev", null, Insecure: false));

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Equal([expected], harness.ErrorLines);
        Assert.DoesNotContain(Token, harness.Error.Output + harness.Output.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ProviderFailsAfterCancellation_ReturnsCancelled()
    {
        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using Harness harness = new() { CancellationToken = cancellation.Token };
        harness.Providers.Add(new FakeSecretProvider(
            AccessTokenProviderName,
            _ =>
            {
                cancellation.Cancel();

                throw new HttpFileException("Could not sign in.");
            }));
        string path = await harness.WriteProviderFilesAsync();

        int exitCode = await harness.RunAsync(new RunOptions(path, null, "dev", null, Insecure: false));

        Assert.Equal(ExitCodes.Cancelled, exitCode);
        Assert.Equal(["error: cancelled"], harness.ErrorLines);
    }

    [Fact]
    public async Task RunAsync_HeaderLeftOut_RendersWarning()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new();
        string path = await harness.WriteHttpFileAsync(
            $"""
            GET {server.BaseUri}echo
            Content-Type: application/json
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, null, null, null, Insecure: false));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(
            [
                "warning: Header 'Content-Type' ignored: a request without a body cannot have content headers "
                    + "(line 2).",
            ],
            harness.ErrorLines);
    }

    [Fact]
    public async Task RunAsync_OutputRedirected_WritesBodyBytesOnly()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new() { IsOutputRedirected = true };
        string path = await harness.WriteHttpFileAsync($"GET {server.BaseUri}json");

        int exitCode = await harness.RunAsync(new RunOptions(path, null, null, null, Insecure: false));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(EchoServer.JsonBody, Encoding.UTF8.GetString(harness.StandardOutput.ToArray()));
        Assert.Empty(harness.OutputLines);
        Assert.StartsWith("HTTP/1.1 200 OK", Assert.Single(harness.ErrorLines), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ForcedFullModeWhenRedirected_PrintsFullView()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new() { IsOutputRedirected = true };
        string path = await harness.WriteHttpFileAsync($"GET {server.BaseUri}json");

        int exitCode = await harness.RunAsync(new RunOptions(path, null, null, OutputMode.Full, Insecure: false));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal($"GET {server.BaseUri}json", harness.OutputLines[0]);
        Assert.Equal(0, harness.StandardOutput.Length);
    }

    [Fact]
    public async Task RunAsync_ForcedBodyModeInTerminal_WritesBodyBytesOnly()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new();
        string path = await harness.WriteHttpFileAsync($"GET {server.BaseUri}json");

        int exitCode = await harness.RunAsync(new RunOptions(path, null, null, OutputMode.Body, Insecure: false));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(EchoServer.JsonBody, Encoding.UTF8.GetString(harness.StandardOutput.ToArray()));
        Assert.Empty(harness.OutputLines);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_BodyModeEchoesSecret_WritesBytesUnmaskedAndWarns(bool forced)
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new() { IsOutputRedirected = !forced };
        harness.Providers.Add(new FakeSecretProvider(AccessTokenProviderName, _ => Token));
        await harness.WriteFileAsync(
            EnvironmentFileLocator.FileName,
            """{ "dev": { "token": { "provider": "EntraAccessToken" } } }""");
        string path = await harness.WriteHttpFileAsync(
            $$$"""
            GET {{{server.BaseUri}}}echo
            X-Api-Key: {{token}}
            """);

        int exitCode = await harness.RunAsync(
            new RunOptions(path, null, "dev", forced ? OutputMode.Body : null, Insecure: false));

        Assert.Equal(ExitCodes.Success, exitCode);
        string body = Encoding.UTF8.GetString(harness.StandardOutput.ToArray());
        Assert.Contains($"\"{Token}\"", body, StringComparison.Ordinal);
        Assert.Equal(2, harness.ErrorLines.Length);
        Assert.StartsWith("HTTP/1.1 200 OK", harness.ErrorLines[0], StringComparison.Ordinal);
        Assert.Equal(
            "warning: the response body contains a secret value from this run; it is written unmasked.",
            harness.ErrorLines[1]);
        Assert.Empty(harness.OutputLines);
    }

    [Fact]
    public async Task RunAsync_BodyModeWithSecretThatIsNotEchoed_DoesNotWarn()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new() { IsOutputRedirected = true };
        harness.Providers.Add(new FakeSecretProvider(AccessTokenProviderName, _ => Token));
        await harness.WriteFileAsync(
            EnvironmentFileLocator.FileName,
            """{ "dev": { "token": { "provider": "EntraAccessToken" } } }""");
        string path = await harness.WriteHttpFileAsync(
            $$$"""
            GET {{{server.BaseUri}}}json
            X-Api-Key: {{token}}
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, null, "dev", null, Insecure: false));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(EchoServer.JsonBody, Encoding.UTF8.GetString(harness.StandardOutput.ToArray()));
        Assert.StartsWith("HTTP/1.1 200 OK", Assert.Single(harness.ErrorLines), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_HeadersMode_PrintsStatusLineAndMaskedResponseHeaders()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new();
        harness.Providers.Add(new FakeSecretProvider(AccessTokenProviderName, _ => Token));
        await harness.WriteFileAsync(
            EnvironmentFileLocator.FileName,
            """{ "dev": { "token": { "provider": "EntraAccessToken" } } }""");
        string url = server.RedirectUrl(200, "key=").AbsoluteUri;
        string path = await harness.WriteHttpFileAsync(
            $$$"""
            GET {{{url}}}{{token}}
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, null, "dev", OutputMode.Headers, Insecure: false));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal("HTTP/1.1 200 OK", harness.OutputLines[0]);
        Assert.Contains("Location: key=***", harness.OutputLines);
        Assert.All(harness.OutputLines[1..], line => Assert.Contains(": ", line, StringComparison.Ordinal));
        Assert.DoesNotContain(Token, harness.Output.Output + harness.Error.Output, StringComparison.Ordinal);
        Assert.Empty(harness.ErrorLines);
        Assert.Equal(0, harness.StandardOutput.Length);
    }

    [Theory]
    [InlineData("json", "HTTP/1.1 200 OK", ExitCodes.Success)]
    [InlineData("not-found", "HTTP/1.1 404 Not Found", ExitCodes.ErrorStatus)]
    public async Task RunAsync_StatusMode_PrintsOneLineAndKeepsTheExitCode(
        string route,
        string statusLine,
        int expectedExitCode)
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new();
        string path = await harness.WriteHttpFileAsync($"GET {server.BaseUri}{route}");

        int exitCode = await harness.RunAsync(new RunOptions(path, null, null, OutputMode.Status, Insecure: false));

        Assert.Equal(expectedExitCode, exitCode);
        Assert.Equal([statusLine], harness.OutputLines);
        Assert.Empty(harness.ErrorLines);
        Assert.Equal(0, harness.StandardOutput.Length);
    }

    [Fact]
    public async Task RunAsync_HeadersModeErrorStatus_ReturnsErrorStatus()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new();
        string path = await harness.WriteHttpFileAsync($"GET {server.BaseUri}not-found");

        int exitCode = await harness.RunAsync(new RunOptions(path, null, null, OutputMode.Headers, Insecure: false));

        Assert.Equal(ExitCodes.ErrorStatus, exitCode);
        Assert.Equal("HTTP/1.1 404 Not Found", harness.OutputLines[0]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunAsync_HeadersOrStatusModeWithDependency_WritesDependencyLineToError(bool headers)
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new();
        string path = await harness.WriteHttpFileAsync(
            $$$"""
            # @name createToDo
            POST {{{server.BaseUri}}}json

            ###

            # @name getToDo
            GET {{{server.BaseUri}}}json?id={{createToDo.response.body.$.items[1].id}}
            """);
        OutputMode mode = headers ? OutputMode.Headers : OutputMode.Status;

        int exitCode = await harness.RunAsync(new RunOptions(path, "getToDo", null, mode, Insecure: false));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal("HTTP/1.1 200 OK", harness.OutputLines[0]);
        Assert.StartsWith(
            $"\u2192 createToDo  POST {server.BaseUri}json  200 OK  ",
            Assert.Single(harness.ErrorLines),
            StringComparison.Ordinal);
        Assert.DoesNotContain("\u2192", harness.Output.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunAsync_Insecure_IsPassedToTheHandlerFactory(bool insecure)
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new();
        string path = await harness.WriteHttpFileAsync($"GET {server.BaseUri}json");

        await harness.RunAsync(new RunOptions(path, null, null, null, insecure));

        Assert.Equal([insecure], harness.InsecureFlags);
    }

    [Fact]
    public async Task RunAsync_DependencyChain_SharesOneHandler()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new();
        string path = await harness.WriteHttpFileAsync(
            $$$"""
            # @name first
            GET {{{server.BaseUri}}}json

            ###

            # @name second
            GET {{{server.BaseUri}}}echo?n={{first.response.body.$.num}}
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, "second", null, null, Insecure: false));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(2, server.Requests.Count);
        Assert.Equal(1, harness.HandlersCreated);
    }

    [Fact]
    public async Task RunAsync_DotEnvVariable_ReadsDotEnvFromTheProjectFolder()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new();
        await harness.WriteFileAsync("Api.csproj", "<Project />");
        await harness.WriteFileAsync(".env", "TODO_ID=17");
        string path = await harness.WriteFileAsync(
            Path.Combine("Requests", HttpFileName),
            $$$"""
            GET {{{server.BaseUri}}}todos/{{$dotenv TODO_ID}}?again={{$dotenv TODO_ID}}
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, null, null, null, Insecure: false));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(["GET /todos/17?again=17 HTTP/1.1"], server.Requests.Select(request => request.RequestLine));
    }

    [Fact]
    public async Task RunAsync_ProcessEnvVariable_UsesTheGivenEnvironment()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new();
        harness.EnvironmentVariables["TODO_ID"] = "23";
        string path = await harness.WriteHttpFileAsync(
            $$$"""
            GET {{{server.BaseUri}}}todos/{{$processEnv TODO_ID}}
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, null, null, null, Insecure: false));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(["GET /todos/23 HTTP/1.1"], server.Requests.Select(request => request.RequestLine));
    }

    [Fact]
    public async Task RunAsync_FileVariableNestingVariablesAndDynamicValue_SendsComposedValue()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new();
        string path = await harness.WriteHttpFileAsync(
            $$$"""
            @user=jaliya
            @postfix={{$randomInt 10 99}}
            @email={{user}}{{postfix}}@example.com

            GET {{{server.BaseUri}}}echo?email={{email}}
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, null, null, null, Insecure: false));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Matches(
            @"^GET /echo\?email=jaliya[1-9][0-9]@example\.com HTTP/1\.1$",
            Assert.Single(server.Requests).RequestLine);
    }

    [Fact]
    public async Task RunAsync_FileVariableReadingLaterResponseUnused_DoesNotBlockEarlierRequest()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new();
        string path = await harness.WriteHttpFileAsync(
            $$$"""
            @fromLater={{later.response.body.$.num}}

            # @name first
            GET {{{server.BaseUri}}}echo/first

            ###

            # @name later
            GET {{{server.BaseUri}}}json
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, "first", null, null, Insecure: false));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(["GET /echo/first HTTP/1.1"], server.Requests.Select(request => request.RequestLine));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" HTTP/1.1")]
    public async Task RunAsync_UrlWithSpacesAndNonAscii_SendsPercentEncodedTarget(string httpVersion)
    {
        const string Cafe = "café";
        const string Jose = "José";
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new();
        string path = await harness.WriteHttpFileAsync(
            $"GET {server.BaseUri}echo/my file/{Cafe}?q=a b&name={Jose}{httpVersion}");

        int exitCode = await harness.RunAsync(new RunOptions(path, null, null, null, Insecure: false));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(
            "GET /echo/my%20file/caf%C3%A9?q=a%20b&name=Jos%C3%A9 HTTP/1.1",
            Assert.Single(server.Requests).RequestLine);
    }

    [Fact]
    public async Task RunAsync_SecretInUrlEchoedInUriEscaping_MasksItInTheBody()
    {
        const string UrlSecret = "p@ss w/rd+1";
        const string SentForm = "p@ss%20w/rd+1";
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new();
        harness.Providers.Add(new FakeSecretProvider(AccessTokenProviderName, _ => UrlSecret));
        await harness.WriteFileAsync(
            EnvironmentFileLocator.FileName,
            """{ "dev": { "token": { "provider": "EntraAccessToken" } } }""");
        string path = await harness.WriteHttpFileAsync(
            $$$"""
            GET {{{server.BaseUri}}}echo/{{token}}?key={{token}}
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, null, "dev", null, Insecure: false));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal($"GET /echo/{SentForm}?key={SentForm} HTTP/1.1", Assert.Single(server.Requests).RequestLine);
        Assert.Equal($"GET {server.BaseUri}echo/***?key=***", harness.OutputLines[0]);
        Assert.Contains(
            "\"request_line\": \"GET /echo/***?key=*** HTTP/1.1\"",
            harness.OutputText,
            StringComparison.Ordinal);
        Assert.DoesNotContain("p@ss", harness.Output.Output + harness.Error.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_CrossOriginRedirect_DropsCredentialsAndPrintsTheMaskedRedirect()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        await using EchoServer otherOrigin = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new();
        harness.Providers.Add(new FakeSecretProvider(AccessTokenProviderName, _ => Token));
        await harness.WriteFileAsync(
            EnvironmentFileLocator.FileName,
            """{ "dev": { "token": { "provider": "EntraAccessToken" } } }""");
        string redirectUrl = server.RedirectUrl(302, $"{otherOrigin.BaseUri}echo?key=").AbsoluteUri;
        string path = await harness.WriteHttpFileAsync(
            $$$"""
            GET {{{redirectUrl}}}{{token}}
            Authorization: Bearer {{token}}
            Cookie: session=written
            X-Api-Key: {{token}}
            X-Plain: plain
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, null, "dev", null, Insecure: false));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal([$"Bearer {Token}"], Assert.Single(server.Requests).HeaderValues(AuthorizationHeader));
        RecordedRequest followed = Assert.Single(otherOrigin.Requests);
        Assert.Equal($"GET /echo?key={Token} HTTP/1.1", followed.RequestLine);
        Assert.Empty(followed.HeaderValues(AuthorizationHeader));
        Assert.Empty(followed.HeaderValues("Cookie"));
        Assert.Empty(followed.HeaderValues("X-Api-Key"));
        Assert.Equal(["plain"], followed.HeaderValues("X-Plain"));
        Assert.Equal([$"Redirected to {otherOrigin.BaseUri}echo?key=*** (302)"], harness.ErrorLines);
        Assert.Equal(
            [$"GET {redirectUrl}***", "Authorization: Bearer ***", "Cookie: ***", "X-Api-Key: ***", "X-Plain: plain"],
            harness.OutputLines[..5]);
        Assert.DoesNotContain(Token, harness.Output.Output + harness.Error.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_RedirectWhenOutputIsRedirected_KeepsStandardOutputByteExact()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new() { IsOutputRedirected = true };
        string path = await harness.WriteHttpFileAsync($"GET {server.RedirectUrl(307, "/json")}");

        int exitCode = await harness.RunAsync(new RunOptions(path, null, null, null, Insecure: false));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(EchoServer.JsonBody, Encoding.UTF8.GetString(harness.StandardOutput.ToArray()));
        Assert.Equal($"Redirected to {server.BaseUri}json (307)", harness.ErrorLines[0]);
        Assert.StartsWith("HTTP/1.1 200 OK", harness.ErrorLines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ProviderThrowsIOException_IsNotReportedAsAFileError()
    {
        using Harness harness = new();
        harness.Providers.Add(new FakeSecretProvider(
            AccessTokenProviderName,
            _ => throw new IOException("The connection was reset.")));
        string path = await harness.WriteProviderFilesAsync();

        int exitCode = await harness.RunAsync(new RunOptions(path, null, "dev", null, Insecure: false));

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Equal(["error: unexpected IOException: The connection was reset."], harness.ErrorLines);
    }

    [Fact]
    public async Task RunAsync_DotEnvFileMissing_ReturnsNotSentNamingThePath()
    {
        using Harness harness = new();
        string path = await harness.WriteHttpFileAsync("GET http://localhost/todos/{{$dotenv TODO_ID}}");
        string dotEnvPath = Path.Combine(harness.Directory.DirectoryPath, ".env");

        int exitCode = await harness.RunAsync(new RunOptions(path, null, null, null, Insecure: false));

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Equal(
            [$"error: 'TODO_ID' cannot be read with $dotenv: file {dotEnvPath} was not found."],
            harness.ErrorLines);
    }

    [Fact]
    public async Task RunAsync_DotEnvNameNotDefined_ReturnsNotSentNamingThePath()
    {
        using Harness harness = new();
        string dotEnvPath = await harness.WriteFileAsync(".env", "OTHER=1");
        string path = await harness.WriteHttpFileAsync("GET http://localhost/todos/{{$dotenv TODO_ID}}");

        int exitCode = await harness.RunAsync(new RunOptions(path, null, null, null, Insecure: false));

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Equal([$"error: 'TODO_ID' is not defined in {dotEnvPath}."], harness.ErrorLines);
    }

    [Fact]
    public async Task RunAsync_GraphQLRequest_SendsTheEnvelopeWithoutTheMarker()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new();
        string path = await harness.WriteHttpFileAsync(
            $$$"""
            @heroId = 1000

            POST {{{server.BaseUri}}}graphql
            X-Request-Type: GraphQL

            query Hero($id: ID!) { hero(id: $id) { name } }

            { "id": "{{heroId}}" }
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, null, null, null, Insecure: false));

        Assert.Equal(ExitCodes.Success, exitCode);
        RecordedRequest received = Assert.Single(server.Requests);
        Assert.Equal(
            """{"query":"query Hero($id: ID!) { hero(id: $id) { name } }","variables":{"id":"1000"},"operationName":"Hero"}""",
            Encoding.UTF8.GetString(received.Body.Span));
        Assert.Equal(["application/json"], received.HeaderValues("Content-Type"));
        Assert.Empty(received.HeaderValues("X-Request-Type"));
        Assert.Equal(
            [$"POST {server.BaseUri}graphql", "Content-Type: application/json", string.Empty],
            harness.OutputLines[..3]);
    }

    [Fact]
    public async Task RunAsync_GraphQLDependency_ReferencesSeeWhatWasSent()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new();
        string path = await harness.WriteHttpFileAsync(
            $$$"""
            # @name gql
            POST {{{server.BaseUri}}}graphql
            X-Request-Type: graphql

            query Hero { hero { name } }

            ###

            # @name next
            GET {{{server.BaseUri}}}echo
            X-Operation: {{gql.request.body.$.operationName}}
            X-Type: {{gql.request.headers.Content-Type}}
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, "next", null, null, Insecure: false));

        Assert.Equal(ExitCodes.Success, exitCode);
        RecordedRequest received = server.Requests[^1];
        Assert.Equal(["Hero"], received.HeaderValues("X-Operation"));
        Assert.Equal(["application/json"], received.HeaderValues("X-Type"));
    }

    [Fact]
    public async Task RunAsync_GraphQLDependencyMarkerReferenced_IsNotFound()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new();
        string path = await harness.WriteHttpFileAsync(
            $$$"""
            # @name gql
            POST {{{server.BaseUri}}}graphql
            X-Request-Type: graphql

            { hero { name } }

            ###

            # @name next
            GET {{{server.BaseUri}}}echo
            X-Type: {{gql.request.headers.X-Request-Type}}
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, "next", null, null, Insecure: false));

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task RunAsync_GraphQLSecretEscapedInTheEnvelope_MasksIt()
    {
        const string QuotedToken = "pa\"ss\\word-123";
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new();
        harness.Providers.Add(new FakeSecretProvider(AccessTokenProviderName, _ => QuotedToken));
        await harness.WriteFileAsync(
            EnvironmentFileLocator.FileName,
            """{ "dev": { "token": { "provider": "EntraAccessToken" } } }""");
        string path = await harness.WriteHttpFileAsync(
            $$$"""
            # @name gql
            POST {{{server.BaseUri}}}graphql
            X-Request-Type: graphql

            { login(password: "{{token}}") }

            ###

            # @name next
            GET {{{server.BaseUri}}}echo
            X-Sent: {{gql.request.body.*}}
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, "next", "dev", null, Insecure: false));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(
            ["""{"query":"{ login(password: \"pa\"ss\\word-123\") }"}"""],
            server.Requests[^1].HeaderValues("X-Sent"));
        Assert.Contains("""X-Sent: {"query":"{ login(password: \"***\") }"}""", harness.OutputLines);
        Assert.DoesNotContain("word-123", harness.Output.Output + harness.Error.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_GraphQLVariablesNotAnObject_ReturnsNotSent()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using Harness harness = new();
        string path = await harness.WriteHttpFileAsync(
            $$$"""
            POST {{{server.BaseUri}}}graphql
            X-Request-Type: graphql

            { hero { name } }

            [1, 2]
            """);

        int exitCode = await harness.RunAsync(new RunOptions(path, null, null, null, Insecure: false));

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Empty(server.Requests);
        Assert.Equal(
            ["error: The GraphQL variables of the request on line 1 are not a JSON object; the request cannot be sent."],
            harness.ErrorLines);
    }

    // Windows refuses a second open of a file held without sharing; elsewhere the file loses its read permission.
    private static IDisposable MakeUnreadable(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }

        File.SetUnixFileMode(path, UnixFileMode.None);

        return new MemoryStream();
    }

    private sealed class FakePicker : IRequestPicker
    {
        public Func<IReadOnlyList<RequestDefinition>, RequestDefinition?> Choose { get; set; } =
            _ => throw new InvalidOperationException("The picker was not expected to run.");

        public IReadOnlyList<RequestDefinition> Offered { get; private set; } = [];

        public RequestDefinition? Pick(IReadOnlyList<RequestDefinition> requests)
        {
            Offered = requests;

            return Choose(requests);
        }
    }

    private sealed class FakeSecretProvider(string name, Func<SecretRequest, string> resolve) : ISecretProvider
    {
        public string Name => name;

        public Task<string> ResolveAsync(SecretRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(resolve(request));
    }

    private static HttpResponseMessage GzipResponse(HttpStatusCode statusCode, byte[] body)
    {
        ByteArrayContent content = new(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Headers.ContentEncoding.Add("gzip");

        return new HttpResponseMessage(statusCode) { Content = content };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            send(request, cancellationToken);
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

        public FakePicker Picker { get; } = new();

        public List<ISecretProvider> Providers { get; } = [];

        public Dictionary<string, string> EnvironmentVariables { get; } = new(StringComparer.Ordinal);

        public List<bool> InsecureFlags { get; } = [];

        /// <summary>The token given to the run.</summary>
        public CancellationToken CancellationToken { get; init; } = TestContext.Current.CancellationToken;

        public bool IsInputRedirected { get; init; }

        public bool IsOutputRedirected { get; init; }

        public Func<bool, HttpMessageHandler> CreateHandler { get; set; } = ToolHttpClient.CreateHandler;

        public int HandlersCreated => InsecureFlags.Count;

        public string OutputText => Normalize(Output.Output);

        public string[] OutputLines => Lines(Output.Output);

        public string[] ErrorLines => Lines(Error.Output);

        public Task<string> WriteHttpFileAsync(string content) => WriteFileAsync(HttpFileName, content);

        public Task<string> WriteFileAsync(string relativePath, string content) =>
            Directory.WriteFileAsync(relativePath, content, TestContext.Current.CancellationToken);

        /// <summary>Writes a request whose only variable comes from the <c>EntraAccessToken</c> provider.</summary>
        public async Task<string> WriteProviderFilesAsync()
        {
            await WriteFileAsync(
                EnvironmentFileLocator.FileName,
                """{ "dev": { "token": { "provider": "EntraAccessToken" } } }""");

            return await WriteHttpFileAsync(
                """
                GET http://localhost/a
                Authorization: Bearer {{token}}
                """);
        }

        public Task<int> RunAsync(RunOptions options)
        {
            HttpFileRunner runner = new(
                new ConsoleStreams(StandardOutput, Output, Error, IsInputRedirected, IsOutputRedirected),
                Picker,
                new SecretProviderRegistry(Providers),
                insecure =>
                {
                    InsecureFlags.Add(insecure);

                    return CreateHandler(insecure);
                },
                TimeProvider.System,
                name => EnvironmentVariables.GetValueOrDefault(name));

            return runner.RunAsync(options, CancellationToken);
        }

        public void Dispose()
        {
            Output.Dispose();
            Error.Dispose();
            StandardOutput.Dispose();
            Directory.Dispose();
        }

        private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

        // Every rendered line ends with a line break, so the text after the last one is not a line.
        private static string[] Lines(string text)
        {
            string[] lines = Normalize(text).Split('\n');

            return lines[..^1];
        }
    }
}
