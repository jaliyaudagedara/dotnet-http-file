using System.Text.Json.Nodes;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using DotNetHttpFile.Secrets;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace DotNetHttpFile.Tests.Secrets;

public sealed class AzureKeyVaultProviderTests
{
    private const string VariableName = "AKVSecret";
    private const string SecretName = "SecretInKeyVault";
    private const string SecretValue = "value-from-vault";
    private const string ServiceMessage = "service response text";
    private const string VaultsPrefix = "/vaults/";

    private const string UnreachableMessage =
        "Variable 'AKVSecret' (AzureKeyVault): could not reach vault 'my-key-vault-01182024' to read secret "
        + "'SecretInKeyVault'. Check the vault name in 'resourceId' and your network connection.";

    private const string ResourceId =
        "/subscriptions/3a914c59-8175a9e0e540/resourceGroups/my-key-vault-rg/providers/Microsoft.KeyVault"
        + "/vaults/my-key-vault-01182024";

    private static readonly string s_signInFailedMessage = string.Join(
        Environment.NewLine,
        "AKVSecret (AzureKeyVault): could not sign in to read secret 'SecretInKeyVault' from vault "
            + "'my-key-vault-01182024'.",
        "  tried:  service response text",
        "  fix:    az login");

    private readonly SecretClient _client = Substitute.For<SecretClient>();
    private readonly FakeTokenCredential _credential = new();
    private Uri? _vaultUri;
    private TokenCredential? _clientCredential;

    [Fact]
    public async Task ResolveAsync_KeyVault_UsesVaultUriFromResourceId()
    {
        ReturnSecret();

        string value = await ResolveAsync(Definition(ResourceId));

        Assert.Equal(
            (SecretValue, new Uri("https://my-key-vault-01182024.vault.azure.net/"), _credential),
            (value, _vaultUri, _clientCredential));
    }

    [Fact]
    public async Task ResolveAsync_KeyVault_PassesCancellationToken()
    {
        ReturnSecret();

        await ResolveAsync(Definition(ResourceId));

        await _client.Received(1).GetSecretAsync(SecretName, null, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ResolveAsync_KeyVaultSecretNotFound_ThrowsNamingVariableVaultAndSecret()
    {
        string message = await ResolveFailureMessageAsync(new RequestFailedException(404, ServiceMessage));

        Assert.Equal(
            "Variable 'AKVSecret' (AzureKeyVault): secret 'SecretInKeyVault' was not found in vault "
                + "'my-key-vault-01182024' (404).",
            message);
    }

    [Fact]
    public async Task ResolveAsync_KeyVaultForbidden_ThrowsNamingVariableVaultAndSecret()
    {
        string message = await ResolveFailureMessageAsync(new RequestFailedException(403, ServiceMessage));

        Assert.Equal(
            "Variable 'AKVSecret' (AzureKeyVault): access to secret 'SecretInKeyVault' in vault "
                + "'my-key-vault-01182024' was forbidden (403). Check that your account can read secrets in "
                + "this vault.",
            message);
    }

    [Fact]
    public async Task ResolveAsync_KeyVaultOtherStatus_ThrowsWithStatus()
    {
        string message = await ResolveFailureMessageAsync(new RequestFailedException(500, ServiceMessage));

        Assert.Equal(
            "Variable 'AKVSecret' (AzureKeyVault): could not read secret 'SecretInKeyVault' from vault "
                + "'my-key-vault-01182024' (HTTP 500).",
            message);
    }

    [Fact]
    public async Task ResolveAsync_KeyVaultNoResponse_ThrowsUnreachable()
    {
        string message = await ResolveFailureMessageAsync(new RequestFailedException(ServiceMessage));

        Assert.Equal(
            "Variable 'AKVSecret' (AzureKeyVault): could not reach vault 'my-key-vault-01182024' to read secret "
                + "'SecretInKeyVault'. Check the vault name in 'resourceId' and your network connection.",
            message);
    }

    // Azure.Core's RetryPolicy throws this shape once more than one attempt has failed.
    [Fact]
    public async Task ResolveAsync_KeyVaultRetriesExhausted_ThrowsUnreachable()
    {
        AggregateException failure = new(
            "Retry failed after 4 tries.",
            new RequestFailedException(ServiceMessage),
            new RequestFailedException(ServiceMessage));
        _client.GetSecretAsync(SecretName, null, Arg.Any<CancellationToken>()).ThrowsAsync(failure);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => ResolveAsync(Definition(ResourceId)));

        Assert.Equal(
            ("Variable 'AKVSecret' (AzureKeyVault): could not reach vault 'my-key-vault-01182024' to read secret "
                + "'SecretInKeyVault'. Check the vault name in 'resourceId' and your network connection.",
                failure),
            (exception.Message, exception.InnerException));
    }

    [Fact]
    public async Task ResolveAsync_KeyVaultRetriesExhaustedWithStatus_UsesLastStatus()
    {
        AggregateException failure = new(
            "Retry failed after 2 tries.",
            new RequestFailedException(ServiceMessage),
            new RequestFailedException(500, ServiceMessage));

        string message = await ResolveFailureMessageAsync(failure);

        Assert.Equal(
            "Variable 'AKVSecret' (AzureKeyVault): could not read secret 'SecretInKeyVault' from vault "
                + "'my-key-vault-01182024' (HTTP 500).",
            message);
    }

    // A per-try timeout reaches the retry policy as a cancellation that the caller did not ask for.
    [Fact]
    public async Task ResolveAsync_KeyVaultEveryTryTimedOut_ThrowsUnreachable()
    {
        AggregateException failure = new(
            "Retry failed after 2 tries.",
            new TaskCanceledException(ServiceMessage),
            new TaskCanceledException(ServiceMessage));
        _client.GetSecretAsync(SecretName, null, Arg.Any<CancellationToken>()).ThrowsAsync(failure);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => ResolveAsync(Definition(ResourceId)));

        Assert.Equal((UnreachableMessage, failure), (exception.Message, exception.InnerException));
    }

    [Fact]
    public async Task ResolveAsync_KeyVaultRequestAndConnectionFailures_ThrowsUnreachable()
    {
        AggregateException failure = new(
            "Retry failed after 2 tries.",
            new RequestFailedException(ServiceMessage),
            new IOException(ServiceMessage));

        string message = await ResolveFailureMessageAsync(failure);

        Assert.Equal(UnreachableMessage, message);
    }

    [Fact]
    public async Task ResolveAsync_KeyVaultCancelledDuringRetries_ThrowsOperationCanceled()
    {
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        AggregateException failure = new(
            "Retry failed after 2 tries.",
            new RequestFailedException(ServiceMessage),
            new TaskCanceledException(ServiceMessage));
        _client
            .GetSecretAsync(SecretName, null, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cancellation.Cancel();

                return Task.FromException<Response<KeyVaultSecret>>(failure);
            });

        OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => ResolveWithTokenAsync(Definition(ResourceId), cancellation.Token));

        Assert.Equal((cancellation.Token, failure), (exception.CancellationToken, exception.InnerException));
    }

    [Fact]
    public async Task ResolveAsync_KeyVaultAggregateOfOtherFailures_Propagates()
    {
        AggregateException failure = new(
            "Retry failed after 2 tries.",
            new RequestFailedException(ServiceMessage),
            new InvalidOperationException("unexpected"));
        _client.GetSecretAsync(SecretName, null, Arg.Any<CancellationToken>()).ThrowsAsync(failure);

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(
            () => ResolveAsync(Definition(ResourceId)));

        Assert.Same(failure, exception);
    }

    [Fact]
    public async Task ResolveAsync_KeyVaultCredentialUnavailable_ThrowsSignInFailure()
    {
        string message = await ResolveFailureMessageAsync(new CredentialUnavailableException(ServiceMessage));

        Assert.Equal(s_signInFailedMessage, message);
    }

    [Fact]
    public async Task ResolveAsync_KeyVaultAuthenticationFailed_ThrowsSignInFailure()
    {
        string message = await ResolveFailureMessageAsync(new AuthenticationFailedException(ServiceMessage));

        Assert.Equal(s_signInFailedMessage, message);
    }

    [Fact]
    public async Task ResolveAsync_KeyVaultFailure_KeepsOriginalAsInner()
    {
        RequestFailedException failure = new(404, ServiceMessage);
        _client.GetSecretAsync(SecretName, null, Arg.Any<CancellationToken>()).ThrowsAsync(failure);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => ResolveAsync(Definition(ResourceId)));

        Assert.Same(failure, exception.InnerException);
    }

    [Fact]
    public async Task ResolveAsync_KeyVaultOtherFailure_Propagates()
    {
        InvalidOperationException failure = new("unexpected");
        _client.GetSecretAsync(SecretName, null, Arg.Any<CancellationToken>()).ThrowsAsync(failure);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ResolveAsync(Definition(ResourceId)));

        Assert.Same(failure, exception);
    }

    [Theory]
    [InlineData("""{ "secretName": "SecretInKeyVault" }""", "resourceId")]
    [InlineData("""{ "secretName": "SecretInKeyVault", "resourceId": "" }""", "resourceId")]
    [InlineData("""{ "secretName": "SecretInKeyVault", "resourceId": 7 }""", "resourceId")]
    [InlineData("""{ "resourceId": "/vaults/my-vault" }""", "secretName")]
    [InlineData("""{ "resourceId": "/vaults/my-vault", "secretName": "" }""", "secretName")]
    [InlineData("""{ "resourceId": "/vaults/my-vault", "secretName": true }""", "secretName")]
    public async Task ResolveAsync_KeyVaultMissingField_Throws(string definitionJson, string fieldName)
    {
        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => ResolveAsync(definitionJson));

        Assert.Equal($"Variable 'AKVSecret' (AzureKeyVault) is missing '{fieldName}'.", exception.Message);
    }

    [Theory]
    [InlineData("/subscriptions/1/resourceGroups/rg/providers/Microsoft.KeyVault/vaults/")]
    [InlineData("/subscriptions/1/resourceGroups/rg/providers/Microsoft.KeyVault/vaults/my-vault/")]
    [InlineData("/vaults/ab")]
    [InlineData("/vaults/abcdefghijklmnopqrstuvwxy")]
    [InlineData("/vaults/1vault")]
    [InlineData("/vaults/-vault")]
    [InlineData("/vaults/vault-")]
    [InlineData("/vaults/my--vault")]
    [InlineData("/vaults/my_vault")]
    [InlineData("/vaults/evil.example.com")]
    [InlineData("/vaults/evil.example.com#")]
    [InlineData("/vaults/my vault")]
    [InlineData("/vaults/my-vault\n")]
    public async Task ResolveAsync_KeyVaultInvalidVaultName_ThrowsWithoutCreatingClient(string resourceId)
    {
        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => ResolveAsync(Definition(resourceId)));

        Assert.Equal(
            ("Variable 'AKVSecret' (AzureKeyVault): 'resourceId' does not end with a valid vault name.", null),
            (exception.Message, _vaultUri));
    }

    [Theory]
    [InlineData("/vaults/abc")]
    [InlineData("/vaults/abcdefghijklmnopqrstuvwx")]
    [InlineData("/vaults/My-Vault-01")]
    public async Task ResolveAsync_KeyVaultValidVaultName_BuildsVaultUri(string resourceId)
    {
        ReturnSecret();
        string vaultName = resourceId[VaultsPrefix.Length..];

        await ResolveAsync(Definition(resourceId));

        Assert.Equal(new Uri($"https://{vaultName}.vault.azure.net/"), _vaultUri);
    }

    private static string Definition(string resourceId)
    {
        JsonObject definition = new()
        {
            ["provider"] = AzureKeyVaultProvider.ProviderName,
            ["secretName"] = SecretName,
            ["resourceId"] = resourceId,
        };

        return definition.ToJsonString();
    }

    private void ReturnSecret()
    {
        KeyVaultSecret secret = SecretModelFactory.KeyVaultSecret(new SecretProperties(SecretName), SecretValue);
        _client
            .GetSecretAsync(SecretName, null, Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(secret, Substitute.For<Response>()));
    }

    private async Task<string> ResolveFailureMessageAsync(Exception failure)
    {
        _client.GetSecretAsync(SecretName, null, Arg.Any<CancellationToken>()).ThrowsAsync(failure);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => ResolveAsync(Definition(ResourceId)));

        return exception.Message;
    }

    private Task<string> ResolveAsync(string definitionJson) =>
        ResolveWithTokenAsync(definitionJson, TestContext.Current.CancellationToken);

    private Task<string> ResolveWithTokenAsync(string definitionJson, CancellationToken cancellationToken)
    {
        AzureKeyVaultProvider provider = new(
            new Lazy<TokenCredential>(_credential),
            (vaultUri, credential) =>
            {
                _vaultUri = vaultUri;
                _clientCredential = credential;

                return _client;
            });

        return provider.ResolveAsync(
            SecretRequestFactory.Create(VariableName, definitionJson),
            cancellationToken);
    }
}
