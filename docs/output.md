# Output

[Back to README](../README.md)

| Mode | When | Prints |
|---|---|---|
| `full` | Default in a terminal, or `-v` | Request, status, headers, formatted body. Secrets masked. |
| `body` | Default when piped | The response body, gzip and deflate decoded. **Not masked.** |
| `headers` | `--show headers` | Status line and response headers. Masked. |
| `status` | `--show status` | Status line only. |

Status lines, warnings and errors go to stderr when stdout carries the body.

## Masking

- Values from a secret provider show as `***`, and `Authorization` as `Bearer ***`.
- Values from `.user` files, `$dotenv` and `$processEnv` are not masked.
- Body output is never masked. Keep it out of CI logs if the server may echo a token.
