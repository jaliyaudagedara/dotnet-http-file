using System.Net;
using DotNetHttpFile.Resolution;

namespace DotNetHttpFile.Execution;

/// <summary>
/// Decides how a redirect is followed. The rules are those of HttpClient's own redirect handling, except for
/// the headers: HttpClient clears <c>Authorization</c> on every redirect and nothing else, while this keeps
/// every header for the same origin and drops each one that can hold a credential for another origin.
/// </summary>
internal static class RedirectPolicy
{
    /// <summary>The default of <see cref="SocketsHttpHandler.MaxAutomaticRedirections"/>.</summary>
    public const int MaxRedirects = 50;

    private const string AuthorizationHeader = "Authorization";
    private const string ProxyAuthorizationHeader = "Proxy-Authorization";
    private const string CookieHeader = "Cookie";
    private const string TransferEncodingHeader = "Transfer-Encoding";

    /// <summary>
    /// Creates the request that follows <paramref name="response"/>, or returns null when the response is
    /// final: it is not a redirect, it names no usable location, or it leads from https to http.
    /// </summary>
    /// <param name="request">The request that <paramref name="response"/> answers.</param>
    /// <param name="response">The response to <paramref name="request"/>.</param>
    /// <param name="secretMask">The provider-resolved values of the run.</param>
    public static ResolvedRequest? Next(ResolvedRequest request, HttpResponseMessage response, SecretMask secretMask)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(secretMask);

        Uri? location = GetLocation(request.Url, response);
        if (location is null)
        {
            return null;
        }

        bool isSameOrigin = Uri.Compare(
            request.Url,
            location,
            UriComponents.SchemeAndServer,
            UriFormat.UriEscaped,
            StringComparison.OrdinalIgnoreCase) == 0;
        bool isChangedToGet = IsChangedToGet(response.StatusCode, request.Method);

        List<KeyValuePair<string, string>> headers = [];
        List<int> headerLines = [];
        for (int index = 0; index < request.Headers.Count; index++)
        {
            KeyValuePair<string, string> header = request.Headers[index];
            bool isDropped = (!isSameOrigin && CanHoldCredential(header, secretMask))
                || (isChangedToGet && IsHeader(header, TransferEncodingHeader));

            if (!isDropped)
            {
                headers.Add(header);
                headerLines.Add(request.HeaderLines[index]);
            }
        }

        return request with
        {
            Method = isChangedToGet ? HttpMethod.Get.Method : request.Method,
            Url = location,
            Headers = headers,
            Body = isChangedToGet ? null : request.Body,
            DisplayUrl = secretMask.Apply(location.AbsoluteUri),
            HeaderLines = headerLines,
        };
    }

    private static Uri? GetLocation(Uri requestUrl, HttpResponseMessage response)
    {
        bool isRedirect = response.StatusCode
            is HttpStatusCode.MultipleChoices
            or HttpStatusCode.Moved
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

        Uri? location = response.Headers.Location;
        if (!isRedirect || location is null)
        {
            return null;
        }

        if (!location.IsAbsoluteUri)
        {
            location = new Uri(requestUrl, location);
        }

        // RFC 9110 section 10.2.2: a location without a fragment inherits the one of the request.
        if (requestUrl.Fragment.Length > 0 && location.Fragment.Length == 0)
        {
            location = new UriBuilder(location) { Fragment = requestUrl.Fragment }.Uri;
        }

        // As HttpClient: never from https down to http, and to no other scheme.
        bool isAllowedScheme = location.Scheme == Uri.UriSchemeHttps
            || (location.Scheme == Uri.UriSchemeHttp && requestUrl.Scheme != Uri.UriSchemeHttps);

        return isAllowedScheme ? location : null;
    }

    // As HttpClient and browsers: a POST becomes a GET on 300, 301 and 302, and so does every method but GET
    // and HEAD on 303. The body is not sent again. 307 and 308 repeat the request as it was.
    private static bool IsChangedToGet(HttpStatusCode statusCode, string method) =>
        statusCode switch
        {
            HttpStatusCode.MultipleChoices or HttpStatusCode.Moved or HttpStatusCode.Found =>
                IsMethod(method, HttpMethod.Post),
            HttpStatusCode.SeeOther => !IsMethod(method, HttpMethod.Get) && !IsMethod(method, HttpMethod.Head),
            _ => false,
        };

    private static bool IsMethod(string method, HttpMethod expected) =>
        string.Equals(method, expected.Method, StringComparison.OrdinalIgnoreCase);

    private static bool CanHoldCredential(KeyValuePair<string, string> header, SecretMask secretMask) =>
        IsHeader(header, AuthorizationHeader)
        || IsHeader(header, ProxyAuthorizationHeader)
        || IsHeader(header, CookieHeader)
        || !string.Equals(secretMask.Apply(header.Value), header.Value, StringComparison.Ordinal);

    private static bool IsHeader(KeyValuePair<string, string> header, string name) =>
        string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase);
}
