using System.Diagnostics.CodeAnalysis;
using DotNetHttpFile.Execution;

namespace DotNetHttpFile.Resolution;

/// <summary>The exchanges of the named requests sent so far in a run, keyed by request name.</summary>
internal sealed class ExchangeStore
{
    private readonly Dictionary<string, HttpExchange> _exchanges = new(StringComparer.Ordinal);

    /// <summary>Records the exchange of the request named <paramref name="requestName"/>.</summary>
    /// <exception cref="InvalidOperationException">An exchange is already stored under that name.</exception>
    public void Add(string requestName, HttpExchange exchange)
    {
        ArgumentNullException.ThrowIfNull(requestName);
        ArgumentNullException.ThrowIfNull(exchange);

        // Each named request is sent once per run, so a second exchange is a bug in the caller, not a user error.
        if (!_exchanges.TryAdd(requestName, exchange))
        {
            throw new InvalidOperationException($"An exchange for request '{requestName}' is already stored.");
        }
    }

    /// <summary>Gets the exchange of the request named <paramref name="requestName"/>, matched by exact case.</summary>
    public bool TryGet(string requestName, [NotNullWhen(true)] out HttpExchange? exchange)
    {
        ArgumentNullException.ThrowIfNull(requestName);

        return _exchanges.TryGetValue(requestName, out exchange);
    }
}
