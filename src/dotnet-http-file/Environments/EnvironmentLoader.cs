using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotNetHttpFile.Environments;

internal static class EnvironmentLoader
{
    private const string SharedName = "$shared";
    private const string UserFileSuffix = ".user";
    private const string ProviderProperty = "provider";

    private static readonly CompositeFormat s_environmentFileNotFoundFormat =
        CompositeFormat.Parse(Resources.EnvironmentFileNotFound);

    private static readonly CompositeFormat s_environmentFileNotObjectFormat =
        CompositeFormat.Parse(Resources.EnvironmentFileNotObject);

    private static readonly CompositeFormat s_environmentNotFoundFormat =
        CompositeFormat.Parse(Resources.EnvironmentNotFound);

    private static readonly CompositeFormat s_environmentNotFoundNoneDefinedFormat =
        CompositeFormat.Parse(Resources.EnvironmentNotFoundNoneDefined);

    private static readonly CompositeFormat s_environmentUnsupportedValueFormat =
        CompositeFormat.Parse(Resources.EnvironmentUnsupportedValue);

    private static readonly CompositeFormat s_environmentVariableUnsupportedValueFormat =
        CompositeFormat.Parse(Resources.EnvironmentVariableUnsupportedValue);

    private static readonly CompositeFormat s_invalidEnvironmentFileFormat =
        CompositeFormat.Parse(Resources.InvalidEnvironmentFile);

    private static readonly CompositeFormat s_invalidEnvironmentFileJsonFormat =
        CompositeFormat.Parse(Resources.InvalidEnvironmentFileJson);

    private static readonly CompositeFormat s_verboseEnvironmentUserFileFormat =
        CompositeFormat.Parse(Resources.VerboseEnvironmentUserFile);

    /// <summary>
    /// Loads the variables of <paramref name="environmentName"/> from the environment file at
    /// <paramref name="environmentFilePath"/> and its sibling <c>.user</c> file.
    /// </summary>
    /// <remarks>
    /// Later layers win: <c>$shared</c>, the <c>.user</c> file's <c>$shared</c>, the named environment, then
    /// the <c>.user</c> file's named environment. Without an environment name only <c>$shared</c> applies.
    /// </remarks>
    /// <param name="environmentFilePath">The environment file, or null when none was found.</param>
    /// <param name="environmentName">The environment to select, or null for none.</param>
    /// <param name="httpFilePath">The <c>.http</c> file the search started from, named in the error.</param>
    /// <param name="log">Receives a line naming the <c>.user</c> file when it is read.</param>
    /// <param name="cancellationToken">Cancels the file reads.</param>
    /// <exception cref="HttpFileException">
    /// An environment was named but there is no environment file or it does not define the environment, or
    /// a file is not valid or holds an unsupported value.
    /// </exception>
    public static async Task<EnvironmentSet> LoadAsync(
        string? environmentFilePath,
        string? environmentName,
        string httpFilePath,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpFilePath);

        if (environmentFilePath is null)
        {
            if (environmentName is null)
            {
                return EnvironmentSet.None;
            }

            throw new HttpFileException(
                Messages.Format(s_environmentFileNotFoundFormat, environmentName, httpFilePath));
        }

        JsonObject root = await ReadAsync(environmentFilePath, cancellationToken).ConfigureAwait(false);
        List<SourceFile> sourceFiles = [new SourceFile(environmentFilePath, root)];

        string userFilePath = environmentFilePath + UserFileSuffix;
        if (File.Exists(userFilePath))
        {
            log?.Invoke(Messages.Format(s_verboseEnvironmentUserFileFormat, userFilePath));
            JsonObject userRoot = await ReadAsync(userFilePath, cancellationToken).ConfigureAwait(false);
            sourceFiles.Add(new SourceFile(userFilePath, userRoot));
        }

        string[] availableNames =
        [
            .. sourceFiles
                .SelectMany(sourceFile => sourceFile.Root.Select(property => property.Key))
                .Where(name => !string.Equals(name, SharedName, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        if (environmentName is not null && !availableNames.Contains(environmentName, StringComparer.Ordinal))
        {
            throw EnvironmentNotFound(environmentName, environmentFilePath, availableNames);
        }

        Dictionary<string, EnvironmentValue> values = [with(StringComparer.Ordinal)];
        string[] layerNames = environmentName is null ? [SharedName] : [SharedName, environmentName];
        foreach (string layerName in layerNames)
        {
            foreach (SourceFile sourceFile in sourceFiles)
            {
                ApplyLayer(values, sourceFile, layerName);
            }
        }

        return new EnvironmentSet(environmentFilePath, environmentName, availableNames, values);
    }

    private static async Task<JsonObject> ReadAsync(string path, CancellationToken cancellationToken)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);

        JsonNode? root;
        try
        {
            if (!JsonFile.TryParse(bytes, out root))
            {
                throw new HttpFileException(Messages.Format(s_invalidEnvironmentFileFormat, path));
            }
        }
        catch (JsonException exception)
        {
            throw InvalidJson(path, exception);
        }

        return root as JsonObject
            ?? throw new HttpFileException(Messages.Format(s_environmentFileNotObjectFormat, path));
    }

    private static void ApplyLayer(
        Dictionary<string, EnvironmentValue> values,
        SourceFile sourceFile,
        string layerName)
    {
        if (!sourceFile.Root.TryGetPropertyValue(layerName, out JsonNode? layer))
        {
            return;
        }

        if (layer is not JsonObject variables)
        {
            throw new HttpFileException(
                Messages.Format(s_environmentUnsupportedValueFormat, layerName, sourceFile.Path));
        }

        foreach ((string name, JsonNode? node) in variables)
        {
            values[name] = ToValue(name, node, sourceFile.Path);
        }
    }

    private static EnvironmentValue ToValue(string name, JsonNode? node, string path)
    {
        if (JsonFile.ScalarText(node) is string text)
        {
            return new LiteralEnvironmentValue(text);
        }

        if (node is JsonObject definition
            && definition[ProviderProperty] is JsonValue provider
            && provider.GetValueKind() is JsonValueKind.String)
        {
            return new ProviderEnvironmentValue(provider.GetValue<string>(), definition);
        }

        throw new HttpFileException(Messages.Format(s_environmentVariableUnsupportedValueFormat, name, path));
    }

    // The JsonException message can quote the file content, so it stays out of the message.
    private static HttpFileException InvalidJson(string path, JsonException exception)
    {
        string message = exception.LineNumber is long zeroBasedLine
            ? Messages.Format(s_invalidEnvironmentFileJsonFormat, path, zeroBasedLine + 1)
            : Messages.Format(s_invalidEnvironmentFileFormat, path);

        return new HttpFileException(message, exception);
    }

    private static HttpFileException EnvironmentNotFound(
        string environmentName,
        string environmentFilePath,
        string[] availableNames)
    {
        if (availableNames.Length == 0)
        {
            return new HttpFileException(
                Messages.Format(s_environmentNotFoundNoneDefinedFormat, environmentName, environmentFilePath));
        }

        return new HttpFileException(Messages.Format(
            s_environmentNotFoundFormat,
            environmentName,
            environmentFilePath,
            string.Join(Messages.ListSeparator, availableNames)));
    }

    private sealed record SourceFile(string Path, JsonObject Root);
}
