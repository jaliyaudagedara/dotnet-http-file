using System.Net;

namespace DotNetHttpFile.Execution;

internal static class ToolHttpClient
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(100);

    /// <summary>
    /// Creates the handler requests are sent through. It adds nothing to a request: no <c>Accept-Encoding</c>,
    /// no <c>traceparent</c> and no stored cookie. It does not follow redirects; <see cref="RequestSender"/>
    /// does, so that it can decide which headers another origin receives.
    /// </summary>
    /// <param name="insecure">True to accept any server certificate (<c>--insecure</c>).</param>
    public static SocketsHttpHandler CreateHandler(bool insecure)
    {
        SocketsHttpHandler handler = new()
        {
            // The handler would forward every header but Authorization to wherever a redirect leads.
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,

            // A stored Set-Cookie would come back as a Cookie header that nobody wrote.
            UseCookies = false,

            // The default propagator adds traceparent whenever an activity is current.
            ActivityHeadersPropagator = null,
        };

        if (insecure)
        {
            // --insecure asks for exactly this, for servers with a self-signed or development certificate.
#pragma warning disable CA5359 // Do not disable certificate validation
            handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
#pragma warning restore CA5359
        }

        return handler;
    }

    /// <summary>Creates the client for <paramref name="handler"/>, which the client then owns.</summary>
    public static HttpClient Create(HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return new HttpClient(handler)
        {
            Timeout = Timeout,
        };
    }
}
