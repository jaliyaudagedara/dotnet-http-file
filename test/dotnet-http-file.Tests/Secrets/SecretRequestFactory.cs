using System.Text.Json.Nodes;
using DotNetHttpFile.Secrets;

namespace DotNetHttpFile.Tests.Secrets;

internal static class SecretRequestFactory
{
    public static SecretRequest Create(string variableName, string definitionJson, string environmentFileDirectory = "")
    {
        JsonObject definition = JsonNode.Parse(definitionJson) as JsonObject
            ?? throw new ArgumentException("The definition must be a JSON object.", nameof(definitionJson));

        return new SecretRequest(variableName, definition, environmentFileDirectory);
    }
}
