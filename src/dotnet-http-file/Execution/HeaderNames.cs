namespace DotNetHttpFile.Execution;

/// <summary>
/// The header names the tool acts on, and how a header is matched to one.
/// </summary>
internal static class HeaderNames
{
    public const string Authorization = "Authorization";
    public const string ContentEncoding = "Content-Encoding";
    public const string ContentType = "Content-Type";
    public const string Cookie = "Cookie";
    public const string ProxyAuthorization = "Proxy-Authorization";
    public const string TransferEncoding = "Transfer-Encoding";
    public const string WwwAuthenticate = "WWW-Authenticate";

    /// <summary>
    /// Whether <paramref name="header"/> is named <paramref name="name"/>, ignoring case as HTTP does.
    /// </summary>
    public static bool Is(KeyValuePair<string, string> header, string name) =>
        string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase);
}
