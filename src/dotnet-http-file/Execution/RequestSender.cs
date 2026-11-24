using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Authentication;
using DotNetHttpFile.Resolution;

namespace DotNetHttpFile.Execution;

/// <summary>
/// Sends requests and follows the redirects they are answered with.
/// </summary>
/// <param name="httpClient">The client to send through. Its handler must not follow redirects itself.</param>
/// <param name="secretMask">The provider-resolved values of the run.</param>
/// <param name="reportRedirect">Receives a message for each redirect that is followed.</param>
internal sealed class RequestSender(HttpClient httpClient, SecretMask secretMask, Action<string> reportRedirect)
{
    /// <summary>
    /// Sends <paramref name="request"/>, follows redirects as <see cref="RedirectPolicy"/> says, and reads the
    /// whole final response into memory, decoding a gzip or deflate body.
    /// </summary>
    /// <param name="request">The request to send.</param>
    /// <param name="warnings">
    /// Receives a message for each header that is left out of the request, apart from <c>Content-Type</c>, and one
    /// when the body is in a coding that is not decoded.
    /// </param>
    /// <param name="log">Receives the diagnostic lines of the first send, not of a redirect that follows.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>
    /// The final response, with the headers as received (one entry per value), the body decoded, and the time from
    /// sending to the end of the body, redirects included. It is itself a redirect when that one could not be
    /// followed.
    /// </returns>
    /// <exception cref="HttpFileException">
    /// The request could not be built or sent, or it timed out. The message names the display URL of the
    /// request or of the redirect that failed, and never a header value or the body. Also thrown, with
    /// <see cref="HttpFileException.ExitCode"/> set to <see cref="ExitCodes.ErrorStatus"/>, when the body is not
    /// valid in its coding or decodes to more than <see cref="ContentDecoder.MaxDecodedLength"/> bytes.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled; the exception carries that token.
    /// </exception>
    public async Task<ReceivedResponse> SendAsync(
        ResolvedRequest request,
        ICollection<string> warnings,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The client's timeout bounds one send. This bounds the request together with its redirects.
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(httpClient.Timeout);

        ResolvedRequest current = request;
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            for (int redirectCount = 0; ; redirectCount++)
            {
                // A redirect that drops the body leaves its content headers behind, which is not worth a warning.
                using HttpRequestMessage message = RequestMessageBuilder.Build(
                    current,
                    redirectCount == 0 ? warnings : [],
                    redirectCount == 0 ? log : null);

                // ResponseContentRead buffers the body inside the call, so the timeout covers reading it.
                using HttpResponseMessage response = await httpClient
                    .SendAsync(message, HttpCompletionOption.ResponseContentRead, timeout.Token)
                    .ConfigureAwait(false);

                // Past the limit the redirect itself is the response, as it is with HttpClient.
                ResolvedRequest? next = redirectCount < RedirectPolicy.MaxRedirects
                    ? RedirectPolicy.Next(current, response, secretMask)
                    : null;
                if (next is not null)
                {
                    reportRedirect(Messages.Format(Resources.Redirected, next.DisplayUrl, (int)response.StatusCode));
                    current = next;
                    continue;
                }

                byte[] body = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
                stopwatch.Stop();

                ReceivedResponse received = new(
                    response.Version,
                    (int)response.StatusCode,
                    response.ReasonPhrase ?? string.Empty,
                    ReadHeaders(response),
                    body,
                    stopwatch.Elapsed);

                return Decode(current, received, body, warnings, cancellationToken);
            }
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Only the caller and the timeout can cancel a send, and the caller did not.
            string text = Messages.Format(
                Resources.SendTimedOut,
                current.Method,
                current.DisplayUrl,
                httpClient.Timeout.TotalSeconds);

            throw new HttpFileException(text, exception);
        }
        catch (OperationCanceledException exception)
        {
            // HttpClient cancels through a token of its own, so its exception does not name the caller's.
            throw new OperationCanceledException(exception.Message, exception, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            // For a TLS failure the outer message only says to look at the inner one.
            string text = exception.InnerException is AuthenticationException authenticationException
                ? Messages.Format(
                    Resources.SendFailedCertificateNotTrusted,
                    current.Method,
                    current.DisplayUrl,
                    authenticationException.Message)
                : Messages.Format(Resources.SendFailed, current.Method, current.DisplayUrl, exception.Message);

            throw new HttpFileException(text, exception);
        }
    }

    private static ReceivedResponse Decode(
        ResolvedRequest request,
        ReceivedResponse response,
        byte[] body,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        if (body.Length == 0)
        {
            return response;
        }

        IReadOnlyList<string> unsupportedCodings = response.UnsupportedCodings;
        if (unsupportedCodings.Count > 0)
        {
            warnings.Add(Messages.Format(
                Resources.ResponseNotDecoded,
                request.Method,
                request.DisplayUrl,
                string.Join(Messages.ListSeparator, unsupportedCodings)));

            return response;
        }

        IReadOnlyList<string> codings = response.ContentCodings;
        if (codings.Count == 0)
        {
            return response;
        }

        // The last coding listed is the last one applied, so it comes off first.
        byte[] decoded = body;
        for (int index = codings.Count - 1; index >= 0; index--)
        {
            string coding = codings[index];
            byte[]? next;
            try
            {
                next = ContentDecoder.Decode(decoded, coding, ContentDecoder.MaxDecodedLength, cancellationToken);
            }
            catch (InvalidDataException exception)
            {
                throw DecodeFailure(Resources.ResponseNotValidCoding, request, response, coding, exception);
            }

            decoded = next ?? throw DecodeFailure(Resources.ResponseDecodedTooLarge, request, response, coding);
        }

        return response with { Body = decoded };
    }

    private static HttpFileException DecodeFailure(
        string format,
        ResolvedRequest request,
        ReceivedResponse response,
        string coding,
        Exception? innerException = null) =>
        new(
            Messages.Format(
                format,
                request.Method,
                request.DisplayUrl,
                response.StatusCode,
                coding,
                ContentDecoder.MaxDecodedMebibytes),
            innerException)
        {
            ExitCode = ExitCodes.ErrorStatus,
        };

    // The non-validated view gives each value as it was received: the validated one parses and rewrites them.
    private static List<KeyValuePair<string, string>> ReadHeaders(HttpResponseMessage response)
    {
        List<KeyValuePair<string, string>> headers = [];
        AddHeaders(headers, response.Headers.NonValidated);
        AddHeaders(headers, response.Content.Headers.NonValidated);

        return headers;
    }

    private static void AddHeaders(List<KeyValuePair<string, string>> headers, HttpHeadersNonValidated source)
    {
        foreach (KeyValuePair<string, HeaderStringValues> header in source)
        {
            foreach (string value in header.Value)
            {
                headers.Add(KeyValuePair.Create(header.Key, value));
            }
        }
    }
}
