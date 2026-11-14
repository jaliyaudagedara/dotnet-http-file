namespace DotNetHttpFile.Execution;

/// <summary>A request with every variable and reference substituted, as it is sent.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Url">
/// The URL to send to. For sending only: never print it or any text derived from it. <see cref="Uri"/> rewrites
/// the text (escaping, host case), so a provider-resolved value in it no longer matches what the secret mask
/// knows. Print <see cref="DisplayUrl"/> instead.
/// </param>
/// <param name="HttpVersion">The version written on the request line, or null.</param>
/// <param name="Headers">The headers in file order.</param>
/// <param name="Body">The body, or null when the request has none.</param>
internal sealed record ResolvedRequest(
    string Method,
    Uri Url,
    string? HttpVersion,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    string? Body)
{
    /// <summary>
    /// The URL as resolved from the file, with provider-resolved values already masked. The only URL to print.
    /// </summary>
    public required string DisplayUrl { get; init; }

    /// <summary>
    /// The file line of each header, parallel to <see cref="ResolvedRequest.Headers"/>. Needed only to build the
    /// message that is sent, whose header warnings and errors name the line.
    /// </summary>
    public IReadOnlyList<int> HeaderLines { get; init; } = [];

    // The generated record text would print the URL, headers and body, which can hold provider-resolved values.
    public override string ToString() => $"{Method} {DisplayUrl}";
}

/// <summary>A response as it was received. <see cref="Body"/> holds the undecoded bytes.</summary>
internal sealed record ReceivedResponse(
    Version HttpVersion,
    int StatusCode,
    string ReasonPhrase,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    ReadOnlyMemory<byte> Body,
    TimeSpan Elapsed)
{
    private const string ContentTypeHeader = "Content-Type";
    private const string ContentEncodingHeader = "Content-Encoding";
    private const string IdentityCoding = "identity";
    private const char CodingSeparator = ',';

    /// <summary>The first <c>Content-Type</c> header value, or null when the response has none.</summary>
    public string? ContentType
    {
        get
        {
            foreach (KeyValuePair<string, string> header in Headers)
            {
                if (string.Equals(header.Key, ContentTypeHeader, StringComparison.OrdinalIgnoreCase))
                {
                    return header.Value;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Whether a <c>Content-Encoding</c> other than <c>identity</c> applies, so that <see cref="Body"/> holds
    /// the encoded bytes and not the content itself.
    /// </summary>
    public bool IsContentEncoded =>
        Headers.Any(header =>
            string.Equals(header.Key, ContentEncodingHeader, StringComparison.OrdinalIgnoreCase)
            && header.Value
                .Split(CodingSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(coding => !string.Equals(coding, IdentityCoding, StringComparison.OrdinalIgnoreCase)));

    public bool IsSuccess => StatusCode is >= 200 and <= 299;
}

/// <summary>A sent request and the response it received.</summary>
internal sealed record HttpExchange(ResolvedRequest Request, ReceivedResponse Response);
