using System.Text.Json.Nodes;

namespace DotNetHttpFile.Environments;

internal abstract record EnvironmentValue;

internal sealed record LiteralEnvironmentValue(string Value) : EnvironmentValue;

internal sealed record ProviderEnvironmentValue(string Provider, JsonObject Definition) : EnvironmentValue;
