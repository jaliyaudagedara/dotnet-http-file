using DotNetHttpFile.Output;

namespace DotNetHttpFile.Cli;

/// <summary>What the command line asks a run to do.</summary>
/// <param name="FilePath">The <c>.http</c> file, as written on the command line.</param>
/// <param name="RequestName">The request given to <c>--name</c>, or null.</param>
/// <param name="EnvironmentName">The environment given to <c>--env</c>, or null.</param>
/// <param name="ForcedMode">The mode asked for with <c>--verbose</c> or <c>--show</c>, or null to choose it.</param>
/// <param name="Insecure">True to accept any server certificate (<c>--insecure</c>).</param>
internal sealed record RunOptions(
    string FilePath,
    string? RequestName,
    string? EnvironmentName,
    OutputMode? ForcedMode,
    bool Insecure);
