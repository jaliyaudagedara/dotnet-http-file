using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using DotNetHttpFile.Environments;

namespace DotNetHttpFile.Secrets;

internal sealed class AspnetUserSecretsProvider(string userSecretsRoot) : ISecretProvider
{
    public const string ProviderName = "AspnetUserSecrets";

    private const string SecretNameField = "secretName";
    private const string SecretsFileName = "secrets.json";
    private const string UserSecretsIdElement = "UserSecretsId";
    private const string KeySeparator = ":";
    private const string CurrentDirectoryName = ".";
    private const string ParentDirectoryName = "..";

    // The ID becomes a folder name under the root, so it must not be able to point anywhere else.
    private static readonly SearchValues<char> s_invalidUserSecretsIdCharacters =
        SearchValues.Create([.. Path.GetInvalidFileNameChars(), '/', '\\']);

    private static readonly XmlReaderSettings s_projectReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    private static readonly CompositeFormat s_userSecretsFileNotFoundFormat =
        CompositeFormat.Parse(Resources.UserSecretsFileNotFound);

    private static readonly CompositeFormat s_userSecretsInvalidFileFormat =
        CompositeFormat.Parse(Resources.UserSecretsInvalidFile);

    private static readonly CompositeFormat s_userSecretsInvalidProjectFormat =
        CompositeFormat.Parse(Resources.UserSecretsInvalidProject);

    private static readonly CompositeFormat s_userSecretsInvalidUserSecretsIdFormat =
        CompositeFormat.Parse(Resources.UserSecretsInvalidUserSecretsId);

    private static readonly CompositeFormat s_userSecretsNoProjectFormat =
        CompositeFormat.Parse(Resources.UserSecretsNoProject);

    private static readonly CompositeFormat s_userSecretsNoUserSecretsIdFormat =
        CompositeFormat.Parse(Resources.UserSecretsNoUserSecretsId);

    private static readonly CompositeFormat s_userSecretsSecretNotFoundFormat =
        CompositeFormat.Parse(Resources.UserSecretsSecretNotFound);

    public string Name => ProviderName;

    /// <summary>
    /// Gets the folder where the .NET user-secrets tooling keeps its files for the current user.
    /// </summary>
    public static string DefaultRoot() =>
        OperatingSystem.IsWindows()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft",
                "UserSecrets")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".microsoft",
                "usersecrets");

    public async Task<string> ResolveAsync(SecretRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string secretName = request.GetRequiredString(ProviderName, SecretNameField);
        string projectFilePath = ProjectLocator.FindProjectFile(request.EnvironmentFileDirectory)
            ?? throw Failure(s_userSecretsNoProjectFormat, request.VariableName, request.EnvironmentFileDirectory);

        string userSecretsId = await ReadUserSecretsIdAsync(request.VariableName, projectFilePath, cancellationToken)
            .ConfigureAwait(false);
        string secretsFilePath = Path.Combine(userSecretsRoot, userSecretsId, SecretsFileName);
        JsonObject secrets = await ReadSecretsAsync(request.VariableName, secretsFilePath, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<string, string> values = [with(StringComparer.OrdinalIgnoreCase)];
        if (!TryFlatten(values, string.Empty, secrets))
        {
            throw Failure(s_userSecretsInvalidFileFormat, request.VariableName, secretsFilePath);
        }

        return values.TryGetValue(secretName, out string? value)
            ? value
            : throw Failure(s_userSecretsSecretNotFoundFormat, request.VariableName, secretName, secretsFilePath);
    }

    private static async Task<string> ReadUserSecretsIdAsync(
        string variableName,
        string projectFilePath,
        CancellationToken cancellationToken)
    {
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(projectFilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (FileReadFailure.IsFileReadFailure(exception))
        {
            throw FileReadFailure.Create(exception);
        }

        XDocument project;
        try
        {
            using MemoryStream stream = new(bytes);
            using XmlReader reader = XmlReader.Create(stream, s_projectReaderSettings);
            project = XDocument.Load(reader);
        }
        catch (XmlException exception)
        {
            throw Failure(s_userSecretsInvalidProjectFormat, exception, variableName, projectFilePath);
        }

        // Matched by local name because older project files put every element in the MSBuild namespace.
        string? userSecretsId = project
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == UserSecretsIdElement)
            ?.Value
            .Trim();

        if (string.IsNullOrEmpty(userSecretsId))
        {
            throw Failure(s_userSecretsNoUserSecretsIdFormat, variableName, projectFilePath);
        }

        if (userSecretsId is CurrentDirectoryName or ParentDirectoryName
            || userSecretsId.AsSpan().ContainsAny(s_invalidUserSecretsIdCharacters))
        {
            throw Failure(s_userSecretsInvalidUserSecretsIdFormat, variableName, projectFilePath);
        }

        return userSecretsId;
    }

    private static async Task<JsonObject> ReadSecretsAsync(
        string variableName,
        string secretsFilePath,
        CancellationToken cancellationToken)
    {
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(secretsFilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw Failure(s_userSecretsFileNotFoundFormat, exception, variableName, secretsFilePath);
        }
        catch (Exception exception) when (FileReadFailure.IsFileReadFailure(exception))
        {
            throw FileReadFailure.Create(exception);
        }

        try
        {
            if (JsonFile.TryParse(bytes, out JsonNode? root) && root is JsonObject secrets)
            {
                return secrets;
            }
        }
        catch (JsonException exception)
        {
            throw Failure(s_userSecretsInvalidFileFormat, exception, variableName, secretsFilePath);
        }

        throw Failure(s_userSecretsInvalidFileFormat, variableName, secretsFilePath);
    }

    // Joins nested names and array indexes with ':'. Returns false when two values end up under the same
    // key, such as "a:b" beside a nested a.b or two names that differ only by case: picking one would be a guess.
    private static bool TryFlatten(Dictionary<string, string> values, string key, JsonNode? node)
    {
        switch (node)
        {
            case JsonObject properties:
                foreach ((string name, JsonNode? child) in properties)
                {
                    if (!TryFlatten(values, CombineKey(key, name), child))
                    {
                        return false;
                    }
                }

                return true;

            case JsonArray items:
                for (int index = 0; index < items.Count; index++)
                {
                    string itemKey = CombineKey(key, index.ToString(CultureInfo.InvariantCulture));
                    if (!TryFlatten(values, itemKey, items[index]))
                    {
                        return false;
                    }
                }

                return true;

            default:
                return JsonFile.ScalarText(node) is not string text || values.TryAdd(key, text);
        }
    }

    private static string CombineKey(string key, string name) =>
        key.Length == 0 ? name : key + KeySeparator + name;

    private static HttpFileException Failure(CompositeFormat format, params ReadOnlySpan<object?> arguments) =>
        new(Messages.Format(format, arguments));

    private static HttpFileException Failure(
        CompositeFormat format,
        Exception innerException,
        params ReadOnlySpan<object?> arguments) =>
        new(Messages.Format(format, arguments), innerException);
}
