namespace DotNetHttpFile.Secrets;

internal sealed class SecretProviderRegistry(IEnumerable<ISecretProvider> providers)
{
    private readonly Dictionary<string, ISecretProvider> _providers =
        providers.ToDictionary(provider => provider.Name, StringComparer.Ordinal);

    /// <summary>
    /// Gets the provider whose name is exactly <paramref name="providerName"/>.
    /// </summary>
    /// <param name="providerName">The value of <c>provider</c> in the environment file.</param>
    /// <param name="variableName">The variable that uses the provider, named in the error.</param>
    /// <exception cref="HttpFileException">No provider has that name.</exception>
    public ISecretProvider Get(string providerName, string variableName)
    {
        ArgumentNullException.ThrowIfNull(providerName);

        if (_providers.TryGetValue(providerName, out ISecretProvider? provider))
        {
            return provider;
        }

        throw new HttpFileException(Messages.Format(
            Resources.UnknownSecretProvider,
            variableName,
            providerName,
            string.Join(Messages.ListSeparator, _providers.Keys.Order(StringComparer.Ordinal))));
    }
}
