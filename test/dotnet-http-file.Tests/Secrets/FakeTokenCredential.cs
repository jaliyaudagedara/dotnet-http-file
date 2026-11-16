using Azure.Core;

namespace DotNetHttpFile.Tests.Secrets;

internal sealed class FakeTokenCredential(string token = FakeTokenCredential.DefaultToken, Exception? failure = null)
    : TokenCredential
{
    public const string DefaultToken = "token-1";

    public DateTimeOffset ExpiresOn { get; init; } = DateTimeOffset.MaxValue;

    /// <summary>
    /// The credential type to report as DefaultAzureCredential does when it hands out a token, or null for none.
    /// </summary>
    public string? SelectedCredential { get; init; }

    public TokenRequestContext? RequestContext { get; private set; }

    public CancellationToken CancellationToken { get; private set; }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        RequestContext = requestContext;
        CancellationToken = cancellationToken;

        if (failure is not null)
        {
            throw failure;
        }

        if (SelectedCredential is not null)
        {
            AzureIdentityEvents.RaiseCredentialSelected(SelectedCredential);
        }

        return new AccessToken(token, ExpiresOn);
    }

    public override ValueTask<AccessToken> GetTokenAsync(
        TokenRequestContext requestContext,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(GetToken(requestContext, cancellationToken));
}
