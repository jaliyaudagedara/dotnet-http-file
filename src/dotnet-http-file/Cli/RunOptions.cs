using DotNetHttpFile.Output;

namespace DotNetHttpFile.Cli;

/// <summary>
/// What the command line asks a run to do.
/// </summary>
/// <param name="FilePath">The <c>.http</c> file, as written on the command line.</param>
internal sealed record RunOptions(string FilePath)
{
    /// <summary>
    /// The request given to <c>--name</c>, or null.
    /// </summary>
    public string? RequestName { get; init; }

    /// <summary>
    /// The environment given to <c>--env</c>, or null.
    /// </summary>
    public string? EnvironmentName { get; init; }

    /// <summary>
    /// The mode asked for with <c>--verbose</c> or <c>--show</c>, or null to choose it.
    /// </summary>
    public OutputMode? ForcedMode { get; init; }

    /// <summary>
    /// True to accept any server certificate (<c>--insecure</c>).
    /// </summary>
    public bool Insecure { get; init; }
}
