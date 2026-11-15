# Environments and secrets

[Back to README](../README.md)

## Environment files

- `http-client.env.json` is looked for in the folder of the `.http` file and then in each parent folder. The
  nearest one is used.
- `http-client.env.json.user` in the same folder overrides its values for each environment.
- When a name is defined more than once, the order of precedence, highest first, is:
  1. variables in the `.http` file
  2. the selected environment in the `.user` file
  3. the selected environment in `http-client.env.json`
  4. `$shared` in the `.user` file
  5. `$shared` in `http-client.env.json`
- An environment file with a repeated JSON property name is rejected.

Without `--env`, only the `$shared` section of the environment file and the variables in the `.http` file
apply.

## Secret providers

A variable in an environment file can be an object with a `provider`. Providers only run for the variables
that the selected request and its dependencies use, so a request that needs no token works without a
sign-in.

| Provider | Fields | What it returns |
|---|---|---|
| `EntraAccessToken` | `scope` (required), `tenantId` (optional) | A Microsoft Entra access token for the scope. This provider is new in this tool. Visual Studio does not have it. |
| `AzureKeyVault` | `secretName`, `resourceId` | A Key Vault secret. The vault name is the last segment of `resourceId`, and the vault is reached at `https://<name>.vault.azure.net` (Azure public cloud only). |
| `AspnetUserSecrets` | `secretName` | A value from the user secrets of the project in the environment file's folder. |
| `Encrypted` | `value` | A value decrypted with Windows DPAPI for the current user. Windows only. On Linux and macOS the request is not sent and the error names the variable. |

### EntraAccessToken

```json
"ApiToken": {
  "provider": "EntraAccessToken",
  "scope": "api://todo-api/.default",
  "tenantId": "11111111-2222-3333-4444-555555555555"
}
```

- `scope` is the scope to ask for, for example `api://<application-id>/.default`.
- `tenantId` is optional. Set it when the token must come from a tenant other than your sign-in's default.
  It is a tenant ID or a domain name.

The token comes from `DefaultAzureCredential` in Azure.Identity, which tries these sign-in sources in order
and uses the first one that returns a token:

1. Environment variables that describe a service principal (`EnvironmentCredential`)
2. Workload identity (`WorkloadIdentityCredential`)
3. Managed identity (`ManagedIdentityCredential`)
4. Visual Studio (`VisualStudioCredential`)
5. Azure CLI, `az login` (`AzureCliCredential`)
6. Azure PowerShell, `Connect-AzAccount` (`AzurePowerShellCredential`)
7. Azure Developer CLI, `azd auth login` (`AzureDeveloperCliCredential`)

The tool never opens a browser to sign you in. On a developer machine the usual way is the Azure CLI:

```
az login
```

or, for a specific tenant:

```
az login --tenant 11111111-2222-3333-4444-555555555555
```

If no source can provide a token, the request is not sent and the tool exits with code 2. The error names
the variable, the scope, the tenant, the sign-in sources that were tried, and the `az login` command to run.

#### When Visual Studio and the Azure CLI are signed in to different accounts

Visual Studio is tried before the Azure CLI, so if Visual Studio is signed in, its account is used even when
`az` is signed in to another one. The token is then for the wrong tenant, and Azure answers
`401 Unauthorized` with the error code `InvalidAuthenticationTokenTenant` ("The access token is from the wrong
issuer"). Either of these fixes it:

- Set `tenantId` on the variable to the tenant the API belongs to.
- Use only the Azure CLI for this run by setting `AZURE_TOKEN_CREDENTIALS`, which Azure.Identity reads:

  ```
  # bash
  AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet http-file MyApi.http --name getToDos --env dev

  # PowerShell
  $env:AZURE_TOKEN_CREDENTIALS = "AzureCliCredential"
  dotnet http-file MyApi.http --name getToDos --env dev
  ```

The Azure CLI honours `AZURE_CONFIG_DIR`, so if you keep separate `az` profiles, set it alongside
`AZURE_TOKEN_CREDENTIALS` to choose which profile the token comes from.

`AzureKeyVault` signs in the same way.

### AspnetUserSecrets

The tool reads `UserSecretsId` from the `.csproj` in the same folder as the environment file, then reads
`secretName` from that project's `secrets.json` (`%APPDATA%\Microsoft\UserSecrets\<id>\` on Windows,
`~/.microsoft/usersecrets/<id>/` on Linux and macOS).

A `UserSecretsId` that is set in `Directory.Build.props` is not found. Set it in the project's `.csproj`.
