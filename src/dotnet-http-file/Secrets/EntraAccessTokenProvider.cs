using System.Text;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;

namespace DotNetHttpFile.Secrets;

internal sealed partial class EntraAccessTokenProvider(Lazy<TokenCredential> credential) : ISecretProvider
{
    public const string ProviderName = "EntraAccessToken";

    private const string ScopeField = "scope";
    private const string TenantIdField = "tenantId";
    private const string LoginCommand = "az login";
    private const string LoginToTenantCommand = "az login --tenant ";

    private static readonly CompositeFormat s_entraAccessTokenFailedFormat =
        CompositeFormat.Parse(Resources.EntraAccessTokenFailed);

    private static readonly CompositeFormat s_entraAccessTokenInvalidScopeFormat =
        CompositeFormat.Parse(Resources.EntraAccessTokenInvalidScope);

    private static readonly CompositeFormat s_entraAccessTokenInvalidTenantIdFormat =
        CompositeFormat.Parse(Resources.EntraAccessTokenInvalidTenantId);

    public string Name => ProviderName;

    public async Task<string> ResolveAsync(SecretRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string scope = request.GetRequiredString(ProviderName, ScopeField);
        if (!ScopePattern.IsMatch(scope))
        {
            throw new HttpFileException(Messages.Format(s_entraAccessTokenInvalidScopeFormat, request.VariableName));
        }

        string? tenantId = request.GetOptionalString(ProviderName, TenantIdField);
        if (tenantId is not null && !TenantIdPattern.IsMatch(tenantId))
        {
            throw new HttpFileException(Messages.Format(s_entraAccessTokenInvalidTenantIdFormat, request.VariableName));
        }

        TokenRequestContext requestContext = new([scope], tenantId: tenantId);

        try
        {
            AccessToken accessToken = await credential.Value
                .GetTokenAsync(requestContext, cancellationToken)
                .ConfigureAwait(false);

            return accessToken.Token;
        }
        catch (AuthenticationFailedException exception)
        {
            // CredentialUnavailableException derives from AuthenticationFailedException, so this covers both.
            throw new HttpFileException(
                Messages.Format(
                    s_entraAccessTokenFailedFormat,
                    request.VariableName,
                    scope,
                    tenantId ?? Resources.EntraAccessTokenDefaultTenant,
                    exception.Message,
                    tenantId is null ? LoginCommand : LoginToTenantCommand + tenantId,
                    Environment.NewLine),
                exception);
        }
    }

    // Azure.Core's scope check for the developer-tool credentials allows only these characters, and the scope
    // is printed in the failure message, so it is checked first.
    [GeneratedRegex(@"^[0-9A-Za-z_.:/-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex ScopePattern { get; }

    // A tenant ID or a domain name. The value is printed in the suggested "az login --tenant" command, so
    // nothing that a shell or a terminal would act on may pass.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex TenantIdPattern { get; }
}
