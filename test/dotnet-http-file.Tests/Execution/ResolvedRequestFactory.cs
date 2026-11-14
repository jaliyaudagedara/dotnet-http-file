using DotNetHttpFile.Execution;

namespace DotNetHttpFile.Tests.Execution;

internal static class ResolvedRequestFactory
{
    /// <summary>Creates a request whose display URL is the URL it is sent to.</summary>
    public static ResolvedRequest Create(
        string method,
        Uri url,
        string? body = null,
        string? httpVersion = null,
        params (string Name, string Value, int Line)[] headers) =>
        new(
            method,
            url,
            httpVersion,
            [.. headers.Select(header => KeyValuePair.Create(header.Name, header.Value))],
            body)
        {
            DisplayUrl = url.OriginalString,
            HeaderLines = [.. headers.Select(header => header.Line)],
        };
}
