namespace DotNetHttpFile.Tests.Support;

/// <summary>
/// Where the output directory holds the files Visual Studio was captured with
/// (<c>Conformance/VisualStudio</c> in the test project).
/// </summary>
internal static class VisualStudioCapture
{
    public const string CaptureFileName = "capture.http";

    /// <summary>
    /// The folder with <c>http-client.env.json.user</c>, which carries a provider value.
    /// </summary>
    public const string UserEnvironmentFolder = "env-user";

    public static string DirectoryPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "Conformance", "VisualStudio");

    public static string CaptureFilePath { get; } = Path.Combine(DirectoryPath, CaptureFileName);
}
