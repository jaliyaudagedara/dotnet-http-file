using System.Reflection;
using Azure.Identity;

namespace DotNetHttpFile.Tests.Secrets;

/// <summary>
/// Raises the real Azure.Identity events that DefaultAzureCredential writes, for a fake credential to act like it.
/// </summary>
internal static class AzureIdentityEvents
{
    private const string EventSourceTypeName = "Azure.Identity.AzureIdentityEventSource";
    private const string SingletonPropertyName = "Singleton";
    private const string CredentialSelectedMethodName = "DefaultAzureCredentialCredentialSelected";

    /// <summary>
    /// Writes the event DefaultAzureCredential writes when <paramref name="credentialType"/> gets its first token.
    /// </summary>
    public static void RaiseCredentialSelected(string credentialType)
    {
        Type eventSourceType = typeof(DefaultAzureCredential).Assembly
            .GetType(EventSourceTypeName, throwOnError: true)!;
        object? eventSource = eventSourceType
            .GetProperty(SingletonPropertyName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(null);
        MethodInfo raise = eventSourceType.GetMethod(
            CredentialSelectedMethodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            [typeof(string)])!;

        raise.Invoke(eventSource, [credentialType]);
    }
}
