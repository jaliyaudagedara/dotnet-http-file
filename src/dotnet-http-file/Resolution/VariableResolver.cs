using System.Diagnostics;
using System.Text;
using DotNetHttpFile.Environments;
using DotNetHttpFile.Execution;
using DotNetHttpFile.Parsing;
using DotNetHttpFile.Secrets;
using DotNetHttpFile.Templates;

namespace DotNetHttpFile.Resolution;

/// <summary>
/// Substitutes the variables and references of one run. Not thread-safe.
/// </summary>
internal sealed class VariableResolver(
    HttpFile file,
    EnvironmentSet environment,
    SecretProviderRegistry providers,
    DynamicVariables dynamicVariables,
    ExchangeStore exchanges,
    SecretMask secretMask,
    IReadOnlyDictionary<string, string>? overrides = null)
{
    private static readonly CompositeFormat s_requestNotSentFormat =
        CompositeFormat.Parse(Resources.RequestNotSent);

    private static readonly CompositeFormat s_urlNotAbsoluteFormat =
        CompositeFormat.Parse(Resources.UrlNotAbsolute);

    private static readonly CompositeFormat s_variableNotDefinedFormat =
        CompositeFormat.Parse(Resources.VariableNotDefined);

    private static readonly CompositeFormat s_variableNotDefinedNoEnvironmentFormat =
        CompositeFormat.Parse(Resources.VariableNotDefinedNoEnvironment);

    // Keyed by definition, not by name: a name can be defined again further down the file.
    private readonly Dictionary<FileVariable, FileVariableValue> _fileVariableValues =
        [with(ReferenceEqualityComparer.Instance)];

    private readonly Dictionary<string, string> _providerValues = [with(StringComparer.Ordinal)];

    private bool _usesEntraAccessToken;

    /// <summary>
    /// Resolves the URL, headers and body of <paramref name="request"/>.
    /// </summary>
    /// <exception cref="HttpFileException">
    /// A variable is not defined, a value could not be read, or the URL is not an absolute http or https URL.
    /// </exception>
    public async Task<ResolvedRequest> ResolveRequestAsync(
        RequestDefinition request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _usesEntraAccessToken = false;
        string urlText = await ResolveAsync(request.Url, request.Line, cancellationToken).ConfigureAwait(false);

        // Masked from the resolved text, not from the Uri, whose escaping would hide a secret from the mask.
        string displayUrl = secretMask.Apply(urlText);
        if (!Uri.TryCreate(urlText, UriKind.Absolute, out Uri? url)
            || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            throw new HttpFileException(Messages.Format(s_urlNotAbsoluteFormat, displayUrl, request.Line));
        }

        List<KeyValuePair<string, string>> headers = [with(capacity: request.Headers.Count)];
        foreach (HeaderDefinition header in request.Headers)
        {
            string value = await ResolveAsync(header.Value, header.Line, cancellationToken).ConfigureAwait(false);
            headers.Add(KeyValuePair.Create(header.Name, value));
        }

        string? body = request.Body is null
            ? null
            : await ResolveAsync(request.Body, request.BodyLine ?? request.Line, cancellationToken)
                .ConfigureAwait(false);

        return new ResolvedRequest(request.Method, url, headers)
        {
            DisplayUrl = displayUrl,
            HttpVersion = request.HttpVersion,
            Body = body,
            HeaderLines = [.. request.Headers.Select(header => header.Line)],
            UsesEntraAccessToken = _usesEntraAccessToken,
        };
    }

    /// <summary>
    /// Resolves <paramref name="template"/>, whose text starts on <paramref name="line"/>.
    /// </summary>
    /// <remarks>
    /// A name is the <c>--var</c> override, else the file variable defined above <paramref name="line"/>, else
    /// the environment value. A file variable and a provider value are resolved once per run; a dynamic variable
    /// written in the template itself is evaluated at each use.
    /// </remarks>
    /// <exception cref="HttpFileException">A variable is not defined or a value could not be read.</exception>
    public async Task<string> ResolveAsync(Template template, int line, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);

        StringBuilder builder = new();
        IReadOnlyDictionary<string, FileVariable>? visibleVariables = null;
        for (int index = 0; index < template.Segments.Count; index++)
        {
            builder.Append(template.Segments[index] switch
            {
                LiteralSegment literal => literal.Text,
                VariableSegment variable =>
                    await ResolveVariableAsync(
                        variable.Name,
                        visibleVariables ??= file.VariablesVisibleAt(line),
                        cancellationToken).ConfigureAwait(false)
                    ?? throw NotDefined(variable.Name, TemplateLines.LineOf(template, index, line)),
                DynamicSegment dynamic =>
                    await dynamicVariables.EvaluateAsync(dynamic, cancellationToken).ConfigureAwait(false),
                RequestReferenceSegment reference => EvaluateReference(reference),
                _ => throw new UnreachableException(),
            });
        }

        return builder.ToString();
    }

    private async Task<string?> ResolveVariableAsync(
        string name,
        IReadOnlyDictionary<string, FileVariable> visibleVariables,
        CancellationToken cancellationToken)
    {
        if (overrides is not null && overrides.TryGetValue(name, out string? overrideValue))
        {
            return overrideValue;
        }

        if (visibleVariables.TryGetValue(name, out FileVariable? variable))
        {
            if (!_fileVariableValues.TryGetValue(variable, out FileVariableValue? value))
            {
                // A file variable sees only the definitions above it, so this recursion cannot loop.
                bool usedBefore = _usesEntraAccessToken;
                _usesEntraAccessToken = false;
                value = new FileVariableValue(
                    await ResolveAsync(variable.Value, variable.Line, cancellationToken).ConfigureAwait(false),
                    _usesEntraAccessToken);
                _fileVariableValues.Add(variable, value);
                _usesEntraAccessToken = usedBefore;
            }

            // The value is cached, so what it was made from has to be remembered with it.
            _usesEntraAccessToken |= value.UsesEntraAccessToken;

            return value.Text;
        }

        if (!environment.TryGetValue(name, out EnvironmentValue? environmentValue))
        {
            return null;
        }

        return environmentValue switch
        {
            LiteralEnvironmentValue literal => literal.Value,
            ProviderEnvironmentValue provider =>
                await ResolveProviderValueAsync(name, provider, cancellationToken).ConfigureAwait(false),
            _ => throw new UnreachableException(),
        };
    }

    private async Task<string> ResolveProviderValueAsync(
        string name,
        ProviderEnvironmentValue value,
        CancellationToken cancellationToken)
    {
        _usesEntraAccessToken |= string.Equals(
            value.Provider,
            EntraAccessTokenProvider.ProviderName,
            StringComparison.Ordinal);

        if (_providerValues.TryGetValue(name, out string? secret))
        {
            return secret;
        }

        // The loader only produces provider values from an environment file, so a missing path is a bug in
        // the caller, not a user error.
        string directory = Path.GetDirectoryName(environment.EnvironmentFilePath)
            ?? throw new InvalidOperationException($"Variable '{name}' uses a provider without an environment file.");

        ISecretProvider provider = providers.Get(value.Provider, name);
        secret = await provider
            .ResolveAsync(new SecretRequest(name, value.Definition, directory), cancellationToken)
            .ConfigureAwait(false);

        secretMask.Add(secret);
        _providerValues.Add(name, secret);

        return secret;
    }

    private string EvaluateReference(RequestReferenceSegment reference)
    {
        if (!exchanges.TryGet(reference.RequestName, out HttpExchange? exchange))
        {
            throw new HttpFileException(Messages.Format(s_requestNotSentFormat, reference.RequestName));
        }

        return ReferenceEvaluator.Evaluate(reference, exchange);
    }

    private sealed record FileVariableValue(string Text, bool UsesEntraAccessToken);

    private HttpFileException NotDefined(string name, int line)
    {
        if (environment.SelectedName is null && environment.AvailableNames.Count > 0)
        {
            return new HttpFileException(Messages.Format(
                s_variableNotDefinedNoEnvironmentFormat,
                name,
                line,
                string.Join(Messages.ListSeparator, environment.AvailableNames)));
        }

        return new HttpFileException(Messages.Format(s_variableNotDefinedFormat, name, line));
    }
}
