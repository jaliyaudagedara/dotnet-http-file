using Azure.Core;
using Azure.Identity;
using DotNetHttpFile.Secrets;

namespace DotNetHttpFile.Tests.Secrets;

public sealed class AzureCredentialFactoryTests
{
    [Fact]
    public void CreateOptions_Always_AllowsAnyTenant()
    {
        DefaultAzureCredentialOptions options = AzureCredentialFactory.CreateOptions();

        Assert.Equal(["*"], options.AdditionallyAllowedTenants);
    }

    // Only the type is checked: asking the credential for a token would reach the network.
    [Fact]
    public void Create_Always_ReturnsDefaultAzureCredential()
    {
        TokenCredential credential = AzureCredentialFactory.Create();

        Assert.IsType<DefaultAzureCredential>(credential);
    }
}
