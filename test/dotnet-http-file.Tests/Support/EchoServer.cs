using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace DotNetHttpFile.Tests.Support;

/// <summary>A request as <see cref="EchoServer"/> received it.</summary>
/// <param name="Method">The method on the request line.</param>
/// <param name="Target">The raw request target, with its query string.</param>
/// <param name="Protocol">The protocol on the request line, such as <c>HTTP/1.1</c>.</param>
/// <param name="Headers">The headers in the order Kestrel exposes them, one entry per value.</param>
/// <param name="Body">The body bytes as received.</param>
internal sealed record RecordedRequest(
    string Method,
    string Target,
    string Protocol,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    ReadOnlyMemory<byte> Body)
{
    public string RequestLine => $"{Method} {Target} {Protocol}";

    /// <summary>The values of the header named <paramref name="name"/>, in the order received.</summary>
    public IReadOnlyList<string> HeaderValues(string name) =>
        [.. Headers.Where(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
            .Select(header => header.Value)];
}

/// <summary>
/// Mirrors the capture server used to record the Visual Studio fixtures (<c>echo_server.py</c>, kept outside
/// the repo): the same endpoints and bodies, on a free loopback port, recording every request it receives.
/// </summary>
internal sealed class EchoServer : IAsyncDisposable
{
    public const string JsonBody =
        """{"str": "abc def", "num": 42, "dec": 3.5, "bool": true, "nul": null, "obj": {"a": 1, "b": "two"}, "arr": [1, 2, 3], "items": [{"id": 1}, {"id": 2}]}""";

    public const string XmlBody =
        """<?xml version="1.0" encoding="utf-8"?><root><token>xml-token-123</token><n>7</n></root>""";

    public const string JsonContentType = "application/json; charset=utf-8";
    public const string XmlContentType = "application/xml; charset=utf-8";
    public const string RedirectLocation = "/echo?redirected=1";

    /// <summary>
    /// Not in echo_server.py: redirects as <c>/redirect</c> does and also sets <see cref="RedirectCookie"/>.
    /// </summary>
    public const string RedirectWithCookiePath = "/redirect-with-cookie";

    public const string RedirectCookie = "session=echo-cookie; Path=/";

    /// <summary>Not in echo_server.py: redirects to itself with 302, without end.</summary>
    public const string RedirectLoopPath = "/redirect-loop";

    /// <summary>Not in echo_server.py: answers 404 with <see cref="NotFoundBody"/>.</summary>
    public const string NotFoundPath = "/not-found";

    public const string NotFoundBody = """{"error": "not found"}""";

    private const string LoopbackAnyPort = "http://127.0.0.1:0";
    private const string JsonPath = "/json";
    private const string XmlPath = "/xml";
    private const string RedirectPath = "/redirect";
    private const string RedirectToPath = "/redirect-to";
    private const string StatusParameter = "status";
    private const string LocationParameter = "location";
    private const string ChunkedFraming = "chunked";
    private const string ContentLengthHeader = "Content-Length";

    // Keeps + and quotes readable in the echoed entry, as Python's json.dumps does.
    private static readonly JsonWriterOptions s_echoOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly WebApplication _application;
    private readonly ConcurrentQueue<RecordedRequest> _requests;

    private EchoServer(WebApplication application, ConcurrentQueue<RecordedRequest> requests, Uri baseUri)
    {
        _application = application;
        _requests = requests;
        BaseUri = baseUri;
    }

    /// <summary>The address the server listens on, such as <c>http://127.0.0.1:50123/</c>.</summary>
    public Uri BaseUri { get; }

    /// <summary>A snapshot of the requests received so far, in the order they arrived.</summary>
    public IReadOnlyList<RecordedRequest> Requests => [.. _requests];

    /// <summary>
    /// Not in echo_server.py: the URL of an endpoint that answers with <paramref name="statusCode"/> and
    /// <paramref name="location"/> as its <c>Location</c> header.
    /// </summary>
    public Uri RedirectUrl(int statusCode, string location)
    {
        string escapedLocation = Uri.EscapeDataString(location);
        string target = string.Create(
            CultureInfo.InvariantCulture,
            $"{RedirectToPath}?{StatusParameter}={statusCode}&{LocationParameter}={escapedLocation}");

        return new Uri(BaseUri, target);
    }

    public static async Task<EchoServer> StartAsync(CancellationToken cancellationToken = default)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(LoopbackAnyPort);

        WebApplication application = builder.Build();
        ConcurrentQueue<RecordedRequest> requests = new();
        application.Run(context => HandleAsync(context, requests));

        try
        {
            await application.StartAsync(cancellationToken);

            return new EchoServer(application, requests, new Uri(application.Urls.Single()));
        }
        catch
        {
            await application.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync() => _application.DisposeAsync();

    private static async Task HandleAsync(HttpContext context, ConcurrentQueue<RecordedRequest> requests)
    {
        HttpRequest request = context.Request;

        using MemoryStream buffer = new();
        await request.Body.CopyToAsync(buffer, context.RequestAborted);

        List<KeyValuePair<string, string>> headers = [];
        foreach (KeyValuePair<string, StringValues> header in request.Headers)
        {
            foreach (string? value in header.Value)
            {
                headers.Add(KeyValuePair.Create(header.Key, value ?? string.Empty));
            }
        }

        RecordedRequest recorded = new(
            request.Method,
            context.Features.GetRequiredFeature<IHttpRequestFeature>().RawTarget,
            request.Protocol,
            headers,
            buffer.ToArray());
        requests.Enqueue(recorded);

        string path = request.Path.Value ?? string.Empty;
        switch (path)
        {
            case JsonPath:
                await WriteAsync(context, JsonContentType, Encoding.UTF8.GetBytes(JsonBody));
                break;
            case XmlPath:
                await WriteAsync(context, XmlContentType, Encoding.UTF8.GetBytes(XmlBody));
                break;
            case RedirectWithCookiePath:
                context.Response.Headers.SetCookie = RedirectCookie;
                goto case RedirectPath;
            case RedirectPath:
                context.Response.StatusCode = StatusCodes.Status302Found;
                context.Response.Headers.Location = RedirectLocation;
                context.Response.ContentLength = 0;
                break;
            case RedirectToPath:
                context.Response.StatusCode = int.Parse(
                    request.Query[StatusParameter].ToString(),
                    CultureInfo.InvariantCulture);
                context.Response.Headers.Location = request.Query[LocationParameter].ToString();
                context.Response.ContentLength = 0;
                break;
            case RedirectLoopPath:
                context.Response.StatusCode = StatusCodes.Status302Found;
                context.Response.Headers.Location = RedirectLoopPath;
                context.Response.ContentLength = 0;
                break;
            case NotFoundPath:
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await WriteAsync(context, JsonContentType, Encoding.UTF8.GetBytes(NotFoundBody));
                break;
            default:
                await WriteAsync(context, JsonContentType, EchoEntry(recorded, request));
                break;
        }
    }

    private static async Task WriteAsync(HttpContext context, string contentType, byte[] payload)
    {
        context.Response.ContentType = contentType;
        context.Response.ContentLength = payload.Length;
        await context.Response.Body.WriteAsync(payload, context.RequestAborted);
    }

    // The same properties as the entry echo_server.py logs and echoes.
    private static byte[] EchoEntry(RecordedRequest recorded, HttpRequest request)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer, s_echoOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("at", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("request_line", recorded.RequestLine);
            writer.WriteStartArray("headers");
            foreach (KeyValuePair<string, string> header in recorded.Headers)
            {
                writer.WriteStartArray();
                writer.WriteStringValue(header.Key);
                writer.WriteStringValue(header.Value);
                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.WriteString("framing", Framing(request));
            writer.WriteString("body_repr", PythonBytesRepr(recorded.Body.Span));
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static string Framing(HttpRequest request)
    {
        if (string.Equals(request.Headers.TransferEncoding, ChunkedFraming, StringComparison.OrdinalIgnoreCase))
        {
            return ChunkedFraming;
        }

        string? contentLength = request.Headers[ContentLengthHeader];

        return $"content-length={contentLength ?? "None"}";
    }

    // Python's repr() of a bytes value, which is what echo_server.py writes.
    private static string PythonBytesRepr(ReadOnlySpan<byte> bytes)
    {
        char quote = bytes.Contains((byte)'\'') && !bytes.Contains((byte)'"') ? '"' : '\'';

        StringBuilder builder = new();
        builder.Append('b').Append(quote);
        foreach (byte value in bytes)
        {
            char character = (char)value;
            if (character == '\\' || character == quote)
            {
                builder.Append('\\').Append(character);
            }
            else if (character == '\t')
            {
                builder.Append(@"\t");
            }
            else if (character == '\n')
            {
                builder.Append(@"\n");
            }
            else if (character == '\r')
            {
                builder.Append(@"\r");
            }
            else if (value is < 0x20 or >= 0x7F)
            {
                builder.Append(CultureInfo.InvariantCulture, $@"\x{value:x2}");
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.Append(quote).ToString();
    }
}
