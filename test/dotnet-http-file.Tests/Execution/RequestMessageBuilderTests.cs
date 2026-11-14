using System.Net;
using DotNetHttpFile.Execution;

namespace DotNetHttpFile.Tests.Execution;

public sealed class RequestMessageBuilderTests
{
    private const string Secret = "s3cret-value";

    private static readonly Uri s_url = new("https://localhost:7128/echo");

    [Theory]
    [InlineData(null)]
    [InlineData("HTTP/1.1")]
    public void Build_NoVersionOrHttp11_UsesVersion11(string? httpVersion)
    {
        ResolvedRequest request = ResolvedRequestFactory.Create("GET", s_url, httpVersion: httpVersion);

        using HttpRequestMessage message = RequestMessageBuilder.Build(request, []);

        Assert.Equal(HttpVersion.Version11, message.Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionOrLower, message.VersionPolicy);
    }

    [Theory]
    [InlineData("HTTP/2", 2)]
    [InlineData("HTTP/2.0", 2)]
    [InlineData("HTTP/3", 3)]
    [InlineData("HTTP/3.0", 3)]
    public void Build_Http2OrHttp3_RequestsVersionOrLower(string httpVersion, int expectedMajor)
    {
        ResolvedRequest request = ResolvedRequestFactory.Create("GET", s_url, httpVersion: httpVersion);

        using HttpRequestMessage message = RequestMessageBuilder.Build(request, []);

        Assert.Equal(new Version(expectedMajor, 0), message.Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionOrLower, message.VersionPolicy);
    }

    [Theory]
    [InlineData("HTTP/9")]
    [InlineData("HTTP/1.1x")]
    [InlineData("HTTP/")]
    public void Build_UnsupportedVersion_Throws(string httpVersion)
    {
        ResolvedRequest request = ResolvedRequestFactory.Create("GET", s_url, httpVersion: httpVersion);

        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => RequestMessageBuilder.Build(request, []));

        Assert.Equal(
            $"'{httpVersion}' is not a supported HTTP version. Use HTTP/1.0, HTTP/1.1, HTTP/2 or HTTP/3.",
            exception.Message);
    }

    [Fact]
    public void Build_MethodAndUrl_AreTakenFromRequest()
    {
        ResolvedRequest request = ResolvedRequestFactory.Create("PATCH", s_url);

        using HttpRequestMessage message = RequestMessageBuilder.Build(request, []);

        Assert.Equal("PATCH", message.Method.Method);
        Assert.Same(s_url, message.RequestUri);
        Assert.Null(message.Content);
    }

    [Fact]
    public void Build_Body_LeavesExpectContinueUnset()
    {
        ResolvedRequest request = ResolvedRequestFactory.Create("POST", s_url, body: "{}");

        using HttpRequestMessage message = RequestMessageBuilder.Build(request, []);

        Assert.Null(message.Headers.ExpectContinue);
    }

    [Fact]
    public void Build_ContentHeadersWithBody_GoOnContent()
    {
        ResolvedRequest request = ResolvedRequestFactory.Create(
            "POST",
            s_url,
            body: "{}",
            headers:
            [
                ("content-type", "application/json", 2),
                ("Content-Encoding", "identity", 3),
                ("Content-Language", "en", 4),
                ("Authorization", "Bearer abc", 5),
            ]);
        List<string> warnings = [];

        using HttpRequestMessage message = RequestMessageBuilder.Build(request, warnings);

        Assert.Empty(warnings);
        Assert.NotNull(message.Content);
        Assert.Equal(
            ["Content-Type", "Content-Encoding", "Content-Language"],
            message.Content.Headers.NonValidated.Select(header => header.Key));
        Assert.Equal("application/json", message.Content.Headers.NonValidated["Content-Type"].ToString());
        Assert.Equal(["Authorization"], message.Headers.NonValidated.Select(header => header.Key));
    }

    [Fact]
    public void Build_SeveralContentHeadersWithoutBody_WarnsForEachInFileOrder()
    {
        ResolvedRequest request = ResolvedRequestFactory.Create(
            "DELETE",
            s_url,
            headers:
            [
                ("Content-Length", "5", 12),
                ("Accept", "*/*", 13),
                ("Content-Encoding", "gzip", 14),
            ]);
        List<string> warnings = [];

        using HttpRequestMessage message = RequestMessageBuilder.Build(request, warnings);

        Assert.Equal(
            [
                "Header 'Content-Length' ignored: a request without a body cannot have content headers (line 12).",
                "Header 'Content-Encoding' ignored: a request without a body cannot have content headers (line 14).",
            ],
            warnings);
        Assert.Null(message.Content);
        Assert.Equal(["Accept"], message.Headers.NonValidated.Select(header => header.Key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    public void Build_HeaderNameHttpClientRefuses_ThrowsNamingHeaderAndLineOnly(string? body)
    {
        ResolvedRequest request = ResolvedRequestFactory.Create(
            "POST",
            s_url,
            body,
            headers: [("X Bad", Secret, 9)]);

        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => RequestMessageBuilder.Build(request, []));

        Assert.Equal("Header 'X Bad' is not a valid HTTP header and cannot be sent (line 9).", exception.Message);
    }

    [Theory]
    [InlineData("Authorization", "\r")]
    [InlineData("Authorization", "\n")]
    [InlineData("Authorization", "\r\n")]
    [InlineData("Authorization", "\0")]
    [InlineData("Content-Type", "\r")]
    [InlineData("Content-Type", "\n")]
    [InlineData("Content-Type", "\r\n")]
    [InlineData("Content-Type", "\0")]
    public void Build_HeaderValueWithLineBreak_ThrowsNamingHeaderAndLineOnly(string name, string lineBreak)
    {
        ResolvedRequest request = ResolvedRequestFactory.Create(
            "POST",
            s_url,
            body: "{}",
            headers: [(name, $"Bearer {Secret}{lineBreak}X-Injected: 1", 9)]);

        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => RequestMessageBuilder.Build(request, []));

        Assert.Equal(
            $"Header '{name}' has a value that contains a line break and cannot be sent (line 9).",
            exception.Message);
        Assert.DoesNotContain(Secret, exception.Message);
    }

    [Fact]
    public void Build_ContentHeaderValueWithLineBreakWithoutBody_ThrowsInsteadOfWarning()
    {
        ResolvedRequest request = ResolvedRequestFactory.Create(
            "GET",
            s_url,
            headers: [("Content-Type", $"text/plain\r\nX-Injected: {Secret}", 3)]);
        List<string> warnings = [];

        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => RequestMessageBuilder.Build(request, warnings));

        Assert.Equal(
            "Header 'Content-Type' has a value that contains a line break and cannot be sent (line 3).",
            exception.Message);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Build_Http10_UsesVersion10()
    {
        ResolvedRequest request = ResolvedRequestFactory.Create("GET", s_url, httpVersion: "HTTP/1.0");

        using HttpRequestMessage message = RequestMessageBuilder.Build(request, []);

        Assert.Equal(HttpVersion.Version10, message.Version);
    }

    [Fact]
    public void Build_HeaderLinesNotParallelToHeaders_ThrowsArgumentException()
    {
        ResolvedRequest request = new("GET", s_url, HttpVersion: null, [new("Accept", "*/*")], Body: null)
        {
            DisplayUrl = s_url.OriginalString,
        };

        Assert.Throws<ArgumentException>(() => RequestMessageBuilder.Build(request, []));
    }
}
