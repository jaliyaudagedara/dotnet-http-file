using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotNetHttpFile.Secrets;

/// <summary>
/// A provider-backed variable to resolve.
/// </summary>
/// <param name="VariableName">The variable name, used in error messages.</param>
/// <param name="Definition">The variable's JSON object from the environment file.</param>
/// <param name="EnvironmentFileDirectory">The folder that holds the environment file.</param>
internal sealed record SecretRequest(string VariableName, JsonObject Definition, string EnvironmentFileDirectory)
{
    /// <summary>
    /// Reads a required string property of the definition.
    /// </summary>
    /// <exception cref="HttpFileException">The property is absent, empty or not a string.</exception>
    public string GetRequiredString(string providerName, string fieldName)
    {
        if (TryGetString(fieldName, out string? value))
        {
            return value;
        }

        throw new HttpFileException(Messages.Format(
            Resources.SecretFieldMissing,
            VariableName,
            providerName,
            fieldName));
    }

    /// <summary>
    /// Reads an optional string property of the definition.
    /// </summary>
    /// <returns>
    /// The value, or null when the property is absent or JSON null.
    /// </returns>
    /// <exception cref="HttpFileException">The property is present but empty or not a string.</exception>
    public string? GetOptionalString(string providerName, string fieldName)
    {
        if (Definition[fieldName] is null)
        {
            return null;
        }

        if (TryGetString(fieldName, out string? value))
        {
            return value;
        }

        throw new HttpFileException(Messages.Format(
            Resources.SecretFieldInvalid,
            VariableName,
            providerName,
            fieldName));
    }

    // The generated record text would print the definition, which can hold an encrypted value.
    public override string ToString() => VariableName;

    private bool TryGetString(string fieldName, [NotNullWhen(true)] out string? value)
    {
        value = Definition[fieldName] is JsonValue node && node.GetValueKind() is JsonValueKind.String
            ? node.GetValue<string>()
            : null;

        return !string.IsNullOrWhiteSpace(value);
    }
}
