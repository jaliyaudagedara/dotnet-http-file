using System.Diagnostics.Tracing;
using System.Text;
using Azure.Core.Diagnostics;
using DotNetHttpFile.Output;

namespace DotNetHttpFile.Secrets;

/// <summary>
/// Logs which credential of the shared DefaultAzureCredential answered a token request.
/// </summary>
internal static class AzureCredentialLog
{
    private const string IdentityEventSourceName = "Azure-Identity";
    private const string CredentialSelectedEventName = "DefaultAzureCredentialCredentialSelected";
    private const char NamespaceSeparator = '.';

    private static readonly CompositeFormat s_verboseCredentialSelectedFormat =
        CompositeFormat.Parse(Resources.VerboseCredentialSelected);

    /// <summary>
    /// Starts writing the credential that DefaultAzureCredential selects to <paramref name="log"/>. Dispose the
    /// result as soon as the call that can get a token returns.
    /// </summary>
    /// <returns>
    /// The listener, or null when <paramref name="log"/> is null or not enabled.
    /// </returns>
    /// <remarks>
    /// DefaultAzureCredential reports the credential only through its event source, and only on the first token
    /// it gets, so every provider that shares the credential listens. The listener is process-wide, so it must
    /// not outlive the call.
    /// </remarks>
    public static IDisposable? Listen(DiagnosticLog? log)
    {
        if (log is not { IsEnabled: true })
        {
            return null;
        }

        return new AzureEventSourceListener(
            (eventData, _) =>
            {
                string? credentialName = CredentialNameOf(
                    eventData.EventSource.Name,
                    eventData.EventName,
                    eventData.Payload);
                if (credentialName is not null)
                {
                    log.Write(Messages.Format(s_verboseCredentialSelectedFormat, credentialName));
                }
            },
            EventLevel.Informational);
    }

    /// <summary>
    /// The credential type, without its namespace, that a DefaultAzureCredential "credential selected" event
    /// names.
    /// </summary>
    /// <returns>
    /// The type name, or null for any other event or payload.
    /// </returns>
    /// <remarks>
    /// Only the credential type is taken: the other Azure.Identity events carry scopes, account details and
    /// exception text, which do not belong in the log.
    /// </remarks>
    internal static string? CredentialNameOf(string sourceName, string? eventName, IReadOnlyList<object?>? payload)
    {
        if (!string.Equals(sourceName, IdentityEventSourceName, StringComparison.Ordinal)
            || !string.Equals(eventName, CredentialSelectedEventName, StringComparison.Ordinal)
            || payload is not [string credentialType, ..])
        {
            return null;
        }

        return credentialType[(credentialType.LastIndexOf(NamespaceSeparator) + 1)..];
    }
}
