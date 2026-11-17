using DotNetHttpFile.Parsing;

namespace DotNetHttpFile.Cli;

internal interface IRequestPicker
{
    /// <summary>
    /// Gets whether the terminal can show the choice. <see cref="PickAsync"/> must not be called when it cannot.
    /// </summary>
    bool CanPick { get; }

    /// <summary>
    /// Asks the user which of <paramref name="requests"/> to run.
    /// </summary>
    /// <returns>
    /// The chosen request, or null when the user leaves without choosing.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<RequestDefinition?> PickAsync(
        IReadOnlyList<RequestDefinition> requests,
        CancellationToken cancellationToken = default);
}
