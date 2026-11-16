using System.Buffers;
using System.Net;
using System.Net.Mime;
using System.Text;

namespace DotNetHttpFile.Execution;

internal static class RequestMessageBuilder
{
    public const string DefaultContentType = MediaTypeNames.Text.Plain + "; charset=utf-8";

    private static readonly SearchValues<char> s_lineBreakCharacters = SearchValues.Create("\r\n\0");

    private static readonly CompositeFormat s_contentHeaderWithoutBodyFormat =
        CompositeFormat.Parse(Resources.ContentHeaderWithoutBody);

    private static readonly CompositeFormat s_droppedContentTypeFormat =
        CompositeFormat.Parse(Resources.DroppedContentType);

    private static readonly CompositeFormat s_headerNotSendableFormat =
        CompositeFormat.Parse(Resources.HeaderNotSendable);

    private static readonly CompositeFormat s_headerValueHasLineBreakFormat =
        CompositeFormat.Parse(Resources.HeaderValueHasLineBreak);

    private static readonly CompositeFormat s_unsupportedHttpVersionFormat =
        CompositeFormat.Parse(Resources.UnsupportedHttpVersion);

    /// <summary>
    /// Builds the message that sends <paramref name="request"/>. The caller disposes it.
    /// </summary>
    /// <param name="request">The request, whose header lines are parallel to its headers.</param>
    /// <param name="warnings">
    /// Receives a message for each header that is left out: a content header on a request without a body,
    /// apart from <c>Content-Type</c>.
    /// </param>
    /// <param name="log">Receives a line for a <c>Content-Type</c> that is left out for want of a body.</param>
    /// <remarks>
    /// Headers are added as written, without validation or rewriting. Nothing is added apart from the default
    /// <c>Content-Type</c> of a body that has none.
    /// </remarks>
    /// <exception cref="HttpFileException">
    /// The HTTP version is not supported, or a header cannot be sent: its name is not valid, or its value holds
    /// a line break or a null character. The message never holds a header value.
    /// </exception>
    public static HttpRequestMessage Build(
        ResolvedRequest request,
        ICollection<string> warnings,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(warnings);

        if (request.HeaderLines.Count != request.Headers.Count)
        {
            throw new ArgumentException(
                $"{nameof(ResolvedRequest.HeaderLines)} must hold one line for each header.",
                nameof(request));
        }

        HttpRequestMessage message = new(new HttpMethod(request.Method), request.Url)
        {
            Version = ParseVersion(request.HttpVersion),
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };

        try
        {
            if (request.Body is not null)
            {
                message.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(request.Body));
            }

            AddHeaders(message, request, warnings, log);

            if (message.Content is not null && !message.Content.Headers.NonValidated.Contains(HeaderNames.ContentType))
            {
                message.Content.Headers.TryAddWithoutValidation(HeaderNames.ContentType, DefaultContentType);
            }

            return message;
        }
        catch
        {
            message.Dispose();
            throw;
        }
    }

    private static Version ParseVersion(string? httpVersion) =>
        httpVersion switch
        {
            null or "HTTP/1.1" => HttpVersion.Version11,
            "HTTP/1.0" => HttpVersion.Version10,
            "HTTP/2" or "HTTP/2.0" => HttpVersion.Version20,
            "HTTP/3" or "HTTP/3.0" => HttpVersion.Version30,
            _ => throw new HttpFileException(Messages.Format(s_unsupportedHttpVersionFormat, httpVersion)),
        };

    private static void AddHeaders(
        HttpRequestMessage message,
        ResolvedRequest request,
        ICollection<string> warnings,
        Action<string>? log)
    {
        // HttpClient keeps content headers apart and refuses them on the request, so its own answer decides
        // which header is which. Without a body there is no content to hold them, and this one is never sent.
        using ByteArrayContent? contentHeaderProbe = message.Content is null ? new ByteArrayContent([]) : null;

        for (int index = 0; index < request.Headers.Count; index++)
        {
            (string name, string value) = request.Headers[index];
            int line = request.HeaderLines[index];

            // Nothing below validates the value, so a line break in it would go out as a header of its own.
            if (value.AsSpan().ContainsAny(s_lineBreakCharacters))
            {
                throw new HttpFileException(Messages.Format(s_headerValueHasLineBreakFormat, name, line));
            }

            if (message.Headers.TryAddWithoutValidation(name, value))
            {
                continue;
            }

            if (message.Content is not null && message.Content.Headers.TryAddWithoutValidation(name, value))
            {
                continue;
            }

            if (contentHeaderProbe is not null && contentHeaderProbe.Headers.TryAddWithoutValidation(name, value))
            {
                // Visual Studio sends no Content-Type without a body and does not warn, so neither does this.
                if (HeaderNames.Is(request.Headers[index], HeaderNames.ContentType))
                {
                    log?.Invoke(Messages.Format(s_droppedContentTypeFormat, line));
                }
                else
                {
                    warnings.Add(Messages.Format(s_contentHeaderWithoutBodyFormat, name, line));
                }

                continue;
            }

            throw new HttpFileException(Messages.Format(s_headerNotSendableFormat, name, line));
        }
    }
}
