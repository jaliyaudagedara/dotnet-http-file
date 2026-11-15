# Environments and secrets

[Back to README](../README.md)

Environment files (`http-client.env.json`, `.user`, `$shared`) and the `AzureKeyVault`, `AspnetUserSecrets`
and `Encrypted` providers work as in
[Visual Studio](https://learn.microsoft.com/aspnet/core/test/http-files#environment-files). Without `--env`,
only `$shared` and the file's own variables apply. `AspnetUserSecrets` reads `UserSecretsId` from the
`.csproj` only, not `Directory.Build.props`. `Encrypted` works on Windows only.

## EntraAccessToken

The tool's own provider: an Entra access token from your existing sign-in.

```json
"ApiToken": {
  "provider": "EntraAccessToken",
  "scope": "api://todo-api/.default",
  "tenantId": "11111111-2222-3333-4444-555555555555"
}
```

`tenantId` is optional. The token comes from `DefaultAzureCredential`. On a developer machine, run
`az login`. The tool never opens a browser.

**Wrong tenant (401 `InvalidAuthenticationTokenTenant`):** Visual Studio is tried before the Azure CLI, so
its account wins. Set `tenantId`, or use only the CLI:

```
AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet http-file MyApi.http --name getToDos --env dev
```
