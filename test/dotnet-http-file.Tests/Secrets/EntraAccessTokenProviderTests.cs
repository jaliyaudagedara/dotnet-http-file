using Azure.Core;
using Azure.Identity;
using DotNetHttpFile.Secrets;

namespace DotNetHttpFile.Tests.Secrets;

public sealed class EntraAccessTokenProviderTests
{
    private const string VariableName = "ApiToken";
    private const string Scope = "api://myapi/.default";
    private const string TenantId = "11111111-2222-3333-4444-555555555555";
    private const string CredentialMessage = "DefaultAzureCredential failed to retrieve a token.";

    private const string DefinitionWithTenant =
        $$"""{ "provider": "EntraAccessToken", "scope": "{{Scope}}", "tenantId": "{{TenantId}}" }""";

    private const string DefinitionWithoutTenant =
        $$"""{ "provider": "EntraAccessToken", "scope": "{{Scope}}" }""";

    [Fact]
    public async Task ResolveAsync_EntraAccessToken_PassesScopeAndTenant()
    {
        FakeTokenCredential credential = new();

        string value = await ResolveAsync(credential, DefinitionWithTenant);

        TokenRequestContext requestContext = Assert.NotNull(credential.RequestContext);
        Assert.Equal(
            (FakeTokenCredential.DefaultToken, Scope, TenantId, TestContext.Current.CancellationToken),
            (value, Assert.Single(requestContext.Scopes), requestContext.TenantId, credential.CancellationToken));
    }

    [Fact]
    public async Task ResolveAsync_EntraAccessTokenWithoutTenant_RequestsNoTenant()
    {
        FakeTokenCredential credential = new();

        await ResolveAsync(credential, DefinitionWithoutTenant);

        TokenRequestContext requestContext = Assert.NotNull(credential.RequestContext);
        Assert.Null(requestContext.TenantId);
    }

    [Fact]
    public async Task ResolveAsync_EntraAccessTokenUnavailable_ThrowsWithFixLine()
    {
        FakeTokenCredential credential = new(failure: new CredentialUnavailableException(CredentialMessage));

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => ResolveAsync(credential, DefinitionWithTenant));

        string expected = string.Join(
            Environment.NewLine,
            "ApiToken (EntraAccessToken): could not get an access token.",
            "  scope:  api://myapi/.default",
            "  tenant: 11111111-2222-3333-4444-555555555555",
            "  tried:  DefaultAzureCredential failed to retrieve a token.",
            "  fix:    az login --tenant 11111111-2222-3333-4444-555555555555");
        Assert.Equal(expected, exception.Message);
    }

    [Fact]
    public async Task ResolveAsync_EntraAccessTokenUnavailableWithoutTenant_ThrowsWithDefaultTenant()
    {
        FakeTokenCredential credential = new(failure: new CredentialUnavailableException(CredentialMessage));

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => ResolveAsync(credential, DefinitionWithoutTenant));

        string expected = string.Join(
            Environment.NewLine,
            "ApiToken (EntraAccessToken): could not get an access token.",
            "  scope:  api://myapi/.default",
            "  tenant: default",
            "  tried:  DefaultAzureCredential failed to retrieve a token.",
            "  fix:    az login");
        Assert.Equal(expected, exception.Message);
    }

    [Fact]
    public async Task ResolveAsync_EntraAccessTokenAuthenticationFailed_ThrowsWithOriginalAsInner()
    {
        AuthenticationFailedException failure = new(CredentialMessage);
        FakeTokenCredential credential = new(failure: failure);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => ResolveAsync(credential, DefinitionWithTenant));

        Assert.Same(failure, exception.InnerException);
    }

    [Fact]
    public async Task ResolveAsync_EntraAccessTokenOtherFailure_Propagates()
    {
        InvalidOperationException failure = new("unexpected");
        FakeTokenCredential credential = new(failure: failure);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ResolveAsync(credential, DefinitionWithTenant));

        Assert.Same(failure, exception);
    }

    [Theory]
    [InlineData("""{ "provider": "EntraAccessToken" }""")]
    [InlineData("""{ "provider": "EntraAccessToken", "scope": "" }""")]
    [InlineData("""{ "provider": "EntraAccessToken", "scope": 5 }""")]
    [InlineData("""{ "provider": "EntraAccessToken", "scope": null }""")]
    [InlineData("""{ "provider": "EntraAccessToken", "scope": ["api://myapi/.default"] }""")]
    public async Task ResolveAsync_EntraAccessTokenMissingScope_Throws(string definitionJson)
    {
        FakeTokenCredential credential = new();

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => ResolveAsync(credential, definitionJson));

        Assert.Equal("Variable 'ApiToken' (EntraAccessToken) is missing 'scope'.", exception.Message);
    }

    [Theory]
    [InlineData("""{ "provider": "EntraAccessToken", "scope": "api://myapi/.default", "tenantId": 5 }""")]
    [InlineData("""{ "provider": "EntraAccessToken", "scope": "api://myapi/.default", "tenantId": "" }""")]
    public async Task ResolveAsync_EntraAccessTokenTenantNotAString_Throws(string definitionJson)
    {
        FakeTokenCredential credential = new();

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => ResolveAsync(credential, definitionJson));

        Assert.Equal(
            "Variable 'ApiToken' (EntraAccessToken) has an invalid 'tenantId'. Expected a non-empty string.",
            exception.Message);
    }

    [Theory]
    [InlineData("""{ "provider": "EntraAccessToken", "scope": "api://myapi/.default", "tenantId": "x; y" }""")]
    [InlineData("""{ "provider": "EntraAccessToken", "scope": "api://myapi/.default", "tenantId": "a\nb" }""")]
    [InlineData("""{ "provider": "EntraAccessToken", "scope": "api://myapi/.default", "tenantId": "contoso.com\n" }""")]
    [InlineData("""{ "provider": "EntraAccessToken", "scope": "api://myapi/.default", "tenantId": "\u001b[31mred" }""")]
    [InlineData("""{ "provider": "EntraAccessToken", "scope": "api://myapi/.default", "tenantId": "-contoso.com" }""")]
    public async Task ResolveAsync_EntraAccessTokenInvalidTenant_Throws(string definitionJson)
    {
        FakeTokenCredential credential = new();

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => ResolveAsync(credential, definitionJson));

        Assert.Equal(
            ("Variable 'ApiToken' (EntraAccessToken) has an invalid 'tenantId'. Expected a tenant ID (GUID) or a "
                + "domain name.",
                null),
            (exception.Message, credential.RequestContext));
    }

    [Theory]
    [InlineData("""{ "provider": "EntraAccessToken", "scope": "api://myapi/.default offline_access" }""")]
    [InlineData("""{ "provider": "EntraAccessToken", "scope": "api://myapi/.default; y" }""")]
    [InlineData("""{ "provider": "EntraAccessToken", "scope": "api://myapi/.default\n" }""")]
    [InlineData("""{ "provider": "EntraAccessToken", "scope": "\u001b[31mred" }""")]
    public async Task ResolveAsync_EntraAccessTokenMalformedScope_ThrowsWithoutRequestingToken(string definitionJson)
    {
        FakeTokenCredential credential = new();

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => ResolveAsync(credential, definitionJson));

        Assert.Equal(
            ("Variable 'ApiToken' (EntraAccessToken) has an invalid 'scope'. Expected a single scope made of "
                + "letters, digits and . - : _ / (for example api://my-api/.default).",
                null),
            (exception.Message, credential.RequestContext));
    }

    [Theory]
    [InlineData("contoso.onmicrosoft.com")]
    [InlineData("11111111-2222-3333-4444-555555555555")]
    public async Task ResolveAsync_EntraAccessTokenTenantDomainOrId_RequestsThatTenant(string tenantId)
    {
        FakeTokenCredential credential = new();

        await ResolveAsync(
            credential,
            $$"""{ "provider": "EntraAccessToken", "scope": "{{Scope}}", "tenantId": "{{tenantId}}" }""");

        TokenRequestContext requestContext = Assert.NotNull(credential.RequestContext);
        Assert.Equal(tenantId, requestContext.TenantId);
    }

    private static Task<string> ResolveAsync(FakeTokenCredential credential, string definitionJson)
    {
        EntraAccessTokenProvider provider = new(new Lazy<TokenCredential>(credential));

        return provider.ResolveAsync(
            SecretRequestFactory.Create(VariableName, definitionJson),
            TestContext.Current.CancellationToken);
    }
}
