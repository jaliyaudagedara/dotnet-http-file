using System.Buffers;
using System.Globalization;
using DotNetHttpFile.Execution;
using DotNetHttpFile.Parsing;
using DotNetHttpFile.Resolution;
using Spectre.Console;

namespace DotNetHttpFile.Output;

/// <summary>
/// Writes exchanges, warnings and errors to the console. What the tool says about the request it built, and its
/// warnings and errors, go through the secret mask. The response is shown as the server sent it. Everything
/// written as text has its control characters made visible; the response bytes of <see cref="OutputMode.Body"/>
/// are written as received.
/// </summary>
internal sealed class ResponseRenderer(ConsoleStreams console, SecretMask secretMask, OutputMode mode)
{
    private const int VersionFieldCount = 2;
    private const int BytesPerKilobyte = 1024;
    private const int BytesPerMegabyte = BytesPerKilobyte * BytesPerKilobyte;

    // The decimals of the F1 format a size is written with.
    private const int SizeDecimals = 1;

    // The characters of an RFC 9110 token, which is what an authentication scheme is made of.
    private static readonly SearchValues<char> s_tokenCharacters = SearchValues.Create(
        "!#$%&'*+-.^_`|~0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz");

    private static readonly Style s_successStyle = new(foreground: Color.Green);
    private static readonly Style s_redirectStyle = new(foreground: Color.Yellow);
    private static readonly Style s_failureStyle = new(foreground: Color.Red);
    private static readonly Style s_warningStyle = new(foreground: Color.Yellow);
    private static readonly Style s_dependencyStyle = new(foreground: Color.Grey);
    private static readonly Style s_headerNameStyle = new(foreground: Color.Teal);
    private static readonly Style s_requestLineStyle = new(decoration: Decoration.Bold);

    /// <summary>
    /// Writes the one-line summary of a request that was sent because the selected request refers to it.
    /// </summary>
    public void RenderDependency(RequestDefinition request, HttpExchange exchange)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(exchange);

        ResolvedRequest sent = exchange.Request;
        string status = StatusText(exchange.Response);
        long milliseconds = Milliseconds(exchange.Response);
        string line = string.Create(
            CultureInfo.InvariantCulture,
            $"\u2192 {request.DisplayName}  {sent.Method} {sent.DisplayUrl}  {status}  {milliseconds} ms");

        WriteMaskedLines(mode == OutputMode.Full ? console.Output : console.Error, line, s_dependencyStyle);
    }

    /// <summary>
    /// Writes the exchange of the selected request as the mode asks. <see cref="OutputMode.Body"/> writes the
    /// status line with the time and the size to standard error, because standard output holds the bytes alone;
    /// <see cref="OutputMode.Headers"/> and <see cref="OutputMode.Status"/> write nothing to standard error.
    /// </summary>
    public async Task RenderAsync(HttpExchange exchange, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exchange);

        if (mode == OutputMode.Full)
        {
            RenderFull(exchange);

            return;
        }

        ReceivedResponse response = exchange.Response;
        if (mode == OutputMode.Body)
        {
            ConsoleText.WriteLines(console.Error, StatusLine(response), StatusStyle(response));

            await console.StandardOutput.WriteAsync(response.Body, cancellationToken).ConfigureAwait(false);
            await console.StandardOutput.FlushAsync(cancellationToken).ConfigureAwait(false);

            return;
        }

        ConsoleText.WriteLines(console.Output, ProtocolStatusText(response), StatusStyle(response));
        if (mode == OutputMode.Headers)
        {
            WriteHeaders(console.Output, response);
        }
    }

    /// <summary>
    /// Writes <paramref name="message"/>, the note that a redirect is followed, to standard error. It is not masked:
    /// it reports the location the server sent.
    /// </summary>
    public void RenderRedirect(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        ConsoleText.WriteLines(console.Error, message, s_dependencyStyle);
    }

    /// <summary>
    /// Writes <paramref name="message"/>, advice on a failed response, to standard error.
    /// </summary>
    public void RenderHint(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        WriteMaskedLines(console.Error, message, s_dependencyStyle);
    }

    /// <summary>
    /// Writes <paramref name="message"/> to standard error as a warning.
    /// </summary>
    public void RenderWarning(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        WriteMaskedLines(console.Error, Messages.Format(Resources.OutputWarning, message), s_warningStyle);
    }

    /// <summary>
    /// Writes <paramref name="message"/> to standard error as an error.
    /// </summary>
    public void RenderError(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        WriteMaskedLines(console.Error, Messages.Format(Resources.OutputError, message), s_failureStyle);
    }

    /// <summary>
    /// The status code of <paramref name="response"/>, followed by its reason phrase when it has one.
    /// </summary>
    internal static string StatusText(ReceivedResponse response) =>
        response.ReasonPhrase.Length == 0
            ? response.StatusCode.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{response.StatusCode} {response.ReasonPhrase}");

    /// <summary>
    /// The time <paramref name="response"/> took, in whole milliseconds.
    /// </summary>
    internal static long Milliseconds(ReceivedResponse response) =>
        (long)Math.Round(response.Elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero);

    private static string BodyDisplayText(ReceivedResponse response)
    {
        string? contentType = response.ContentType;

        // A body still in a coding the tool does not decode is not what its content type says.
        if (!response.IsContentEncoded && BodyFormatter.IsText(contentType, response.Body.Span))
        {
            return BodyFormatter.FormatForDisplay(contentType, response.Body);
        }

        string size = FormatSize(response.Body.Length);

        return string.IsNullOrWhiteSpace(contentType)
            ? Messages.Format(Resources.BinaryBodySummaryNoContentType, size)
            : Messages.Format(Resources.BinaryBodySummary, size, contentType);
    }

    private static string WithoutFinalLineBreak(string text)
    {
        if (text.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return text[..^2];
        }

        return text.EndsWith('\n') ? text[..^1] : text;
    }

    private static string RequestHeaderDisplayValue(KeyValuePair<string, string> header)
    {
        // A cookie has no scheme, and the first cookie pair must never be shown as one.
        if (HeaderNames.Is(header, HeaderNames.Cookie))
        {
            return SecretMask.Placeholder;
        }

        if (!HeaderNames.Is(header, HeaderNames.Authorization)
            && !HeaderNames.Is(header, HeaderNames.ProxyAuthorization))
        {
            return header.Value;
        }

        ReadOnlySpan<char> value = header.Value.AsSpan().Trim();
        int schemeLength = value.IndexOf(' ');
        if (schemeLength <= 0 || value[..schemeLength].ContainsAnyExcept(s_tokenCharacters))
        {
            return SecretMask.Placeholder;
        }

        return $"{value[..schemeLength]} {SecretMask.Placeholder}";
    }

    private static string StatusLine(ReceivedResponse response)
    {
        string size = FormatSize(response.Body.Length);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{ProtocolStatusText(response)}   {Milliseconds(response)} ms \u00B7 {size}");
    }

    private static string ProtocolStatusText(ReceivedResponse response) =>
        $"HTTP/{response.HttpVersion.ToString(VersionFieldCount)} {StatusText(response)}";

    private static Style StatusStyle(ReceivedResponse response) =>
        response.StatusCode switch
        {
            >= 200 and <= 299 => s_successStyle,
            >= 300 and <= 399 => s_redirectStyle,
            _ => s_failureStyle,
        };

    private static string FormatSize(long byteCount)
    {
        if (byteCount < BytesPerKilobyte)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{byteCount} B");
        }

        // The unit is chosen for the number as printed: just under a megabyte would print as 1024.0 KB.
        double kilobytes = Math.Round(
            (double)byteCount / BytesPerKilobyte,
            SizeDecimals,
            MidpointRounding.AwayFromZero);

        return kilobytes < BytesPerKilobyte
            ? string.Create(CultureInfo.InvariantCulture, $"{kilobytes:F1} KB")
            : string.Create(CultureInfo.InvariantCulture, $"{(double)byteCount / BytesPerMegabyte:F1} MB");
    }

    private static string HeaderLine(string name, string value) => $"{name}{ConsoleText.HeaderSeparator}{value}";

    private static void WriteHeaders(IAnsiConsole target, ReceivedResponse response)
    {
        foreach (KeyValuePair<string, string> header in response.Headers)
        {
            ConsoleText.WriteHeader(target, HeaderLine(header.Key, header.Value), s_headerNameStyle);
        }
    }

    private void RenderFull(HttpExchange exchange)
    {
        IAnsiConsole output = console.Output;
        ResolvedRequest request = exchange.Request;
        ReceivedResponse response = exchange.Response;

        WriteMaskedLines(output, $"{request.Method} {request.DisplayUrl}", s_requestLineStyle);
        foreach (KeyValuePair<string, string> header in request.Headers)
        {
            // The line is masked as a whole before the name is styled, so a secret that spans the name and the
            // value is hidden, and the styling can never split one.
            ConsoleText.WriteHeader(
                output,
                secretMask.Apply(HeaderLine(header.Key, RequestHeaderDisplayValue(header))),
                s_headerNameStyle);
        }

        ConsoleText.WriteLines(output, string.Empty);
        ConsoleText.WriteLines(output, StatusLine(response), StatusStyle(response));
        WriteHeaders(output, response);
        ConsoleText.WriteLines(output, string.Empty);

        string body = WithoutFinalLineBreak(BodyDisplayText(response));
        if (body.Length > 0)
        {
            ConsoleText.WriteLines(output, body);
        }
    }

    // The text is masked as a whole and only then split into lines, so a secret that spans lines is hidden too.
    // Control characters are made visible after masking: a secret that holds one still matches as it was
    // registered, and text inserted after the match cannot become part of one.
    private void WriteMaskedLines(IAnsiConsole target, string text, Style? style = null) =>
        ConsoleText.WriteLines(target, secretMask.Apply(text), style);
}
