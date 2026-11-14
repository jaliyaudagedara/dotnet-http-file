namespace DotNetHttpFile.Secrets;

internal interface ISecretProvider
{
    /// <summary>The value of <c>provider</c> in an environment file that selects this provider.</summary>
    string Name { get; }

    /// <summary>Resolves the value of the variable described by <paramref name="request"/>.</summary>
    /// <exception cref="HttpFileException">The definition is incomplete or the value could not be read.</exception>
    Task<string> ResolveAsync(SecretRequest request, CancellationToken cancellationToken = default);
}
