using DotNetHttpFile.Environments;
using DotNetHttpFile.Tests.Support;

namespace DotNetHttpFile.Tests.Environments;

public sealed class EnvironmentFileLocatorTests : IDisposable
{
    private const string EmptyEnvironmentFile = "{}";
    private const string EmptyProjectFile = "<Project />";

    private readonly TemporaryDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task Find_FileInParentFolder_ReturnsNearest()
    {
        await WriteAsync(EnvironmentFileLocator.FileName, EmptyEnvironmentFile);
        string nearest = await WriteAsync(
            Path.Combine("parent", EnvironmentFileLocator.FileName),
            EmptyEnvironmentFile);
        string httpFilePath = Path.Combine(_directory.CreateDirectory(Path.Combine("parent", "child")), "api.http");

        string? found = EnvironmentFileLocator.Find(httpFilePath);

        Assert.Equal(nearest, found);
    }

    [Fact]
    public async Task Find_FileBesideHttpFile_ReturnsIt()
    {
        string beside = await WriteAsync(EnvironmentFileLocator.FileName, EmptyEnvironmentFile);

        string? found = EnvironmentFileLocator.Find(Path.Combine(_directory.DirectoryPath, "api.http"));

        Assert.Equal(beside, found);
    }

    [Fact]
    public void Find_NoFile_ReturnsNull()
    {
        string httpFilePath = Path.Combine(_directory.CreateDirectory("child"), "api.http");

        string? found = EnvironmentFileLocator.Find(httpFilePath);

        Assert.Null(found);
    }

    [Fact]
    public async Task Find_OnlyUserFile_ReturnsNull()
    {
        await WriteAsync(EnvironmentFileLocator.FileName + ".user", EmptyEnvironmentFile);

        string? found = EnvironmentFileLocator.Find(Path.Combine(_directory.DirectoryPath, "api.http"));

        Assert.Null(found);
    }

    [Fact]
    public async Task FindProjectDirectory_CsprojTwoLevelsUp_ReturnsThatFolder()
    {
        await WriteAsync(Path.Combine("project", "Api.csproj"), EmptyProjectFile);
        string start = _directory.CreateDirectory(Path.Combine("project", "one", "two"));

        string? found = ProjectLocator.FindProjectDirectory(start);

        Assert.Equal(Path.Combine(_directory.DirectoryPath, "project"), found);
    }

    [Fact]
    public async Task FindProjectDirectory_CsprojInStartFolder_ReturnsStartFolder()
    {
        await WriteAsync(Path.Combine("outer", "Outer.csproj"), EmptyProjectFile);
        await WriteAsync(Path.Combine("outer", "inner", "Inner.csproj"), EmptyProjectFile);
        string start = Path.Combine(_directory.DirectoryPath, "outer", "inner");

        string? found = ProjectLocator.FindProjectDirectory(start);

        Assert.Equal(start, found);
    }

    [Fact]
    public async Task FindProjectDirectory_SeveralCsprojInNearestFolder_ThrowsWithFolder()
    {
        await WriteAsync(Path.Combine("project", "First.csproj"), EmptyProjectFile);
        await WriteAsync(Path.Combine("project", "Second.csproj"), EmptyProjectFile);
        string start = _directory.CreateDirectory(Path.Combine("project", "one"));

        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => ProjectLocator.FindProjectDirectory(start));

        Assert.Equal(
            $"More than one project file was found in {Path.Combine(_directory.DirectoryPath, "project")}.",
            exception.Message);
    }

    [Fact]
    public async Task FindProjectDirectory_FolderOnTheWayUpCannotBeListed_SearchesPastIt()
    {
        await WriteAsync(Path.Combine("project", "Api.csproj"), EmptyProjectFile);
        string unlistable = _directory.CreateDirectory(Path.Combine("project", "unlistable"));
        string start = _directory.CreateDirectory(Path.Combine("project", "unlistable", "start"));

        string? found;
        using (WithheldAccess.ToList(unlistable))
        {
            found = ProjectLocator.FindProjectDirectory(start);
        }

        Assert.Equal(Path.Combine(_directory.DirectoryPath, "project"), found);
    }

    [Fact]
    public async Task FindProjectFile_OneCsproj_ReturnsItsPath()
    {
        string projectFile = await WriteAsync("Api.csproj", EmptyProjectFile);
        await WriteAsync(Path.Combine("nested", "Nested.csproj"), EmptyProjectFile);
        await WriteAsync("Api.csproj.user", EmptyProjectFile);

        string? found = ProjectLocator.FindProjectFile(_directory.DirectoryPath);

        Assert.Equal(projectFile, found);
    }

    [Fact]
    public async Task FindProjectFile_UnnormalizedDirectory_ReturnsFullPath()
    {
        string projectFile = await WriteAsync("Api.csproj", EmptyProjectFile);
        string unnormalized = Path.Combine(_directory.CreateDirectory("nested"), "..");

        string? found = ProjectLocator.FindProjectFile(unnormalized);

        Assert.Equal(projectFile, found);
    }

    [Fact]
    public async Task FindProjectFile_SeveralCsprojInUnnormalizedDirectory_ThrowsWithFullPath()
    {
        await WriteAsync("First.csproj", EmptyProjectFile);
        await WriteAsync("Second.csproj", EmptyProjectFile);
        string unnormalized = Path.Combine(_directory.CreateDirectory("nested"), "..");

        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => ProjectLocator.FindProjectFile(unnormalized));

        Assert.Equal($"More than one project file was found in {_directory.DirectoryPath}.", exception.Message);
    }

    [Fact]
    public async Task FindProjectFile_NoCsproj_ReturnsNull()
    {
        await WriteAsync(Path.Combine("nested", "Nested.csproj"), EmptyProjectFile);

        string? found = ProjectLocator.FindProjectFile(_directory.DirectoryPath);

        Assert.Null(found);
    }

    [Fact]
    public async Task FindProjectFile_SeveralCsproj_ThrowsWithFolder()
    {
        await WriteAsync("First.csproj", EmptyProjectFile);
        await WriteAsync("Second.csproj", EmptyProjectFile);

        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => ProjectLocator.FindProjectFile(_directory.DirectoryPath));

        Assert.Equal($"More than one project file was found in {_directory.DirectoryPath}.", exception.Message);
    }

    private Task<string> WriteAsync(string relativePath, string content) =>
        _directory.WriteFileAsync(relativePath, content, TestContext.Current.CancellationToken);
}
