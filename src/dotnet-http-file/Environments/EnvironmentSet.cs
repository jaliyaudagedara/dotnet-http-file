using System.Diagnostics.CodeAnalysis;

namespace DotNetHttpFile.Environments;

internal sealed class EnvironmentSet
{
    private readonly IReadOnlyDictionary<string, EnvironmentValue> _values;

    public EnvironmentSet(
        string? environmentFilePath,
        string? selectedName,
        IReadOnlyList<string> availableNames,
        IReadOnlyDictionary<string, EnvironmentValue> values)
    {
        ArgumentNullException.ThrowIfNull(availableNames);
        ArgumentNullException.ThrowIfNull(values);

        EnvironmentFilePath = environmentFilePath;
        SelectedName = selectedName;
        AvailableNames = availableNames;
        _values = values;
    }

    /// <summary>The set used when no environment file was found.</summary>
    public static EnvironmentSet None { get; } = new(
        environmentFilePath: null,
        selectedName: null,
        availableNames: [],
        values: new Dictionary<string, EnvironmentValue>(StringComparer.Ordinal));

    public string? EnvironmentFilePath { get; }

    /// <summary>The selected environment, or null when only <c>$shared</c> applies.</summary>
    public string? SelectedName { get; }

    /// <summary>
    /// The environments defined in the environment file and its <c>.user</c> file, without <c>$shared</c>,
    /// in ordinal order.
    /// </summary>
    public IReadOnlyList<string> AvailableNames { get; }

    /// <summary>Gets the value of the variable <paramref name="name"/>, matched by case.</summary>
    public bool TryGetValue(string name, [NotNullWhen(true)] out EnvironmentValue? value) =>
        _values.TryGetValue(name, out value);
}
