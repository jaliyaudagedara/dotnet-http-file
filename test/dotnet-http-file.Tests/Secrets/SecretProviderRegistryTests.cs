using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using DotNetHttpFile.Secrets;

namespace DotNetHttpFile.Tests.Secrets;

public sealed class SecretProviderRegistryTests
{
    private const string VariableName = "ApiToken";

    private readonly Lazy<TokenCredential> _credential = new(
        () => throw new InvalidOperationException("The credential must not be created."));

    [Theory]
    [InlineData("AspnetUserSecrets", typeof(AspnetUserSecretsProvider))]
    [InlineData("AzureKeyVault", typeof(AzureKeyVaultProvider))]
    [InlineData("Encrypted", typeof(EncryptedProvider))]
    [InlineData("EntraAccessToken", typeof(EntraAccessTokenProvider))]
    public void Get_KnownProvider_ReturnsIt(string providerName, Type expectedType)
    {
        SecretProviderRegistry registry = CreateRegistry();

        ISecretProvider provider = registry.Get(providerName, VariableName);

        Assert.Equal((expectedType, providerName), (provider.GetType(), provider.Name));
    }

    [Theory]
    [InlineData("Nope")]
    [InlineData("entraaccesstoken")]
    [InlineData("EntraAccessToken ")]
    [InlineData("")]
    public void Get_UnknownProvider_ThrowsWithSupportedList(string providerName)
    {
        SecretProviderRegistry registry = CreateRegistry();

        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => registry.Get(providerName, VariableName));

        Assert.Equal(
            $"Variable 'ApiToken' uses unknown provider '{providerName}'. "
                + "Supported: AspnetUserSecrets, AzureKeyVault, Encrypted, EntraAccessToken.",
            exception.Message);
    }

    [Fact]
    public void Get_ProvidersConstructedAndLookedUp_DoesNotCreateCredential()
    {
        SecretProviderRegistry registry = CreateRegistry();

        registry.Get(EntraAccessTokenProvider.ProviderName, VariableName);
        registry.Get(AzureKeyVaultProvider.ProviderName, VariableName);

        Assert.False(_credential.IsValueCreated);
    }

    [Theory]
    [InlineData("AzureKeyVault", """{ "provider": "AzureKeyVault", "secretName": "s", "resourceId": "/vaults/a.b" }""")]
    [InlineData("EntraAccessToken", """{ "provider": "EntraAccessToken" }""")]
    public async Task ResolveAsync_InvalidDefinition_DoesNotCreateCredential(string providerName, string definitionJson)
    {
        ISecretProvider provider = CreateRegistry().Get(providerName, VariableName);

        await Assert.ThrowsAsync<HttpFileException>(
            () => provider.ResolveAsync(
                SecretRequestFactory.Create(VariableName, definitionJson),
                TestContext.Current.CancellationToken));

        Assert.False(_credential.IsValueCreated);
    }

    // Registered out of order so the supported list has to be sorted by the registry.
    private SecretProviderRegistry CreateRegistry() =>
        new(
        [
            new EncryptedProvider(),
            new AzureKeyVaultProvider(_credential, (vaultUri, credential) => new SecretClient(vaultUri, credential)),
            new EntraAccessTokenProvider(_credential),
            new AspnetUserSecretsProvider(string.Empty),
        ]);
}
