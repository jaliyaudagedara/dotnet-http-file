namespace DotNetHttpFile.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    public string DirectoryPath { get; } = Directory.CreateTempSubdirectory("dotnet-http-file-tests-").FullName;

    /// <summary>
    /// Creates the folder at <paramref name="relativePath"/> and returns its full path.
    /// </summary>
    public string CreateDirectory(string relativePath) =>
        Directory.CreateDirectory(Path.Combine(DirectoryPath, relativePath)).FullName;

    /// <summary>
    /// Writes the file at <paramref name="relativePath"/>, creating its folder.
    /// </summary>
    /// <returns>
    /// The full path of the file.
    /// </returns>
    public async Task<string> WriteFileAsync(
        string relativePath,
        string content,
        CancellationToken cancellationToken = default)
    {
        string path = Path.Combine(DirectoryPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? DirectoryPath);
        await File.WriteAllTextAsync(path, content, cancellationToken);

        return path;
    }

    public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
}
