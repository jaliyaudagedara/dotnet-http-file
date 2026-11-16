# Output

[Back to README](../README.md)

| Mode | When | Prints |
|---|---|---|
| `full` | Default in a terminal, or `-v` | Request (secrets masked), status, headers, formatted body. |
| `body` | Default when piped | The response body, gzip and deflate decoded. |
| `headers` | `--show headers` | Status line and response headers. |
| `status` | `--show status` | Status line only. |

Status lines, warnings and errors go to stderr when stdout carries the body.

## Masking

- Masking covers what the tool prints about the request it built: the request line, request headers, the
  line for each dependency, warnings and errors. Values from a secret provider show as `***`, and
  `Authorization` as `Bearer ***`.
- The response is shown as the server sent it: status line, headers and body. Keep output out of CI logs if
  the server may echo a token.
- A redirect location is shown as the server sent it, unless it takes parts of the request URL (a relative
  location, or one that takes the request's `#fragment`). Then it is masked.
- Values from `.user` files, `$dotenv` and `$processEnv` are not masked.
