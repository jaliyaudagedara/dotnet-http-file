using System.Globalization;
using System.Text;
using DotNetHttpFile.Execution;
using DotNetHttpFile.Output;
using DotNetHttpFile.Parsing;
using DotNetHttpFile.Resolution;
using DotNetHttpFile.Templates;
using DotNetHttpFile.Tests.Support;
using Spectre.Console;
using Spectre.Console.Testing;

namespace DotNetHttpFile.Tests.Output;

public sealed class ResponseRendererTests
{
    private const string ContentTypeHeader = "Content-Type";
    private const string JsonContentType = "application/json; charset=utf-8";
    private const string TextContentType = "text/plain";
    private const string ToDosUrl = "https://localhost:7128/v1/todos";
    private const string Secret = "s3cr3t-value";
    private const char Escape = (char)0x1B;
    private const char MiddleDot = (char)0xB7;

    [Fact]
    public async Task RenderFull_JsonResponse_PrintsStatusHeadersAndPrettyBody()
    {
        using Harness harness = new(OutputMode.Full);
        HttpExchange exchange = new(
            Request(headers: [new("Accept", "application/json")]),
            Response(JsonContentType, "[1]"u8.ToArray(), elapsedMilliseconds: 141.6));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "GET https://localhost:7128/v1/todos",
                "Accept: application/json",
                "",
                "HTTP/1.1 200 OK   142 ms \u00B7 3 B",
                "Content-Type: application/json; charset=utf-8",
                "",
                "[",
                "  1",
                "]",
            ],
            harness.OutputLines);
        Assert.Empty(harness.ErrorLines);
        Assert.Equal(0, harness.StandardOutput.Length);
    }

    [Fact]
    public async Task RenderFull_AuthorizationBearer_ShowsSchemeAndMask()
    {
        using Harness harness = new(OutputMode.Full);
        HttpExchange exchange = new(
            Request(headers: [new("Authorization", "Bearer eyJhbGciOi.payload.signature")]),
            Response(TextContentType, []));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.Equal("Authorization: Bearer ***", harness.OutputLines[1]);
        Assert.DoesNotContain("eyJhbGciOi", harness.Output.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("authorization", "Basic dXNlcjpwYXNz", "authorization: Basic ***")]
    [InlineData("Proxy-Authorization", "Negotiate   YIIabc==", "Proxy-Authorization: Negotiate ***")]
    [InlineData("Authorization", "AWS4-HMAC-SHA256 Credential=abc, Sig=def", "Authorization: AWS4-HMAC-SHA256 ***")]
    [InlineData("Authorization", "raw-token-without-scheme", "Authorization: ***")]
    [InlineData("Authorization", "Bearer", "Authorization: ***")]
    [InlineData("Authorization", "key=abc def", "Authorization: ***")]
    [InlineData("Cookie", "session=abc123; theme=dark", "Cookie: ***")]
    [InlineData("COOKIE", "session abc123", "COOKIE: ***")]
    public async Task RenderFull_CredentialHeader_HidesTheCredential(string name, string value, string expected)
    {
        using Harness harness = new(OutputMode.Full);
        HttpExchange exchange = new(Request(headers: [new(name, value)]), Response(TextContentType, []));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.Equal(expected, harness.OutputLines[1]);
    }

    [Fact]
    public async Task RenderFull_AuthorizationSchemeIsProviderSecret_IsMasked()
    {
        using Harness harness = new(OutputMode.Full);
        harness.SecretMask.Add(Secret);
        HttpExchange exchange = new(
            Request(headers: [new("Authorization", Secret + " rest")]),
            Response(TextContentType, []));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.Equal("Authorization: *** ***", harness.OutputLines[1]);
    }

    [Fact]
    public async Task RenderFull_ProviderSecretInUrl_IsMasked()
    {
        using Harness harness = new(OutputMode.Full);
        harness.SecretMask.Add(Secret);
        HttpExchange exchange = new(
            Request(displayUrl: $"https://localhost:7128/v1/todos?key={Secret}"),
            Response(TextContentType, []));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.Equal("GET https://localhost:7128/v1/todos?key=***", harness.OutputLines[0]);
    }

    [Fact]
    public async Task RenderFull_SecretInRequestAndResponse_MasksOnlyTheRequest()
    {
        using Harness harness = new(OutputMode.Full);
        harness.SecretMask.Add(Secret);
        HttpExchange exchange = new(
            Request(displayUrl: $"{ToDosUrl}?key={Secret}", headers: [new("X-Api-Key", Secret)]),
            new ReceivedResponse(
                new Version(1, 1),
                401,
                $"Bad key {Secret}",
                [new(ContentTypeHeader, JsonContentType), new("Location", $"{ToDosUrl}?key={Secret}")],
                Encoding.UTF8.GetBytes($$"""{"key":"{{Secret}}"}"""),
                TimeSpan.FromMilliseconds(5)));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                $"GET {ToDosUrl}?key=***",
                "X-Api-Key: ***",
                "",
                $"HTTP/1.1 401 Bad key {Secret}   5 ms {MiddleDot} 22 B",
                $"Content-Type: {JsonContentType}",
                $"Location: {ToDosUrl}?key={Secret}",
                "",
                "{",
                $"  \"key\": \"{Secret}\"",
                "}",
            ],
            harness.OutputLines);
        Assert.Empty(harness.ErrorLines);
    }

    [Fact]
    public async Task RenderFull_BodyWithMarkupCharacters_PrintsLiterally()
    {
        using Harness harness = new(OutputMode.Full);
        HttpExchange exchange = new(
            Request(displayUrl: "https://localhost:7128/v1/todos?filter=[red]a[/]", headers: [new("X-Request", "[b]")]),
            new ReceivedResponse(
                new Version(1, 1),
                200,
                "[u]OK[/]",
                [new(ContentTypeHeader, TextContentType), new("X-Response", "[b]")],
                "[red]x[/] ["u8.ToArray(),
                TimeSpan.FromMilliseconds(5)));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "GET https://localhost:7128/v1/todos?filter=[red]a[/]",
                "X-Request: [b]",
                "",
                "HTTP/1.1 200 [u]OK[/]   5 ms \u00B7 11 B",
                "Content-Type: text/plain",
                "X-Response: [b]",
                "",
                "[red]x[/] [",
            ],
            harness.OutputLines);
    }

    [Fact]
    public async Task RenderFull_BinaryBody_PrintsSummary()
    {
        using Harness harness = new(OutputMode.Full);
        HttpExchange exchange = new(Request(), Response("application/octet-stream", [0xFF, 0xFE, 0x00, 0x41]));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.Equal(
            "(binary body, 4 B, application/octet-stream; use --show body to write it)",
            harness.OutputLines[^1]);
        Assert.Equal(0, harness.StandardOutput.Length);
    }

    [Fact]
    public async Task RenderFull_DecodedGzipBody_PrintsTheFormattedBodyAndItsDecodedSize()
    {
        byte[] body = """{"title": "Write blog post"}"""u8.ToArray();
        using Harness harness = new(OutputMode.Full);
        HttpExchange exchange = new(
            Request(),
            new ReceivedResponse(
                new Version(1, 1),
                200,
                "OK",
                [new(ContentTypeHeader, "application/json"), new("Content-Encoding", "gzip")],
                body,
                TimeSpan.FromMilliseconds(12)));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.Equal(["{", "  \"title\": \"Write blog post\"", "}"], harness.OutputLines[^3..]);
        Assert.Contains($"HTTP/1.1 200 OK   12 ms {MiddleDot} {body.Length} B", harness.OutputLines);
    }

    [Fact]
    public async Task RenderFull_BodyInACodingThatIsNotDecoded_PrintsSummary()
    {
        byte[] body = Compression.Brotli("""{"title": "Write blog post"}"""u8);
        using Harness harness = new(OutputMode.Full);
        HttpExchange exchange = new(
            Request(),
            new ReceivedResponse(
                new Version(1, 1),
                200,
                "OK",
                [new(ContentTypeHeader, "application/json"), new("Content-Encoding", "br")],
                body,
                TimeSpan.FromMilliseconds(12)));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.Equal(
            $"(binary body, {body.Length} B, application/json; use --show body to write it)",
            harness.OutputLines[^1]);
        Assert.Equal("Content-Encoding: br", harness.OutputLines[^3]);
    }

    [Fact]
    public async Task RenderBody_BodyInACodingThatIsNotDecoded_WritesBytesUnchanged()
    {
        byte[] body = [0x1F, 0x8B, 0x08, 0x00, 0xFF];
        using Harness harness = new(OutputMode.Body);
        HttpExchange exchange = new(
            Request(),
            new ReceivedResponse(
                new Version(1, 1),
                200,
                "OK",
                [new("Content-Encoding", "br")],
                body,
                TimeSpan.FromMilliseconds(12)));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.Equal(body, harness.StandardOutput.ToArray());
    }

    [Fact]
    public void RenderRedirect_Message_GoesToStandardErrorAsGivenWithVisibleControlCharacters()
    {
        using Harness harness = new(OutputMode.Full);
        harness.SecretMask.Add("s3cret");

        harness.Renderer.RenderRedirect("Redirected to http://localhost/a?key=s3cret\u001B[2J (302)");

        Assert.Equal(["Redirected to http://localhost/a?key=s3cret\\x1B[2J (302)"], harness.ErrorLines);
        Assert.Empty(harness.OutputLines);
    }

    [Fact]
    public async Task RenderFull_BinaryBodyWithoutContentType_PrintsSummaryWithoutType()
    {
        using Harness harness = new(OutputMode.Full);
        HttpExchange exchange = new(Request(), Response(contentType: null, [0xFF, 0xFE, 0x00, 0x41]));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.Equal("(binary body, 4 B; use --show body to write it)", harness.OutputLines[^1]);
    }

    [Fact]
    public async Task RenderFull_LongLinesOnNarrowConsole_ArriveIntact()
    {
        using Harness harness = new(OutputMode.Full, width: 20);
        string longValue = string.Join(' ', Enumerable.Repeat("word\tword", 12));
        string json = "{\"text\":\"" + longValue.Replace("\t", "\\t", StringComparison.Ordinal) + "\"}";
        HttpExchange exchange = new(
            Request(headers: [new("X-Long", longValue)]),
            Response(JsonContentType, Encoding.UTF8.GetBytes(json)));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "GET https://localhost:7128/v1/todos",
                "X-Long: " + longValue,
                "",
                $"HTTP/1.1 200 OK   12 ms \u00B7 {json.Length} B",
                "Content-Type: application/json; charset=utf-8",
                "",
                "{",
                "  \"text\": \"" + longValue.Replace("\t", "\\t", StringComparison.Ordinal) + "\"",
                "}",
            ],
            harness.OutputLines);
    }

    [Fact]
    public async Task RenderFull_SpectreConsoleOverWriter_KeepsLongLinesWhole()
    {
        using StringWriter writer = new();
        IAnsiConsole spectreConsole = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.Standard,
            Out = new AnsiConsoleOutput(writer),
        });
        using MemoryStream standardOutput = new();
        ResponseRenderer renderer = new(
            new ConsoleStreams(
                standardOutput,
                spectreConsole,
                spectreConsole,
                IsInputRedirected: false,
                IsOutputRedirected: false),
            new SecretMask(),
            OutputMode.Full);
        string longLine = new string('x', 300) + " [red]y[/] " + new string('z', 300);
        HttpExchange exchange = new(
            Request(headers: [new("X-Long", longLine)]),
            Response(TextContentType, Encoding.UTF8.GetBytes(longLine)));

        await renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        string[] lines = writer.ToString().Split(Environment.NewLine);
        Assert.True(spectreConsole.Profile.Width < longLine.Length);
        Assert.Contains("X-Long: " + longLine, lines);
        Assert.Contains(longLine, lines);
    }

    [Fact]
    public async Task RenderFull_TextBodyWithTabsAndLineEndings_KeepsItsLines()
    {
        using Harness harness = new(OutputMode.Full, width: 20);
        HttpExchange exchange = new(
            Request(),
            Response(TextContentType, "a\tb  c\r\n\r\na second line longer than the console\nlast\n"u8.ToArray()));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["a\tb  c", "", "a second line longer than the console", "last"],
            harness.OutputLines[^4..]);
    }

    [Fact]
    public async Task RenderFull_EmptyBody_EndsAfterTheBlankLine()
    {
        using Harness harness = new(OutputMode.Full);
        HttpExchange exchange = new(
            Request(),
            new ReceivedResponse(new Version(1, 1), 204, "No Content", [], ReadOnlyMemory<byte>.Empty, TimeSpan.Zero));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.Equal(
            "GET https://localhost:7128/v1/todos\n\nHTTP/1.1 204 No Content   0 ms \u00B7 0 B\n\n",
            harness.OutputText);
    }

    [Fact]
    public async Task RenderFull_RequestWithBody_DoesNotPrintRequestBody()
    {
        using Harness harness = new(OutputMode.Full);
        ResolvedRequest request = new("POST", new Uri(ToDosUrl), [], ToDosUrl)
        {
            Body = "request-body-text",
        };

        await harness.Renderer.RenderAsync(
            new HttpExchange(request, Response(TextContentType, [])),
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain("request-body-text", harness.Output.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2, 0, "HTTP/2.0 200 OK")]
    [InlineData(3, 0, "HTTP/3.0 200 OK")]
    [InlineData(1, 0, "HTTP/1.0 200 OK")]
    public async Task RenderFull_HttpVersion_PrintsMajorAndMinor(int major, int minor, string expected)
    {
        using Harness harness = new(OutputMode.Full);
        HttpExchange exchange = new(
            Request(),
            new ReceivedResponse(new Version(major, minor), 200, "OK", [], ReadOnlyMemory<byte>.Empty, TimeSpan.Zero));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.StartsWith(expected + "   ", harness.OutputLines[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderFull_EmptyReasonPhrase_LeavesNoGap()
    {
        using Harness harness = new(OutputMode.Full);
        HttpExchange exchange = new(
            Request(),
            new ReceivedResponse(new Version(2, 0), 200, "", [], ReadOnlyMemory<byte>.Empty, TimeSpan.Zero));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.Equal("HTTP/2.0 200   0 ms \u00B7 0 B", harness.OutputLines[2]);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1280, "1.3 KB")]
    [InlineData(1048524, "1023.9 KB")]
    [InlineData(1048525, "1.0 MB")]
    [InlineData(1048575, "1.0 MB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(3774874, "3.6 MB")]
    public async Task RenderFull_BodySize_UsesBinaryUnitsWithOneDecimal(int length, string expected)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        using Harness harness = new(OutputMode.Full);
        HttpExchange exchange = new(Request(), Response("application/octet-stream", new byte[length]));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.EndsWith(" \u00B7 " + expected, harness.OutputLines[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderBody_NonUtf8Bytes_WritesBytesUnchanged()
    {
        byte[] body = [0xFF, 0xFE, 0x00, 0x41];
        using Harness harness = new(OutputMode.Body);

        await harness.Renderer.RenderAsync(
            new HttpExchange(Request(), Response(JsonContentType, body)),
            TestContext.Current.CancellationToken);

        Assert.Equal(body, harness.StandardOutput.ToArray());
    }

    [Fact]
    public async Task RenderBody_StatusLineGoesToError()
    {
        using Harness harness = new(OutputMode.Body);

        await harness.Renderer.RenderAsync(
            new HttpExchange(Request(), Response(JsonContentType, "[1]"u8.ToArray())),
            TestContext.Current.CancellationToken);

        Assert.Equal(["HTTP/1.1 200 OK   12 ms \u00B7 3 B"], harness.ErrorLines);
        Assert.Empty(harness.OutputText);
    }

    [Fact]
    public async Task RenderBody_SecretInBodyAndReasonPhrase_WritesBothAsReceived()
    {
        byte[] body = Encoding.UTF8.GetBytes($"echo {Secret}");
        using Harness harness = new(OutputMode.Body);
        harness.SecretMask.Add(Secret);
        HttpExchange exchange = new(
            Request(),
            new ReceivedResponse(
                new Version(1, 1),
                200,
                Secret,
                [],
                body,
                TimeSpan.FromMilliseconds(12)));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.Equal(body, harness.StandardOutput.ToArray());
        Assert.Equal([$"HTTP/1.1 200 {Secret}   12 ms \u00B7 17 B"], harness.ErrorLines);
    }

    [Fact]
    public async Task RenderStatus_Response_WritesOneLineToOutputAndNothingToError()
    {
        using Harness harness = new(OutputMode.Status);
        harness.SecretMask.Add(Secret);
        HttpExchange exchange = new(
            Request(headers: [new("Accept", "application/json")]),
            Response(TextContentType, Encoding.UTF8.GetBytes($"echo {Secret}")));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.Equal(["HTTP/1.1 200 OK"], harness.OutputLines);
        Assert.Empty(harness.ErrorLines);
        Assert.Equal(0, harness.StandardOutput.Length);
    }

    [Fact]
    public async Task RenderStatus_SecretAndEscapeSequenceInReasonPhrase_PrintsItAsReceivedAsText()
    {
        using Harness harness = new(OutputMode.Status);
        harness.SecretMask.Add(Secret);
        HttpExchange exchange = new(
            Request(),
            new ReceivedResponse(
                new Version(2, 0),
                404,
                $"{Secret}{Escape}[2J",
                [],
                Array.Empty<byte>(),
                TimeSpan.FromMilliseconds(12)));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.Equal([$@"HTTP/2.0 404 {Secret}\x1B[2J"], harness.OutputLines);
    }

    [Fact]
    public async Task RenderHeaders_Response_WritesStatusLineAndHeadersAsReceivedToOutput()
    {
        using Harness harness = new(OutputMode.Headers);
        harness.SecretMask.Add(Secret);
        HttpExchange exchange = new(
            Request(headers: [new("Accept", "application/json")]),
            new ReceivedResponse(
                new Version(1, 1),
                200,
                "OK",
                [
                    new(ContentTypeHeader, JsonContentType),
                    new("X-Echo", $"key={Secret}"),
                    new("X-Title", $"{Escape}]0;owned"),
                ],
                "[1]"u8.ToArray(),
                TimeSpan.FromMilliseconds(12)));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "HTTP/1.1 200 OK",
                "Content-Type: application/json; charset=utf-8",
                $"X-Echo: key={Secret}",
                @"X-Title: \x1B]0;owned",
            ],
            harness.OutputLines);
        Assert.Empty(harness.ErrorLines);
        Assert.Equal(0, harness.StandardOutput.Length);
    }

    [Theory]
    [InlineData(OutputMode.Headers)]
    [InlineData(OutputMode.Status)]
    public void RenderDependency_HeadersOrStatusMode_WritesSummaryToError(object mode)
    {
        using Harness harness = new((OutputMode)mode);
        HttpExchange exchange = new(Request(method: "POST"), Response(JsonContentType, "{}"u8.ToArray()));

        harness.Renderer.RenderDependency(Definition("login"), exchange);

        Assert.Equal(
            ["\u2192 login  POST https://localhost:7128/v1/todos  200 OK  12 ms"],
            harness.ErrorLines);
        Assert.Empty(harness.OutputText);
    }

    [Fact]
    public void RenderDependency_FullMode_WritesSummaryToOutput()
    {
        using Harness harness = new(OutputMode.Full);
        harness.SecretMask.Add(Secret);
        HttpExchange exchange = new(
            Request(method: "POST", displayUrl: $"https://localhost:7128/login?key={Secret}"),
            Response(JsonContentType, "{}"u8.ToArray(), elapsedMilliseconds: 12.4));

        harness.Renderer.RenderDependency(Definition("login"), exchange);

        Assert.Equal(
            ["\u2192 login  POST https://localhost:7128/login?key=***  200 OK  12 ms"],
            harness.OutputLines);
        Assert.Empty(harness.ErrorLines);
    }

    [Fact]
    public void RenderDependency_BodyMode_WritesSummaryToError()
    {
        using Harness harness = new(OutputMode.Body, width: 20);
        HttpExchange exchange = new(Request(method: "POST"), Response(JsonContentType, "{}"u8.ToArray()));

        harness.Renderer.RenderDependency(Definition("[login]"), exchange);

        Assert.Equal(
            ["\u2192 [login]  POST https://localhost:7128/v1/todos  200 OK  12 ms"],
            harness.ErrorLines);
        Assert.Empty(harness.OutputText);
        Assert.Equal(0, harness.StandardOutput.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RenderError_AnyMode_WritesPrefixedMaskedMessageToError(bool bodyMode)
    {
        using Harness harness = new(bodyMode ? OutputMode.Body : OutputMode.Full, width: 20);
        harness.SecretMask.Add(Secret);

        harness.Renderer.RenderError($"Could not send GET https://localhost/[x]?key={Secret}: refused.");

        Assert.Equal(["error: Could not send GET https://localhost/[x]?key=***: refused."], harness.ErrorLines);
        Assert.Empty(harness.OutputText);
    }

    [Fact]
    public void RenderError_MessageWithSeveralLines_KeepsTheLines()
    {
        using Harness harness = new(OutputMode.Full);

        harness.Renderer.RenderError($"first{Environment.NewLine}  second");

        Assert.Equal(["error: first", "  second"], harness.ErrorLines);
    }

    [Fact]
    public void RenderWarning_AnyMode_WritesPrefixedMaskedMessageToError()
    {
        using Harness harness = new(OutputMode.Body);
        harness.SecretMask.Add(Secret);

        harness.Renderer.RenderWarning($"Header '[b]' ignored for {Secret}.");

        Assert.Equal(["warning: Header '[b]' ignored for ***."], harness.ErrorLines);
        Assert.Empty(harness.OutputText);
    }

    [Fact]
    public async Task RenderFull_EscapeSequenceInBodyAndHeaders_PrintsItAsText()
    {
        string clearScreen = $"{Escape}[2J";
        using Harness harness = new(OutputMode.Full);
        HttpExchange exchange = new(
            Request(headers: [new("X-Sent", clearScreen)]),
            new ReceivedResponse(
                new Version(1, 1),
                200,
                $"OK{clearScreen}",
                [new(ContentTypeHeader, TextContentType), new("X-Echo", clearScreen)],
                Encoding.UTF8.GetBytes($"before{clearScreen}after"),
                TimeSpan.FromMilliseconds(5)));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "GET https://localhost:7128/v1/todos",
                @"X-Sent: \x1B[2J",
                "",
                @"HTTP/1.1 200 OK\x1B[2J   5 ms " + MiddleDot + " 15 B",
                "Content-Type: text/plain",
                @"X-Echo: \x1B[2J",
                "",
                @"before\x1B[2Jafter",
            ],
            harness.OutputLines);
        Assert.DoesNotContain(Escape, harness.Output.Output);
    }

    [Fact]
    public async Task RenderFull_LoneCarriageReturnInBodyLine_PrintsItAsText()
    {
        using Harness harness = new(OutputMode.Full);
        byte[] body = [.. "real status"u8, 0x0D, .. "FAKE"u8, 0x0D, 0x0A, .. "next"u8, 0x0D];
        HttpExchange exchange = new(Request(), Response(TextContentType, body));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.Equal([@"real status\x0DFAKE", @"next\x0D"], harness.OutputLines[^2..]);
    }

    [Theory]
    [InlineData(0x00, @"a\x00b")]
    [InlineData(0x07, @"a\x07b")]
    [InlineData(0x08, @"a\x08b")]
    [InlineData(0x1F, @"a\x1Fb")]
    [InlineData(0x7F, @"a\x7Fb")]
    [InlineData(0x80, @"a\x80b")]
    [InlineData(0x85, @"a\x85b")]
    [InlineData(0x9B, @"a\x9Bb")]
    [InlineData(0x9F, @"a\x9Fb")]
    public async Task RenderFull_ControlCharacterInBody_PrintsItAsText(int character, string expected)
    {
        using Harness harness = new(OutputMode.Full);
        HttpExchange exchange = new(
            Request(),
            Response("text/plain; charset=utf-8", Encoding.UTF8.GetBytes($"a{(char)character}b")));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.Equal(expected, harness.OutputLines[^1]);
    }

    [Theory]
    [InlineData(0x20)]
    [InlineData(0x7E)]
    [InlineData(0xA0)]
    [InlineData(0xE9)]
    public async Task RenderFull_PrintableCharacterNextToControlRanges_IsKept(int character)
    {
        using Harness harness = new(OutputMode.Full);
        string text = $"a{(char)character}b";
        HttpExchange exchange = new(
            Request(),
            Response("text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text)));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.Equal(text, harness.OutputLines[^1]);
    }

    [Fact]
    public async Task RenderFull_RequestSecretWithControlCharacter_IsMaskedBeforeItIsEscaped()
    {
        string secret = $"pa{Escape}ss";
        using Harness harness = new(OutputMode.Full);
        harness.SecretMask.Add(secret);
        HttpExchange exchange = new(
            Request(headers: [new("X-Key", $"key={secret};")]),
            Response(TextContentType, []));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.Equal("X-Key: key=***;", harness.OutputLines[1]);
    }

    [Fact]
    public async Task RenderFull_ResponseSecretWithControlCharacter_IsShownWithTheCharacterAsText()
    {
        using Harness harness = new(OutputMode.Full);
        harness.SecretMask.Add($"pa{Escape}ss");
        HttpExchange exchange = new(
            Request(),
            Response(TextContentType, Encoding.UTF8.GetBytes($"key=pa{Escape}ss;")));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.Equal(@"key=pa\x1Bss;", harness.OutputLines[^1]);
    }

    [Fact]
    public async Task RenderBody_EscapeSequenceInBody_WritesBytesUnchanged()
    {
        byte[] body = [0x1B, 0x5B, 0x32, 0x4A, 0x0D, 0x9B];
        using Harness harness = new(OutputMode.Body);
        HttpExchange exchange = new(
            Request(),
            new ReceivedResponse(
                new Version(1, 1),
                200,
                $"OK{Escape}[2J",
                [],
                body,
                TimeSpan.FromMilliseconds(12)));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.Equal(body, harness.StandardOutput.ToArray());
        Assert.Equal([@"HTTP/1.1 200 OK\x1B[2J   12 ms " + MiddleDot + " 6 B"], harness.ErrorLines);
    }

    [Fact]
    public void RenderDependency_EscapeSequenceInReasonPhrase_PrintsItAsText()
    {
        using Harness harness = new(OutputMode.Full);
        HttpExchange exchange = new(
            Request(method: "POST"),
            new ReceivedResponse(
                new Version(1, 1),
                200,
                $"OK{Escape}[2J",
                [],
                ReadOnlyMemory<byte>.Empty,
                TimeSpan.FromMilliseconds(12)));

        harness.Renderer.RenderDependency(Definition("login"), exchange);

        Assert.EndsWith(@"  200 OK\x1B[2J  12 ms", Assert.Single(harness.OutputLines), StringComparison.Ordinal);
    }

    [Fact]
    public void RenderErrorAndWarning_EscapeSequenceInMessage_PrintItAsText()
    {
        using Harness harness = new(OutputMode.Body);

        harness.Renderer.RenderError($"refused{Escape}[2J.");
        harness.Renderer.RenderWarning($"ignored{Escape}[2J.\rnext");

        Assert.Equal([@"error: refused\x1B[2J.", @"warning: ignored\x1B[2J.\x0Dnext"], harness.ErrorLines);
    }

    [Fact]
    public async Task RenderFull_ElapsedAtHalfMillisecond_RoundsAwayFromZero()
    {
        using Harness harness = new(OutputMode.Full);
        HttpExchange exchange = new(Request(), Response(TextContentType, [], elapsedMilliseconds: 142.5));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.StartsWith("HTTP/1.1 200 OK   143 ms ", harness.OutputLines[2], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task RenderFull_BodyIsOnlyALineBreak_EndsAfterTheBlankLine(string body)
    {
        using Harness harness = new(OutputMode.Full);
        HttpExchange exchange = new(Request(), Response(TextContentType, Encoding.UTF8.GetBytes(body)));

        await harness.Renderer.RenderAsync(exchange, TestContext.Current.CancellationToken);

        Assert.EndsWith("\nContent-Type: text/plain\n\n", harness.OutputText, StringComparison.Ordinal);
    }

    private static ResolvedRequest Request(
        string method = "GET",
        string displayUrl = ToDosUrl,
        IReadOnlyList<KeyValuePair<string, string>>? headers = null) =>
        new(method, new Uri(ToDosUrl), headers ?? [], displayUrl);

    private static ReceivedResponse Response(string? contentType, byte[] body, double elapsedMilliseconds = 12) =>
        new(
            new Version(1, 1),
            200,
            "OK",
            contentType is null ? [] : [new(ContentTypeHeader, contentType)],
            body,
            TimeSpan.FromMilliseconds(elapsedMilliseconds));

    private static RequestDefinition Definition(string name) =>
        new("POST", Template.Parse("https://localhost:7128/login"), [], Line: 1) { Name = name };

    private sealed class Harness : IDisposable
    {
        public Harness(OutputMode mode, int width = 80)
        {
            Output.Profile.Width = width;
            Error.Profile.Width = width;
            Renderer = new ResponseRenderer(
                new ConsoleStreams(
                    StandardOutput,
                    Output,
                    Error,
                    IsInputRedirected: false,
                    IsOutputRedirected: mode == OutputMode.Body),
                SecretMask,
                mode);
        }

        public TestConsole Output { get; } = new();

        public TestConsole Error { get; } = new();

        public MemoryStream StandardOutput { get; } = new();

        public SecretMask SecretMask { get; } = new();

        public ResponseRenderer Renderer { get; }

        public string OutputText => Normalize(Output.Output);

        public string[] OutputLines => Lines(Output.Output);

        public string[] ErrorLines => Lines(Error.Output);

        public void Dispose()
        {
            Output.Dispose();
            Error.Dispose();
            StandardOutput.Dispose();
        }

        private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

        // Every rendered line ends with a line break, so the text after the last one is not a line.
        private static string[] Lines(string text)
        {
            string normalized = Normalize(text);
            if (normalized.Length == 0)
            {
                return [];
            }

            Assert.EndsWith("\n", normalized, StringComparison.Ordinal);

            return normalized[..^1].Split('\n');
        }
    }
}
