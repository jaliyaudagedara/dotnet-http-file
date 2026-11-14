using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using DotNetHttpFile.Resolution;

namespace DotNetHttpFile.Execution;

/// <summary>Sends requests and follows the redirects they are answered with.</summary>
/// <param name="httpClient">The client to send through. Its handler must not follow redirects itself.</param>
/// <param name="secretMask">The provider-resolved values of the run.</param>
/// <param name="reportRedirect">Receives a message for each redirect that is followed.</param>
internal sealed class RequestSender(HttpClient httpClient, SecretMask secretMask, Action<string> reportRedirect)
{
    private static readonly CompositeFormat s_redirectedFormat =
        CompositeFormat.Parse(Resources.Redirected);

    private static readonly CompositeFormat s_sendFailedFormat =
        CompositeFormat.Parse(Resources.SendFailed);

    private static readonly CompositeFormat s_sendFailedCertificateNotTrustedFormat =
        CompositeFormat.Parse(Resources.SendFailedCertificateNotTrusted);

    private static readonly CompositeFormat s_sendTimedOutFormat =
        CompositeFormat.Parse(Resources.SendTimedOut);

    /// <summary>
    /// Sends <paramref name="request"/>, follows redirects as <see cref="RedirectPolicy"/> says, and reads the
    /// whole final response into memory.
    /// </summary>
    /// <param name="request">The request to send.</param>
    /// <param name="warnings">Receives a message for each header that is left out of the request.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>
    /// The final response, with the headers as received (one entry per value) and the time from sending to the
    /// end of the body, redirects included. It is itself a redirect when that one could not be followed.
    /// </returns>
    /// <exception cref="HttpFileException">
    /// The request could not be built or sent, or it timed out. The message names the display URL of the
    /// request or of the redirect that failed, and never a header value or the body.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled; the exception carries that token.
    /// </exception>
    public async Task<ReceivedResponse> SendAsync(
        ResolvedRequest request,
        ICollection<string> warnings,
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
                    redirectCount == 0 ? warnings : []);

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
                    reportRedirect(Format(s_redirectedFormat, next.DisplayUrl, (int)response.StatusCode));
                    current = next;
                    continue;
                }

                byte[] body = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
                stopwatch.Stop();

                return new ReceivedResponse(
                    response.Version,
                    (int)response.StatusCode,
                    response.ReasonPhrase ?? string.Empty,
                    ReadHeaders(response),
                    body,
                    stopwatch.Elapsed);
            }
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Only the caller and the timeout can cancel a send, and the caller did not.
            throw new HttpFileException(
                Format(s_sendTimedOutFormat, current.Method, current.DisplayUrl, httpClient.Timeout.TotalSeconds),
                exception);
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
                ? Format(
                    s_sendFailedCertificateNotTrustedFormat,
                    current.Method,
                    current.DisplayUrl,
                    authenticationException.Message)
                : Format(s_sendFailedFormat, current.Method, current.DisplayUrl, exception.Message);

            throw new HttpFileException(text, exception);
        }
    }

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

    private static string Format(CompositeFormat format, params ReadOnlySpan<object?> arguments) =>
        string.Format(CultureInfo.CurrentCulture, format, arguments);
}
