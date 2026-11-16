using DotNetHttpFile.Output;
using DotNetHttpFile.Secrets;
using Spectre.Console.Testing;

namespace DotNetHttpFile.Tests.Secrets;

[Collection(AzureIdentityEventGroup.Name)]
public sealed class AzureCredentialLogTests
{
    [Fact]
    public void Listen_CredentialSelected_LogsTheTypeName()
    {
        using TestConsole error = new();

        using (AzureCredentialLog.Listen(new DiagnosticLog(error) { IsEnabled = true }))
        {
            AzureIdentityEvents.RaiseCredentialSelected("Azure.Identity.VisualStudioCredential");
        }

        Assert.Equal("verbose: Credential: VisualStudioCredential\n", Normalize(error.Output));
    }

    [Fact]
    public void Listen_Disposed_StopsLogging()
    {
        using TestConsole error = new();
        IDisposable? listener = AzureCredentialLog.Listen(new DiagnosticLog(error) { IsEnabled = true });
        listener?.Dispose();

        AzureIdentityEvents.RaiseCredentialSelected("Azure.Identity.VisualStudioCredential");

        Assert.Empty(error.Output);
    }

    [Fact]
    public void Listen_LogDisabledOrNull_ReturnsNull()
    {
        using TestConsole error = new();

        Assert.Equal(
            ((IDisposable?)null, (IDisposable?)null),
            (AzureCredentialLog.Listen(new DiagnosticLog(error)), AzureCredentialLog.Listen(null)));
    }

    [Theory]
    [InlineData("Azure.Identity.VisualStudioCredential", "VisualStudioCredential")]
    [InlineData("AzureCliCredential", "AzureCliCredential")]
    public void CredentialNameOf_CredentialSelectedEvent_ReturnsTheTypeName(string credentialType, string expected)
    {
        string? name = AzureCredentialLog.CredentialNameOf(
            "Azure-Identity",
            "DefaultAzureCredentialCredentialSelected",
            [credentialType]);

        Assert.Equal(expected, name);
    }

    [Theory]
    [InlineData("Azure-Identity", "GetTokenSucceeded")]
    [InlineData("Azure-Identity", null)]
    [InlineData("Azure-Core", "DefaultAzureCredentialCredentialSelected")]
    [InlineData("azure-identity", "DefaultAzureCredentialCredentialSelected")]
    public void CredentialNameOf_OtherEvent_ReturnsNull(string sourceName, string? eventName)
    {
        string? name = AzureCredentialLog.CredentialNameOf(
            sourceName,
            eventName,
            ["Azure.Identity.AzureCliCredential"]);

        Assert.Null(name);
    }

    [Fact]
    public void CredentialNameOf_NonStringPayload_ReturnsNull()
    {
        string? name = AzureCredentialLog.CredentialNameOf(
            "Azure-Identity",
            "DefaultAzureCredentialCredentialSelected",
            [13]);

        Assert.Null(name);
    }

    [Fact]
    public void CredentialNameOf_NoPayload_ReturnsNull()
    {
        string? emptyName = AzureCredentialLog.CredentialNameOf(
            "Azure-Identity",
            "DefaultAzureCredentialCredentialSelected",
            []);
        string? nullName = AzureCredentialLog.CredentialNameOf(
            "Azure-Identity",
            "DefaultAzureCredentialCredentialSelected",
            null);

        Assert.Equal((null, null), (emptyName, nullName));
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
