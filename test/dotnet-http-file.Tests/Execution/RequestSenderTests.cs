using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using DotNetHttpFile.Execution;
using DotNetHttpFile.Resolution;
using DotNetHttpFile.Tests.Support;

namespace DotNetHttpFile.Tests.Execution;

public sealed class RequestSenderTests
{
    private const string ContentTypeHeader = "Content-Type";
    private const string ContentLengthHeader = "Content-Length";
    private const string ContentEncodingHeader = "Content-Encoding";
    private const string HostHeader = "Host";
    private const string CookieHeader = "Cookie";
    private const string AuthorizationHeader = "Authorization";
    private const string ApiKeyHeader = "X-Api-Key";
    private const string PlainHeader = "X-Plain";
    private const string Secret = "s3cret-value";

    private static readonly Uri s_stubUrl = new("http://stub.test/echo");

    [Fact]
    public async Task SendAsync_BodyWithoutContentType_SendsTextPlainUtf8()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using HttpClient httpClient = CreateClient();
        RequestSender sender = CreateSender(httpClient);
        ResolvedRequest request = ResolvedRequestFactory.Create(
            "POST",
            new Uri(server.BaseUri, "/echo?case=2"),
            body: "line1\r\nline2");

        await sender.SendAsync(request, [], cancellationToken: TestContext.Current.CancellationToken);

        RecordedRequest recorded = Assert.Single(server.Requests);
        Assert.Equal("POST /echo?case=2 HTTP/1.1", recorded.RequestLine);
        Assert.Equal(["text/plain; charset=utf-8"], recorded.HeaderValues(ContentTypeHeader));
        Assert.Equal(["12"], recorded.HeaderValues(ContentLengthHeader));
        Assert.Equal("line1\r\nline2"u8.ToArray(), recorded.Body.ToArray());
    }

    [Fact]
    public async Task SendAsync_WrittenContentType_SentExactly()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using HttpClient httpClient = CreateClient();
        RequestSender sender = CreateSender(httpClient);
        ResolvedRequest request = ResolvedRequestFactory.Create(
            "POST",
            new Uri(server.BaseUri, "/echo?case=4"),
            body: "obj={}",
            headers: [(ContentTypeHeader, "text/plain", 6)]);

        await sender.SendAsync(request, [], cancellationToken: TestContext.Current.CancellationToken);

        RecordedRequest recorded = Assert.Single(server.Requests);
        Assert.Equal(["text/plain"], recorded.HeaderValues(ContentTypeHeader));
    }

    [Fact]
    public async Task SendAsync_NonAsciiBody_SendsUtf8Bytes()
    {
        const string Body = "caf\u00E9 \u2192 \u65E5\u672C";
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using HttpClient httpClient = CreateClient();
        RequestSender sender = CreateSender(httpClient);
        ResolvedRequest request = ResolvedRequestFactory.Create("PUT", new Uri(server.BaseUri, "/echo"), body: Body);

        await sender.SendAsync(request, [], cancellationToken: TestContext.Current.CancellationToken);

        RecordedRequest recorded = Assert.Single(server.Requests);
        Assert.Equal(Encoding.UTF8.GetBytes(Body), recorded.Body.ToArray());
    }

    [Fact]
    public async Task SendAsync_NoAddedHeaders_OnlyHostAndWrittenOnes()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using HttpClient httpClient = CreateClient();
        RequestSender sender = CreateSender(httpClient);
        ResolvedRequest request = ResolvedRequestFactory.Create(
            "POST",
            new Uri(server.BaseUri, "/echo?case=4"),
            body: "obj={}",
            headers: [("X-Str", "abc def", 2), (ContentTypeHeader, "text/plain", 3)]);

        // HttpClient adds traceparent on its own whenever an activity is current.
        using Activity activity = new(nameof(SendAsync_NoAddedHeaders_OnlyHostAndWrittenOnes));
        activity.Start();

        await sender.SendAsync(request, [], cancellationToken: TestContext.Current.CancellationToken);

        RecordedRequest recorded = Assert.Single(server.Requests);
        Assert.Equal(
            [ContentLengthHeader, ContentTypeHeader, HostHeader, "X-Str"],
            recorded.Headers.Select(header => header.Key).Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(["abc def"], recorded.HeaderValues("X-Str"));
        Assert.Equal([server.BaseUri.Authority], recorded.HeaderValues(HostHeader));
    }

    [Fact]
    public async Task SendAsync_NoBody_SendsOnlyHost()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using HttpClient httpClient = CreateClient();
        RequestSender sender = CreateSender(httpClient);
        ResolvedRequest request = ResolvedRequestFactory.Create("GET", new Uri(server.BaseUri, "/json"));

        await sender.SendAsync(request, [], cancellationToken: TestContext.Current.CancellationToken);

        RecordedRequest recorded = Assert.Single(server.Requests);
        Assert.Equal([HostHeader], recorded.Headers.Select(header => header.Key));
        Assert.True(recorded.Body.IsEmpty);
    }

    [Fact]
    public async Task SendAsync_WrittenHostHeader_PassedThrough()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using HttpClient httpClient = CreateClient();
        RequestSender sender = CreateSender(httpClient);
        ResolvedRequest request = ResolvedRequestFactory.Create(
            "GET",
            new Uri(server.BaseUri, "/echo"),
            headers: [(HostHeader, "api.example.test", 2)]);

        await sender.SendAsync(request, [], cancellationToken: TestContext.Current.CancellationToken);

        RecordedRequest recorded = Assert.Single(server.Requests);
        Assert.Equal(["api.example.test"], recorded.HeaderValues(HostHeader));
    }

    [Fact]
    public async Task SendAsync_Redirect_FollowsToEcho()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using HttpClient httpClient = CreateClient();
        RequestSender sender = CreateSender(httpClient);
        ResolvedRequest request = ResolvedRequestFactory.Create("GET", new Uri(server.BaseUri, "/redirect?case=8"));

        ReceivedResponse response = await sender.SendAsync(
            request,
            [],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(
            ["GET /redirect?case=8 HTTP/1.1", "GET /echo?redirected=1 HTTP/1.1"],
            server.Requests.Select(recorded => recorded.RequestLine));
    }

    [Fact]
    public async Task SendAsync_ContentHeaderWithoutBody_WarnsAndDrops()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using HttpClient httpClient = CreateClient();
        RequestSender sender = CreateSender(httpClient);
        ResolvedRequest request = ResolvedRequestFactory.Create(
            "GET",
            new Uri(server.BaseUri, "/echo"),
            headers: [("X-Keep", "kept", 6), (ContentLengthHeader, "0", 7)]);
        List<string> warnings = [];

        await sender.SendAsync(request, warnings, cancellationToken: TestContext.Current.CancellationToken);

        string warning = Assert.Single(warnings);
        Assert.Equal(
            "Header 'Content-Length' ignored: a request without a body cannot have content headers (line 7).",
            warning);
        RecordedRequest recorded = Assert.Single(server.Requests);
        Assert.Equal(
            [HostHeader, "X-Keep"],
            recorded.Headers.Select(header => header.Key).Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SendAsync_RedirectChain_PassesTheLogForTheFirstSendOnly()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using HttpClient httpClient = CreateClient();
        RequestSender sender = CreateSender(httpClient);
        ResolvedRequest request = ResolvedRequestFactory.Create(
            "GET",
            server.RedirectUrl(302, "/echo"),
            headers: [(ContentTypeHeader, "application/json", 5)]);
        List<string> log = [];

        await sender.SendAsync(request, [], log.Add, TestContext.Current.CancellationToken);

        Assert.Equal(["Content-Type on line 5 not sent: the request has no body."], log);
    }

    [Fact]
    public async Task SendAsync_ConnectionRefused_ThrowsHttpFileException()
    {
        int port = FindUnusedPort();
        using HttpClient httpClient = CreateClient();
        RequestSender sender = CreateSender(httpClient);
        ResolvedRequest request = new(
            "GET",
            new Uri($"http://127.0.0.1:{port}/echo?key={Secret}"),
            [])
        {
            DisplayUrl = $"http://127.0.0.1:{port}/echo?key=***",
        };

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => sender.SendAsync(request, [], cancellationToken: TestContext.Current.CancellationToken));

        Assert.StartsWith($"Could not send GET http://127.0.0.1:{port}/echo?key=***: ", exception.Message);
        Assert.DoesNotContain(Secret, exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task SendAsync_Response_CapturesStatusHeadersBodyAndElapsed()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using HttpClient httpClient = CreateClient();
        RequestSender sender = CreateSender(httpClient);
        ResolvedRequest request = ResolvedRequestFactory.Create("GET", new Uri(server.BaseUri, "/json"));

        ReceivedResponse response = await sender.SendAsync(
            request,
            [],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpVersion.Version11, response.HttpVersion);
        Assert.Equal(200, response.StatusCode);
        Assert.Equal("OK", response.ReasonPhrase);
        Assert.Equal(EchoServer.JsonContentType, response.ContentType);
        Assert.Contains(KeyValuePair.Create(ContentLengthHeader, "148"), response.Headers);
        Assert.Contains(response.Headers, header => header.Key == "Date");
        Assert.Equal(EchoServer.JsonBody, Encoding.UTF8.GetString(response.Body.Span));
        Assert.True(response.Elapsed > TimeSpan.Zero);
    }

    [Fact]
    public async Task SendAsync_RepeatedResponseHeader_KeepsOneEntryPerValue()
    {
        Task serve = ServeOnceAsync(
            out Uri url,
            "HTTP/1.1 200 OK",
            "Set-Cookie: a=1; Path=/",
            "Content-Type: application/json",
            "Set-Cookie: b=2; Expires=Sat, 14 Nov 2026 00:00:00 GMT",
            "Content-Length: 2",
            "",
            "{}");
        using HttpClient httpClient = CreateClient();
        RequestSender sender = CreateSender(httpClient);

        Task<ReceivedResponse> send = sender.SendAsync(
            ResolvedRequestFactory.Create("GET", url),
            [],
            cancellationToken: TestContext.Current.CancellationToken);
        await Task.WhenAll(send, serve);
        ReceivedResponse response = await send;

        Assert.Equal(
            [
                KeyValuePair.Create("Set-Cookie", "a=1; Path=/"),
                KeyValuePair.Create("Set-Cookie", "b=2; Expires=Sat, 14 Nov 2026 00:00:00 GMT"),
                KeyValuePair.Create(ContentTypeHeader, "application/json"),
                KeyValuePair.Create(ContentLengthHeader, "2"),
            ],
            response.Headers);
    }

    [Fact]
    public async Task SendAsync_ChunkedResponse_ReadsBodyAndAddsNoContentLength()
    {
        Task serve = ServeOnceAsync(
            out Uri url,
            "HTTP/1.1 200 OK",
            "Transfer-Encoding: chunked",
            "",
            "5",
            "hello",
            "0",
            "",
            "");
        using HttpClient httpClient = CreateClient();
        RequestSender sender = CreateSender(httpClient);

        Task<ReceivedResponse> send = sender.SendAsync(
            ResolvedRequestFactory.Create("GET", url),
            [],
            cancellationToken: TestContext.Current.CancellationToken);
        await Task.WhenAll(send, serve);
        ReceivedResponse response = await send;

        Assert.Equal("hello"u8.ToArray(), response.Body.ToArray());
        Assert.Equal([KeyValuePair.Create("Transfer-Encoding", "chunked")], response.Headers);
    }

    [Fact]
    public async Task SendAsync_NullReasonPhrase_BecomesEmpty()
    {
        using HttpClient httpClient = CreateStubClient((_, _) =>
            Task.FromResult(new HttpResponseMessage((HttpStatusCode)299) { ReasonPhrase = null }));
        RequestSender sender = CreateSender(httpClient);

        ReceivedResponse response = await sender.SendAsync(
            ResolvedRequestFactory.Create("GET", s_stubUrl),
            [],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, response.ReasonPhrase);
        Assert.Equal(299, response.StatusCode);
        Assert.True(response.Body.IsEmpty);
    }

    [Fact]
    public async Task SendAsync_CertificateNotTrusted_SuggestsInsecure()
    {
        using HttpClient httpClient = CreateStubClient((_, _) =>
            throw new HttpRequestException(
                "The SSL connection could not be established, see inner exception.",
                new AuthenticationException("The remote certificate is invalid.")));
        RequestSender sender = CreateSender(httpClient);
        ResolvedRequest request = ResolvedRequestFactory.Create("GET", new Uri("https://stub.test/echo"));

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => sender.SendAsync(request, [], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            "Could not send GET https://stub.test/echo: The remote certificate is invalid. "
            + "The server certificate was not trusted. Pass --insecure to skip certificate validation.",
            exception.Message);
    }

    [Fact]
    public async Task SendAsync_SetCookieOnRedirect_SendsNoCookieNobodyWrote()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using HttpClient httpClient = CreateClient();
        RequestSender sender = CreateSender(httpClient);

        await sender.SendAsync(
            ResolvedRequestFactory.Create("GET", new Uri(server.BaseUri, EchoServer.RedirectWithCookiePath)),
            [],
            cancellationToken: TestContext.Current.CancellationToken);
        await sender.SendAsync(
            ResolvedRequestFactory.Create("GET", new Uri(server.BaseUri, "/echo?later=1")),
            [],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            [EchoServer.RedirectWithCookiePath, EchoServer.RedirectLocation, "/echo?later=1"],
            server.Requests.Select(recorded => recorded.Target));
        Assert.All(server.Requests, recorded => Assert.Empty(recorded.HeaderValues(CookieHeader)));
    }

    [Fact]
    public async Task SendAsync_WrittenCookieHeader_IsSent()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using HttpClient httpClient = CreateClient();
        RequestSender sender = CreateSender(httpClient);
        ResolvedRequest request = ResolvedRequestFactory.Create(
            "GET",
            new Uri(server.BaseUri, "/echo"),
            headers: [(CookieHeader, "session=written", 2)]);

        await sender.SendAsync(request, [], cancellationToken: TestContext.Current.CancellationToken);

        RecordedRequest recorded = Assert.Single(server.Requests);
        Assert.Equal(["session=written"], recorded.HeaderValues(CookieHeader));
    }

    [Fact]
    public async Task SendAsync_WrittenContentLengthDisagreesWithBody_ThrowsWithoutHeaderValues()
    {
        const string CustomValue = "custom-header-value";
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using HttpClient httpClient = CreateClient();
        RequestSender sender = CreateSender(httpClient);
        ResolvedRequest request = ResolvedRequestFactory.Create(
            "POST",
            new Uri(server.BaseUri, "/echo"),
            body: "a body far longer than the length that was written",
            headers:
            [
                ("Authorization", $"Bearer {Secret}", 2),
                ("X-Custom", CustomValue, 3),
                (ContentLengthHeader, "7", 4),
            ]);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => sender.SendAsync(request, [], cancellationToken: TestContext.Current.CancellationToken));

        Assert.StartsWith($"Could not send POST {request.DisplayUrl}: ", exception.Message);
        Assert.DoesNotContain(Secret, exception.Message);
        Assert.DoesNotContain(CustomValue, exception.Message);
        Assert.DoesNotContain("a body far longer", exception.Message);
    }

    [Fact]
    public async Task SendAsync_CancelledWithoutTimeoutInnerWhileCallerTokenIsLive_ReportsTimeout()
    {
        using HttpClient httpClient = CreateStubClient((_, _) => throw new OperationCanceledException());
        RequestSender sender = CreateSender(httpClient);
        ResolvedRequest request = ResolvedRequestFactory.Create("GET", s_stubUrl);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => sender.SendAsync(request, [], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("GET http://stub.test/echo timed out after 100 s.", exception.Message);
    }

    [Fact]
    public async Task SendAsync_TimeoutReportedAfterCallerCancelled_CallerCancellationWins()
    {
        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using HttpClient httpClient = CreateStubClient((_, _) =>
        {
            cancellation.Cancel();

            throw new TaskCanceledException("The request timed out.", new TimeoutException());
        });
        RequestSender sender = CreateSender(httpClient);
        ResolvedRequest request = ResolvedRequestFactory.Create("GET", s_stubUrl);

        OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => sender.SendAsync(request, [], cancellationToken: cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task SendAsync_OtherHttpRequestException_HasNoInsecureHint()
    {
        using HttpClient httpClient = CreateStubClient((_, _) =>
            throw new HttpRequestException("No such host is known.", new SocketException()));
        RequestSender sender = CreateSender(httpClient);
        ResolvedRequest request = ResolvedRequestFactory.Create("POST", s_stubUrl, body: "secret body");

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => sender.SendAsync(request, [], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("Could not send POST http://stub.test/echo: No such host is known.", exception.Message);
    }

    [Fact]
    public async Task SendAsync_Timeout_ThrowsHttpFileException()
    {
        TimeSpan timeout = TimeSpan.FromMilliseconds(50);
        using HttpClient httpClient = CreateStubClient(WaitForCancellationAsync);
        httpClient.Timeout = timeout;
        RequestSender sender = CreateSender(httpClient);
        ResolvedRequest request = ResolvedRequestFactory.Create("GET", s_stubUrl);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => sender.SendAsync(request, [], cancellationToken: TestContext.Current.CancellationToken));

        string seconds = timeout.TotalSeconds.ToString(CultureInfo.CurrentCulture);
        Assert.Equal($"GET http://stub.test/echo timed out after {seconds} s.", exception.Message);
    }

    [Fact]
    public async Task SendAsync_CallerCancels_ThrowsOperationCanceledWithCallerToken()
    {
        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using HttpClient httpClient = CreateStubClient((message, cancellationToken) =>
        {
            started.SetResult();

            return WaitForCancellationAsync(message, cancellationToken);
        });
        RequestSender sender = CreateSender(httpClient);

        Task<ReceivedResponse> send = sender.SendAsync(
            ResolvedRequestFactory.Create("GET", s_stubUrl),
            [],
            cancellationToken: cancellation.Token);
        await started.Task;
        await cancellation.CancelAsync();

        OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(() => send);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task SendAsync_SameOriginRedirect_KeepsEveryHeaderAndReportsTheRedirect()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using HttpClient httpClient = CreateClient();
        SecretMask secretMask = new();
        secretMask.Add(Secret);
        List<string> redirects = [];
        RequestSender sender = new(httpClient, secretMask, redirects.Add);
        ResolvedRequest request = ResolvedRequestFactory.Create(
            "GET",
            server.RedirectUrl(302, "/echo?step=2"),
            headers: CredentialHeaders());

        ReceivedResponse response = await sender.SendAsync(
            request,
            [],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(2, server.Requests.Count);
        RecordedRequest followed = server.Requests[1];
        Assert.Equal("GET /echo?step=2 HTTP/1.1", followed.RequestLine);
        Assert.Equal([$"Bearer {Secret}"], followed.HeaderValues(AuthorizationHeader));
        Assert.Equal(["session=written"], followed.HeaderValues(CookieHeader));
        Assert.Equal([Secret], followed.HeaderValues(ApiKeyHeader));
        Assert.Equal(["plain"], followed.HeaderValues(PlainHeader));
        Assert.Equal([$"Redirected to {server.BaseUri}echo?step=2 (302)"], redirects);
    }

    [Fact]
    public async Task SendAsync_CrossOriginRedirect_DropsCredentialHeadersAndKeepsPlainOnes()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        await using EchoServer otherOrigin = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using HttpClient httpClient = CreateClient();
        SecretMask secretMask = new();
        secretMask.Add(Secret);
        List<string> redirects = [];
        RequestSender sender = new(httpClient, secretMask, redirects.Add);
        ResolvedRequest request = ResolvedRequestFactory.Create(
            "GET",
            server.RedirectUrl(302, $"{otherOrigin.BaseUri}echo?key={Secret}"),
            headers: CredentialHeaders());

        ReceivedResponse response = await sender.SendAsync(
            request,
            [],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal([$"Bearer {Secret}"], Assert.Single(server.Requests).HeaderValues(AuthorizationHeader));
        RecordedRequest followed = Assert.Single(otherOrigin.Requests);
        Assert.Empty(followed.HeaderValues(AuthorizationHeader));
        Assert.Empty(followed.HeaderValues(CookieHeader));
        Assert.Empty(followed.HeaderValues(ApiKeyHeader));
        Assert.Equal(["plain"], followed.HeaderValues(PlainHeader));
        Assert.Equal([$"Redirected to {otherOrigin.BaseUri}echo?key={Secret} (302)"], redirects);
    }

    [Theory]
    [InlineData($"http://stub.test/{Secret}/a", "b", "http://stub.test/***/b")]
    [InlineData($"http://stub.test/a#{Secret}", "http://other.test/b", "http://other.test/b#***")]
    public async Task SendAsync_RedirectLocationTakesPartsOfTheRequestUrl_ReportsThemMasked(
        string url,
        string location,
        string expected)
    {
        int sendCount = 0;
        using HttpClient httpClient = CreateStubClient((_, _) =>
            Task.FromResult(++sendCount == 1
                ? Redirect(HttpStatusCode.Found, location)
                : new HttpResponseMessage(HttpStatusCode.OK)));
        SecretMask secretMask = new();
        secretMask.Add(Secret);
        List<string> redirects = [];
        RequestSender sender = new(httpClient, secretMask, redirects.Add);

        await sender.SendAsync(
            ResolvedRequestFactory.Create("GET", new Uri(url)),
            [],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([$"Redirected to {expected} (302)"], redirects);
    }

    [Fact]
    public async Task SendAsync_RedirectBackToTheFirstOrigin_DoesNotSendTheDroppedHeadersAgain()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        await using EchoServer otherOrigin = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using HttpClient httpClient = CreateClient();
        RequestSender sender = CreateSender(httpClient);
        Uri back = otherOrigin.RedirectUrl(302, $"{server.BaseUri}echo?back=1");
        ResolvedRequest request = ResolvedRequestFactory.Create(
            "GET",
            server.RedirectUrl(302, back.AbsoluteUri),
            headers: CredentialHeaders());

        await sender.SendAsync(request, [], cancellationToken: TestContext.Current.CancellationToken);

        RecordedRequest last = server.Requests[^1];
        Assert.Equal("GET /echo?back=1 HTTP/1.1", last.RequestLine);
        Assert.Empty(last.HeaderValues(AuthorizationHeader));
        Assert.Empty(last.HeaderValues(CookieHeader));
        Assert.Equal(["plain"], last.HeaderValues(PlainHeader));
    }

    [Fact]
    public async Task SendAsync_RedirectLoop_StopsAfterFiftyRedirectsAndReturnsTheRedirect()
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using HttpClient httpClient = CreateClient();
        List<string> redirects = [];
        RequestSender sender = new(httpClient, new SecretMask(), redirects.Add);
        ResolvedRequest request = ResolvedRequestFactory.Create(
            "GET",
            new Uri(server.BaseUri, EchoServer.RedirectLoopPath));

        ReceivedResponse response = await sender.SendAsync(
            request,
            [],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(302, response.StatusCode);
        Assert.Equal(RedirectPolicy.MaxRedirects + 1, server.Requests.Count);
        Assert.Equal(50, redirects.Count);
    }

    [Theory]
    [InlineData(300, "POST", "GET")]
    [InlineData(301, "POST", "GET")]
    [InlineData(302, "POST", "GET")]
    [InlineData(303, "POST", "GET")]
    [InlineData(303, "PUT", "GET")]
    [InlineData(303, "DELETE", "GET")]
    public async Task SendAsync_RedirectThatChangesTheMethod_SendsGetWithoutTheBody(
        int statusCode,
        string method,
        string expectedMethod)
    {
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using HttpClient httpClient = CreateClient();
        RequestSender sender = CreateSender(httpClient);
        List<string> warnings = [];
        ResolvedRequest request = ResolvedRequestFactory.Create(
            method,
            server.RedirectUrl(statusCode, "/echo"),
            body: """{"title": "Write blog post"}""",
            headers: [(ContentTypeHeader, "application/json", 2), (PlainHeader, "plain", 3)]);

        await sender.SendAsync(request, warnings, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(method, server.Requests[0].Method);
        RecordedRequest followed = server.Requests[1];
        Assert.Equal($"{expectedMethod} /echo HTTP/1.1", followed.RequestLine);
        Assert.Equal(0, followed.Body.Length);
        Assert.Empty(followed.HeaderValues(ContentTypeHeader));
        Assert.Empty(followed.HeaderValues(ContentLengthHeader));
        Assert.Equal(["plain"], followed.HeaderValues(PlainHeader));
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData(307, "POST")]
    [InlineData(308, "POST")]
    [InlineData(301, "PUT")]
    [InlineData(302, "DELETE")]
    [InlineData(303, "HEAD")]
    public async Task SendAsync_RedirectThatKeepsTheMethod_SendsTheSameMethodAndBody(int statusCode, string method)
    {
        const string Body = """{"title": "Write blog post"}""";
        await using EchoServer server = await EchoServer.StartAsync(TestContext.Current.CancellationToken);
        using HttpClient httpClient = CreateClient();
        RequestSender sender = CreateSender(httpClient);
        ResolvedRequest request = ResolvedRequestFactory.Create(
            method,
            server.RedirectUrl(statusCode, "/echo"),
            body: Body,
            headers: [(ContentTypeHeader, "application/json", 2)]);

        await sender.SendAsync(request, [], cancellationToken: TestContext.Current.CancellationToken);

        RecordedRequest followed = server.Requests[1];
        Assert.Equal($"{method} /echo HTTP/1.1", followed.RequestLine);
        Assert.Equal(Body, Encoding.UTF8.GetString(followed.Body.Span));
        Assert.Equal(["application/json"], followed.HeaderValues(ContentTypeHeader));
    }

    [Fact]
    public async Task SendAsync_RedirectFromHttpsToHttp_IsNotFollowed()
    {
        int sendCount = 0;
        using HttpClient httpClient = CreateStubClient((_, _) =>
        {
            sendCount++;

            return Task.FromResult(Redirect(HttpStatusCode.Found, "http://stub.test/plain"));
        });
        List<string> redirects = [];
        RequestSender sender = new(httpClient, new SecretMask(), redirects.Add);
        ResolvedRequest request = ResolvedRequestFactory.Create("GET", new Uri("https://stub.test/echo"));

        ReceivedResponse response = await sender.SendAsync(
            request,
            [],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal((302, 1), (response.StatusCode, sendCount));
        Assert.Empty(redirects);
    }

    [Theory]
    [InlineData("http://stub.test/echo", "https://stub.test/echo")]
    [InlineData("http://stub.test/echo", "http://stub.test:8080/echo")]
    [InlineData("http://stub.test/echo", "http://other.test/echo")]
    public async Task SendAsync_RedirectToAnotherSchemeHostOrPort_DropsAuthorization(string url, string location)
    {
        List<HttpRequestMessage> sent = [];
        using HttpClient httpClient = CreateStubClient((message, _) =>
        {
            sent.Add(message);

            return Task.FromResult(sent.Count == 1
                ? Redirect(HttpStatusCode.Found, location)
                : new HttpResponseMessage(HttpStatusCode.OK));
        });
        RequestSender sender = CreateSender(httpClient);
        ResolvedRequest request = ResolvedRequestFactory.Create(
            "GET",
            new Uri(url),
            headers: [(AuthorizationHeader, "Bearer written", 2)]);

        await sender.SendAsync(request, [], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            (new Uri(location), false),
            (sent[1].RequestUri, sent[1].Headers.NonValidated.Contains(AuthorizationHeader)));
    }

    [Fact]
    public async Task SendAsync_RedirectWithoutLocation_IsTheResponse()
    {
        using HttpClient httpClient = CreateStubClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found)));
        RequestSender sender = CreateSender(httpClient);

        ReceivedResponse response = await sender.SendAsync(
            ResolvedRequestFactory.Create("GET", s_stubUrl),
            [],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(302, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_FollowedRequestFails_NamesTheRedirectUrl()
    {
        int sendCount = 0;
        using HttpClient httpClient = CreateStubClient((_, _) =>
            ++sendCount == 1
                ? Task.FromResult(Redirect(HttpStatusCode.Found, "http://other.test/next"))
                : throw new HttpRequestException("No such host is known."));
        RequestSender sender = CreateSender(httpClient);
        ResolvedRequest request = ResolvedRequestFactory.Create("POST", s_stubUrl, body: "body");

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => sender.SendAsync(request, [], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("Could not send GET http://other.test/next: No such host is known.", exception.Message);
    }

    [Fact]
    public async Task SendAsync_CallerCancelsWhileFollowingARedirect_ThrowsOperationCanceledWithCallerToken()
    {
        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        TaskCompletionSource followed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int sendCount = 0;
        using HttpClient httpClient = CreateStubClient((message, cancellationToken) =>
        {
            if (++sendCount == 1)
            {
                return Task.FromResult(Redirect(HttpStatusCode.Found, "/next"));
            }

            followed.SetResult();

            return WaitForCancellationAsync(message, cancellationToken);
        });
        RequestSender sender = CreateSender(httpClient);

        Task<ReceivedResponse> send = sender.SendAsync(
            ResolvedRequestFactory.Create("GET", s_stubUrl),
            [],
            cancellationToken: cancellation.Token);
        await followed.Task;
        await cancellation.CancelAsync();

        OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(() => send);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task SendAsync_RedirectsOutlastTheTimeout_ReportsTimeout()
    {
        TimeSpan timeout = TimeSpan.FromMilliseconds(200);
        using HttpClient httpClient = CreateStubClient(async (_, cancellationToken) =>
        {
            // Each send is well inside the timeout; only the chain of them is not.
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);

            return Redirect(HttpStatusCode.TemporaryRedirect, "/again");
        });
        httpClient.Timeout = timeout;
        RequestSender sender = CreateSender(httpClient);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => sender.SendAsync(
                ResolvedRequestFactory.Create("GET", s_stubUrl),
                [],
                cancellationToken: TestContext.Current.CancellationToken));

        string seconds = timeout.TotalSeconds.ToString(CultureInfo.CurrentCulture);
        Assert.Equal($"GET http://stub.test/again timed out after {seconds} s.", exception.Message);
    }

    [Fact]
    public async Task SendAsync_GzipResponse_DecodesTheBodyAndKeepsTheHeadersAsReceived()
    {
        byte[] encoded = Compression.Gzip(Encoding.UTF8.GetBytes(EchoServer.JsonBody));
        using HttpClient httpClient = CreateStubClient((_, _) =>
            Task.FromResult(EncodedResponse(HttpStatusCode.OK, encoded, "gzip")));
        RequestSender sender = CreateSender(httpClient);
        List<string> warnings = [];

        ReceivedResponse response = await sender.SendAsync(
            ResolvedRequestFactory.Create("GET", s_stubUrl),
            warnings,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(EchoServer.JsonBody, Encoding.UTF8.GetString(response.Body.Span));
        Assert.Contains(KeyValuePair.Create(ContentEncodingHeader, "gzip"), response.Headers);
        Assert.Contains(
            KeyValuePair.Create(ContentLengthHeader, encoded.Length.ToString(CultureInfo.InvariantCulture)),
            response.Headers);
        Assert.Empty(warnings);
    }

    [Fact]
    public async Task SendAsync_ZlibDeflateResponse_DecodesTheBody()
    {
        using HttpClient httpClient = CreateStubClient((_, _) =>
            Task.FromResult(EncodedResponse(HttpStatusCode.OK, Compression.Zlib("hello"u8), "deflate")));
        RequestSender sender = CreateSender(httpClient);

        ReceivedResponse response = await sender.SendAsync(
            ResolvedRequestFactory.Create("GET", s_stubUrl),
            [],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("hello"u8.ToArray(), response.Body.ToArray());
    }

    [Fact]
    public async Task SendAsync_RawDeflateResponse_DecodesTheBody()
    {
        using HttpClient httpClient = CreateStubClient((_, _) =>
            Task.FromResult(EncodedResponse(HttpStatusCode.OK, Compression.RawDeflate("hello"u8), "deflate")));
        RequestSender sender = CreateSender(httpClient);

        ReceivedResponse response = await sender.SendAsync(
            ResolvedRequestFactory.Create("GET", s_stubUrl),
            [],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("hello"u8.ToArray(), response.Body.ToArray());
    }

    [Fact]
    public async Task SendAsync_SeveralCodings_DecodesTheLastAppliedFirst()
    {
        byte[] encoded = Compression.Gzip(Compression.Zlib("hello"u8));
        using HttpClient httpClient = CreateStubClient((_, _) =>
            Task.FromResult(EncodedResponse(HttpStatusCode.OK, encoded, "deflate, identity, gzip")));
        RequestSender sender = CreateSender(httpClient);

        ReceivedResponse response = await sender.SendAsync(
            ResolvedRequestFactory.Create("GET", s_stubUrl),
            [],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("hello"u8.ToArray(), response.Body.ToArray());
    }

    [Fact]
    public async Task SendAsync_IdentityCoding_KeepsTheBody()
    {
        using HttpClient httpClient = CreateStubClient((_, _) =>
            Task.FromResult(EncodedResponse(HttpStatusCode.OK, "hello"u8.ToArray(), "identity")));
        RequestSender sender = CreateSender(httpClient);

        ReceivedResponse response = await sender.SendAsync(
            ResolvedRequestFactory.Create("GET", s_stubUrl),
            [],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("hello"u8.ToArray(), response.Body.ToArray());
    }

    [Fact]
    public async Task SendAsync_GzipHeaderWithEmptyBody_ReturnsEmptyBody()
    {
        using HttpClient httpClient = CreateStubClient((_, _) =>
            Task.FromResult(EncodedResponse(HttpStatusCode.NotModified, [], "gzip")));
        RequestSender sender = CreateSender(httpClient);

        ReceivedResponse response = await sender.SendAsync(
            ResolvedRequestFactory.Create("GET", s_stubUrl),
            [],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(response.Body.IsEmpty);
    }

    [Fact]
    public async Task SendAsync_UnsupportedCoding_KeepsTheBytesAndWarnsNamingTheCoding()
    {
        byte[] encoded = Compression.Gzip(Compression.Brotli("hello"u8));
        using HttpClient httpClient = CreateStubClient((_, _) =>
            Task.FromResult(EncodedResponse(HttpStatusCode.OK, encoded, "br, gzip")));
        RequestSender sender = CreateSender(httpClient);
        List<string> warnings = [];

        ReceivedResponse response = await sender.SendAsync(
            ResolvedRequestFactory.Create("GET", s_stubUrl),
            warnings,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(encoded, response.Body.ToArray());
        Assert.Equal(
            [
                "The response to GET http://stub.test/echo is left encoded: Content-Encoding br is not decoded, "
                + "only gzip and deflate are.",
            ],
            warnings);
    }

    [Fact]
    public async Task SendAsync_BodyThatIsNotValidGzip_ThrowsNamingMethodUrlStatusAndCoding()
    {
        using HttpClient httpClient = CreateStubClient((_, _) =>
            Task.FromResult(EncodedResponse(HttpStatusCode.Created, "not gzip at all"u8.ToArray(), "gzip")));
        RequestSender sender = CreateSender(httpClient);
        ResolvedRequest request = ResolvedRequestFactory.Create("POST", s_stubUrl, body: "body");

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => sender.SendAsync(request, [], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            "Could not decode the response to POST http://stub.test/echo (201): the body is not valid gzip data.",
            exception.Message);
        Assert.Equal(ExitCodes.ErrorStatus, exception.ExitCode);
    }

    [Fact]
    public async Task SendAsync_BodyLargerThanTheLimitWhenDecoded_ThrowsNamingMethodUrlStatusAndCoding()
    {
        byte[] encoded = Compression.GzipZeros(ContentDecoder.MaxDecodedLength + 1L);
        using HttpClient httpClient = CreateStubClient((_, _) =>
            Task.FromResult(EncodedResponse(HttpStatusCode.OK, encoded, "gzip")));
        RequestSender sender = CreateSender(httpClient);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => sender.SendAsync(
                ResolvedRequestFactory.Create("GET", s_stubUrl),
                [],
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            "Could not decode the response to GET http://stub.test/echo (200): "
            + "the gzip body is larger than 100 MiB when decoded.",
            exception.Message);
        Assert.Equal(ExitCodes.ErrorStatus, exception.ExitCode);
    }

    private static HttpClient CreateClient() => ToolHttpClient.Create(ToolHttpClient.CreateHandler(insecure: false));

    private static HttpResponseMessage EncodedResponse(HttpStatusCode statusCode, byte[] body, string contentEncoding)
    {
        ByteArrayContent content = new(body);
        content.Headers.TryAddWithoutValidation(ContentEncodingHeader, contentEncoding);
        content.Headers.ContentLength = body.Length;

        return new HttpResponseMessage(statusCode) { Content = content };
    }

    private static RequestSender CreateSender(HttpClient httpClient) =>
        new(httpClient, new SecretMask(), static _ => { });

    private static (string Name, string Value, int Line)[] CredentialHeaders() =>
    [
        (AuthorizationHeader, $"Bearer {Secret}", 2),
        (CookieHeader, "session=written", 3),
        (ApiKeyHeader, Secret, 4),
        (PlainHeader, "plain", 5),
    ];

    private static HttpResponseMessage Redirect(HttpStatusCode statusCode, string location)
    {
        HttpResponseMessage response = new(statusCode);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);

        return response;
    }

    private static HttpClient CreateStubClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        ToolHttpClient.Create(new StubHandler(send));

    private static async Task<HttpResponseMessage> WaitForCancellationAsync(
        HttpRequestMessage message,
        CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        throw new UnreachableException();
    }

    // Answers one connection with the given lines, so a test controls exactly what is on the wire.
    private static Task ServeOnceAsync(out Uri url, params string[] responseLines)
    {
        byte[] response = Encoding.ASCII.GetBytes(string.Join("\r\n", responseLines));
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        url = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");

        return ServeAsync(listener, response, TestContext.Current.CancellationToken);

        static async Task ServeAsync(TcpListener listener, byte[] response, CancellationToken cancellationToken)
        {
            using (listener)
            {
                using TcpClient connection = await listener.AcceptTcpClientAsync(cancellationToken);
                NetworkStream stream = connection.GetStream();
                using StreamReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken)))
                {
                }

                await stream.WriteAsync(response, cancellationToken);
            }
        }
    }

    private static int FindUnusedPort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
