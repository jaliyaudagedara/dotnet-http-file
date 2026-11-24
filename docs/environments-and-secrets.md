# Environments and secrets

[Back to README](../README.md)

Environment files (`http-client.env.json`, `.user`, `$shared`) and the `AzureKeyVault`, `AspnetUserSecrets`
and `Encrypted` providers work as in
[Visual Studio](https://learn.microsoft.com/en-us/aspnet/core/test/http-files#environment-files). Without `--env`,
only `$shared` and the file's own variables apply. `Encrypted` works on Windows only. `AzureKeyVault` reads
only vaults in the public Azure cloud (`vault.azure.net`).

`AspnetUserSecrets` and `$dotenv` look for a `.csproj` (not an `.fsproj` or `.vbproj`):

- `AspnetUserSecrets` reads `UserSecretsId` from the `.csproj` in the environment file's folder, not from
  `Directory.Build.props`.
- `$dotenv` reads the `.env` in the nearest folder with a `.csproj`, starting at the `.http` file's folder and
  going up. If there is none, it reads the `.env` next to the `.http` file.

Only values from a secret provider are masked in the tool's output, so keep secrets in a provider, not as
literal values. See [Masking](output.md#masking).

## EntraAccessToken

The tool's own provider: an Entra access token from your existing sign-in.

```json
"ApiToken": {
  "provider": "EntraAccessToken",
  "scope": "api://todo-api/.default",
  "tenantId": "11111111-2222-3333-4444-555555555555"
}
```

`tenantId` is optional. The token comes from `DefaultAzureCredential`, so on a developer machine run
`az login`. The tool never opens a browser. `-v` shows which credential supplied the token.

**Wrong tenant:** Visual Studio's sign-in is tried before the Azure CLI's. If it is for another tenant, the API
answers 401 `InvalidAuthenticationTokenTenant` and the tool prints a hint. Set `tenantId`, or use only the CLI:

```bash
AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet http-file MyApi.http --name getToDos --env dev
```
