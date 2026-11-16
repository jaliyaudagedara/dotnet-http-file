# Environments and secrets

[Back to README](../README.md)

Environment files (`http-client.env.json`, `.user`, `$shared`) and the `AzureKeyVault`, `AspnetUserSecrets`
and `Encrypted` providers work as in
[Visual Studio](https://learn.microsoft.com/aspnet/core/test/http-files#environment-files). Without `--env`,
only `$shared` and the file's own variables apply. `Encrypted` works on Windows only. `AzureKeyVault` reads
only vaults in the public Azure cloud (`vault.azure.net`).

Only a `.csproj` counts as a project. An F# (`.fsproj`) or Visual Basic (`.vbproj`) project is not found:

- `AspnetUserSecrets` reads `UserSecretsId` from the `.csproj` in the folder of the environment file, not
  from `Directory.Build.props`. Without a `.csproj` there, the variable fails with "no project file was
  found".
- `$dotenv` reads `.env` from the nearest folder with a `.csproj`, starting at the folder of the `.http` file
  and going up. Without one, it reads the `.env` next to the `.http` file.

Values from a secret provider are masked. A literal value in an environment file or its `.user` file, a
`--var` value, and a `$dotenv` or `$processEnv` value are not, except in an `Authorization`,
`Proxy-Authorization` or `Cookie` request header. For a secret, use a secret provider in the environment file.
See [Masking](output.md#masking).

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

```bash
AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet http-file MyApi.http --name getToDos --env dev
```

After such a 401 on a request that used `EntraAccessToken`, the tool prints a hint on stderr that names
`tenantId`. `-v` shows which credential supplied the token.
