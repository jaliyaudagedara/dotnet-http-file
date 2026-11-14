namespace DotNetHttpFile.Environments;

/// <summary>What a <c>.env</c> file holds.</summary>
/// <param name="Path">The path the file was looked for at.</param>
/// <param name="Values">
/// The <c>KEY=VALUE</c> pairs, with the last value kept for a repeated key. Null when the file does not exist.
/// </param>
internal sealed record DotEnvFile(string Path, IReadOnlyDictionary<string, string>? Values)
{
    private const char Comment = '#';
    private const char Assignment = '=';
    private const char DoubleQuote = '"';
    private const char SingleQuote = '\'';

    private static readonly char[] s_lineTerminators = ['\r', '\n'];

    /// <summary>Reads the <c>.env</c> file at <paramref name="path"/>.</summary>
    public static async Task<DotEnvFile> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        string content;
        try
        {
            content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new DotEnvFile(path, Values: null);
        }

        return new DotEnvFile(path, Parse(content));
    }

    private static Dictionary<string, string> Parse(string content)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        StringSplitOptions options = StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;

        foreach (string line in content.Split(s_lineTerminators, options))
        {
            int assignment = line.IndexOf(Assignment);
            if (line.StartsWith(Comment) || assignment <= 0)
            {
                continue;
            }

            values[line[..assignment].TrimEnd()] = Unquote(line[(assignment + 1)..].TrimStart());
        }

        return values;
    }

    private static string Unquote(string value)
    {
        bool isQuoted = value.Length >= 2 && value[0] is DoubleQuote or SingleQuote && value[^1] == value[0];

        return isQuoted ? value[1..^1] : value;
    }
}
