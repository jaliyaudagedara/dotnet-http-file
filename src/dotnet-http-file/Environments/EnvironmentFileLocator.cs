namespace DotNetHttpFile.Environments;

internal static class EnvironmentFileLocator
{
    public const string FileName = "http-client.env.json";

    /// <summary>
    /// Finds the environment file nearest to the <c>.http</c> file at <paramref name="httpFilePath"/>,
    /// searching its folder and then each parent folder.
    /// </summary>
    /// <returns>
    /// The full path of the environment file, or null when there is none.
    /// </returns>
    public static string? Find(string httpFilePath)
    {
        ArgumentNullException.ThrowIfNull(httpFilePath);

        DirectoryInfo? directory = Directory.GetParent(Path.GetFullPath(httpFilePath));
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, FileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
