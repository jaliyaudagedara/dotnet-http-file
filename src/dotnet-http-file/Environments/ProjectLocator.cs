using System.Globalization;
using System.Text;

namespace DotNetHttpFile.Environments;

internal static class ProjectLocator
{
    private const string ProjectFilePattern = "*.csproj";

    private static readonly CompositeFormat s_multipleProjectFilesFormat =
        CompositeFormat.Parse(Resources.MultipleProjectFiles);

    /// <summary>
    /// Finds the nearest folder that holds a project file, starting with <paramref name="startDirectory"/>
    /// and then searching each parent folder.
    /// </summary>
    /// <returns>The full path of the project folder, or null when there is none.</returns>
    /// <exception cref="HttpFileException">The nearest folder with a project file holds several.</exception>
    public static string? FindProjectDirectory(string startDirectory)
    {
        ArgumentNullException.ThrowIfNull(startDirectory);

        DirectoryInfo? directory = new(Path.GetFullPath(startDirectory));
        while (directory is not null)
        {
            if (FindProjectFile(directory.FullName) is not null)
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>Finds the project file in <paramref name="directory"/>, without searching its subfolders.</summary>
    /// <returns>The path of the project file, or null when the folder has none.</returns>
    /// <exception cref="HttpFileException">The folder holds several project files.</exception>
    public static string? FindProjectFile(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        directory = Path.GetFullPath(directory);
        string[] projectFiles = Directory.GetFiles(directory, ProjectFilePattern, SearchOption.TopDirectoryOnly);

        return projectFiles.Length switch
        {
            0 => null,
            1 => projectFiles[0],
            _ => throw new HttpFileException(
                string.Format(CultureInfo.CurrentCulture, s_multipleProjectFilesFormat, directory)),
        };
    }
}
