using System.Globalization;
using System.Text;
using DotNetHttpFile.Cli;
using DotNetHttpFile.Environments;
using DotNetHttpFile.Execution;
using DotNetHttpFile.Output;
using DotNetHttpFile.Parsing;
using DotNetHttpFile.Secrets;
using DotNetHttpFile.Tests.Support;
using Spectre.Console.Testing;

namespace DotNetHttpFile.Tests.Conformance;

/// <summary>
/// Replays <c>Conformance/VisualStudio/capture.http</c> through the runner and compares what the server received
/// with what Visual Studio 18.13 sent. The capture log and notes are kept outside the repo.
/// </summary>
/// <remarks>
/// Header sets are compared without their order. The headers Visual Studio adds and the tool deliberately
/// does not (<c>traceparent</c>, <c>Accept-Encoding</c>, <c>Expect</c>, <c>Connection</c>) are left out of the
/// expected sets.
/// </remarks>
public sealed class VisualStudioConformanceTests
{
    private const string ContentTypeHeader = "Content-Type";
    private const string ContentLengthHeader = "Content-Length";
    private const string HostHeader = "Host";
    private const string PlainText = "text/plain";
    private const string JsonRequestLine = "GET /json HTTP/1.1";
    private const string CarriageReturnLineFeed = "\r\n";
    private const string LineFeed = "\n";

    [Fact]
    public async Task Case2_TrailingBlankLines_BodyIsLine1CrLfLine2()
    {
        await using Replay replay = await Replay.StartAsync();

        int exitCode = await replay.RunCaseAsync(2);

        Assert.Equal(ExitCodes.Success, exitCode);
        RecordedRequest request = Assert.Single(replay.Requests);
        Assert.Equal("POST /echo?case=2 HTTP/1.1", request.RequestLine);
        Assert.Equal("line1\r\nline2", BodyText(request));
        AssertHeaders(
            replay,
            request,
            (ContentTypeHeader, "text/plain; charset=utf-8"),
            (ContentLengthHeader, "12"));
    }

    [Fact]
    public async Task Case3_SeparatorAfterBody_BodyExact()
    {
        await using Replay replay = await Replay.StartAsync();

        int exitCode = await replay.RunCaseAsync(3);

        Assert.Equal(ExitCodes.Success, exitCode);
        RecordedRequest request = Assert.Single(replay.Requests);
        Assert.Equal("POST /echo?case=3 HTTP/1.1", request.RequestLine);
        Assert.Equal("{\"a\": 1}", BodyText(request));
        AssertHeaders(replay, request, (ContentTypeHeader, "application/json"), (ContentLengthHeader, "8"));
    }

    [Fact]
    public async Task Case4_JsonPathScalarsAndObjects_MatchVs()
    {
        // Visual Studio wrote CRLF inside the values because it ran on Windows; the file's own line breaks are
        // CRLF on every platform.
        string[] lines =
        [
            "obj={\n  \"a\": 1,\n  \"b\": \"two\"\n}",
            "arr=[\n  1,\n  2,\n  3\n]",
            "first={\n  \"id\": 1\n}",
        ];
        string body = string.Join(
            CarriageReturnLineFeed,
            lines.Select(line => line.Replace(LineFeed, Environment.NewLine, StringComparison.Ordinal)));
        await using Replay replay = await Replay.StartAsync();

        int exitCode = await replay.RunCaseAsync(4);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(
            [JsonRequestLine, "POST /echo?case=4 HTTP/1.1"],
            replay.Requests.Select(request => request.RequestLine));
        AssertHeaders(replay, replay.Requests[0]);
        RecordedRequest request = replay.Requests[1];
        Assert.Equal(body, BodyText(request));
        AssertHeaders(
            replay,
            request,
            ("X-Str", "abc def"),
            ("X-Num", "42"),
            ("X-Dec", "3.5"),
            ("X-Bool", "true"),
            ("X-Nul", "null"),
            (ContentTypeHeader, PlainText),
            (ContentLengthHeader, body.Length.ToString(CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task Case5_MultipleMatches_CompactArray()
    {
        await using Replay replay = await Replay.StartAsync();

        int exitCode = await replay.RunCaseAsync(5);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(
            [JsonRequestLine, "POST /echo?case=5 HTTP/1.1"],
            replay.Requests.Select(request => request.RequestLine));
        RecordedRequest request = replay.Requests[1];
        Assert.Equal("ids=[1,2]", BodyText(request));
        AssertHeaders(replay, request, (ContentTypeHeader, PlainText), (ContentLengthHeader, "9"));
    }

    [Fact]
    public async Task Case6_NoMatch_NotSentExit2()
    {
        await using Replay replay = await Replay.StartAsync();

        int exitCode = await replay.RunCaseAsync(6);

        Assert.Equal(ExitCodes.NotSent, exitCode);
        Assert.Equal(2, exitCode);
        Assert.Equal([JsonRequestLine], replay.Requests.Select(request => request.RequestLine));
        Assert.Equal(
            [
                "error: The named request does not contain any content at this path "
                    + "'json.response.body.$.nope'.",
            ],
            replay.ErrorLines);
    }

    [Fact]
    public async Task Case7_WholeBodyAndHeader_Exact()
    {
        await using Replay replay = await Replay.StartAsync();

        int exitCode = await replay.RunCaseAsync(7);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(
            [JsonRequestLine, "POST /echo?case=7 HTTP/1.1"],
            replay.Requests.Select(request => request.RequestLine));
        RecordedRequest request = replay.Requests[1];
        Assert.Equal(Encoding.UTF8.GetBytes(EchoServer.JsonBody), request.Body.ToArray());
        AssertHeaders(
            replay,
            request,
            ("X-CT", "application/json; charset=utf-8"),
            (ContentTypeHeader, PlainText),
            (ContentLengthHeader, "148"));
    }

    [Fact]
    public async Task Case8_Redirect_Followed()
    {
        await using Replay replay = await Replay.StartAsync();

        int exitCode = await replay.RunCaseAsync(8);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(
            ["GET /redirect?case=8 HTTP/1.1", "GET /echo?redirected=1 HTTP/1.1"],
            replay.Requests.Select(request => request.RequestLine));
        AssertHeaders(replay, replay.Requests[0]);
        AssertHeaders(replay, replay.Requests[1]);
    }

    // A deliberate difference: Visual Studio rejects this expression (HTTP0021) and sends nothing.
    [Fact]
    public async Task Case10_XPath_SentWithToken()
    {
        await using Replay replay = await Replay.StartAsync();

        int exitCode = await replay.RunCaseAsync(10);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(
            ["GET /xml HTTP/1.1", "POST /echo?case=10 HTTP/1.1"],
            replay.Requests.Select(request => request.RequestLine));
        RecordedRequest request = replay.Requests[1];
        Assert.Equal(["xml-token-123"], request.HeaderValues("X-Token"));
        Assert.Equal(0, request.Body.Length);
    }

    [Fact]
    public async Task Case11_EnvFileWithUnknownProvider_PlainValueResolves()
    {
        await using Replay replay = await Replay.StartAsync();

        int exitCode = await replay.RunCaseAsync(11, "dev");

        Assert.Equal(ExitCodes.Success, exitCode);
        RecordedRequest request = Assert.Single(replay.Requests);
        Assert.Equal("GET /echo?case=11&plain=plain-value HTTP/1.1", request.RequestLine);
        AssertHeaders(replay, request);
        Assert.Empty(replay.ErrorLines);
    }

    private static string BodyText(RecordedRequest request) => Encoding.UTF8.GetString(request.Body.Span);

    /// <summary>
    /// Asserts that <paramref name="request"/> carried <paramref name="expected"/> and <c>Host</c>, and no
    /// other header. Names are compared without case and the order is ignored: Kestrel keeps neither.
    /// </summary>
    private static void AssertHeaders(
        Replay replay,
        RecordedRequest request,
        params ReadOnlySpan<(string Name, string Value)> expected)
    {
        (string Name, string Value)[] expectedHeaders = [.. expected, (HostHeader, replay.Authority)];

        Assert.Equal(
            HeaderLines(expectedHeaders),
            HeaderLines(request.Headers.Select(header => (header.Key, header.Value))));
    }

    private static string[] HeaderLines(IEnumerable<(string Name, string Value)> headers) =>
        [
            .. headers
                .Select(header => $"{header.Name.ToUpperInvariant()}: {header.Value}")
                .Order(StringComparer.Ordinal),
        ];

    /// <summary>
    /// An echo server and a temporary copy of the Visual Studio capture that points at it.
    /// </summary>
    private sealed class Replay : IAsyncDisposable
    {
        private const string CaptureAddress = "http://localhost:5199";
        private const int Width = 400;

        private readonly TemporaryDirectory _directory = new();
        private readonly TestConsole _output = new();
        private readonly TestConsole _error = new();
        private readonly MemoryStream _standardOutput = new();
        private readonly EchoServer _server;

        private Replay(EchoServer server)
        {
            _server = server;
            _output.Profile.Width = Width;
            _error.Profile.Width = Width;
        }

        /// <summary>
        /// The value the server expects in <c>Host</c>, such as <c>127.0.0.1:50123</c>.
        /// </summary>
        public string Authority => _server.BaseUri.Authority;

        public IReadOnlyList<RecordedRequest> Requests => _server.Requests;

        // Every rendered line ends with a line break, so the text after the last one is not a line.
        public string[] ErrorLines =>
            _error.Output.Replace(CarriageReturnLineFeed, LineFeed, StringComparison.Ordinal).Split(LineFeed)[..^1];

        public static async Task<Replay> StartAsync() =>
            new(await EchoServer.StartAsync(TestContext.Current.CancellationToken));

        /// <summary>
        /// Runs the request that <c>capture.http</c> numbers <paramref name="caseNumber"/>, from a copy of the
        /// capture in which that request has a name and the host is the echo server.
        /// </summary>
        public async Task<int> RunCaseAsync(int caseNumber, string? environmentName = null)
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            string requestName = string.Create(CultureInfo.InvariantCulture, $"case{caseNumber}");
            string caseComment = string.Create(CultureInfo.InvariantCulture, $"# {caseNumber}. ");

            // Read as bytes: the file is CRLF on every platform and the body expectations depend on it.
            string capture = Encoding.UTF8.GetString(
                await File.ReadAllBytesAsync(VisualStudioCapture.CaptureFilePath, cancellationToken));
            Assert.DoesNotContain(
                LineFeed,
                capture.Replace(CarriageReturnLineFeed, string.Empty, StringComparison.Ordinal),
                StringComparison.Ordinal);
            Assert.Equal(1, CountOccurrences(capture, CaptureAddress));
            Assert.Equal(1, CountOccurrences(capture, caseComment));

            string copy = capture
                .Replace(
                    CaptureAddress,
                    _server.BaseUri.GetLeftPart(UriPartial.Authority),
                    StringComparison.Ordinal)
                .Replace(
                    caseComment,
                    $"# @name {requestName}{CarriageReturnLineFeed}{caseComment}",
                    StringComparison.Ordinal);
            string path = await _directory.WriteFileAsync(
                VisualStudioCapture.CaptureFileName,
                copy,
                cancellationToken);
            File.Copy(
                Path.Combine(VisualStudioCapture.DirectoryPath, EnvironmentFileLocator.FileName),
                Path.Combine(_directory.DirectoryPath, EnvironmentFileLocator.FileName));

            // Nothing in these cases uses a provider value, so a provider that runs is a failure: values are
            // resolved only when a request uses them, and no test may reach Azure.
            HttpFileRunner runner = new(
                new ConsoleStreams(
                    _standardOutput,
                    _output,
                    _error,
                    IsInputRedirected: false,
                    IsOutputRedirected: false),
                new UnexpectedPicker(),
                new SecretProviderRegistry(
                [
                    new UnexpectedSecretProvider(EntraAccessTokenProvider.ProviderName),
                    new UnexpectedSecretProvider(AzureKeyVaultProvider.ProviderName),
                ]),
                ToolHttpClient.CreateHandler,
                TimeProvider.System,
                _ => null);

            return await runner.RunAsync(
                new RunOptions(path)
                {
                    RequestName = requestName,
                    EnvironmentName = environmentName,
                },
                cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await _server.DisposeAsync();
            _output.Dispose();
            _error.Dispose();
            await _standardOutput.DisposeAsync();
            _directory.Dispose();
        }

        private static int CountOccurrences(string text, string value) =>
            (text.Length - text.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;
    }

    private sealed class UnexpectedPicker : IRequestPicker
    {
        public RequestDefinition? Pick(IReadOnlyList<RequestDefinition> requests) =>
            throw new InvalidOperationException("The picker was not expected to run.");
    }

    private sealed class UnexpectedSecretProvider(string name) : ISecretProvider
    {
        public string Name => name;

        public Task<string> ResolveAsync(SecretRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException($"The {name} provider was not expected to run.");
    }
}
