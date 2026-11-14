using Azure.Core;
using Azure.Identity;

namespace DotNetHttpFile.Secrets;

internal static class AzureCredentialFactory
{
    private const string AnyTenant = "*";

    /// <summary>Creates the credential that the Azure providers share.</summary>
    public static TokenCredential Create() => new DefaultAzureCredential(CreateOptions());

    /// <summary>Creates the options that <see cref="Create"/> builds the credential from.</summary>
    public static DefaultAzureCredentialOptions CreateOptions()
    {
        // Without this the credential refuses a token request for a tenant other than its own,
        // which is what a variable's tenantId asks for.
        DefaultAzureCredentialOptions options = new();
        options.AdditionallyAllowedTenants.Add(AnyTenant);

        return options;
    }
}
