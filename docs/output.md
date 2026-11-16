# Output

[Back to README](../README.md)

| Mode | When | Prints |
|---|---|---|
| `full` | Default in a terminal | Request (secrets masked), status, headers, formatted body. |
| `body` | Default when piped | The response body, gzip and deflate decoded. |
| `headers` | `--show headers` | Status line and response headers. |
| `status` | `--show status` | Status line only. |

Status lines, warnings and errors go to stderr when stdout carries the body.

In a terminal, the status line is coloured, header names are teal and the request line of `full` is bold. The
body is plain. Colour is off when output is redirected or `NO_COLOR` is set.

A request that the selected request refers to prints a one-line summary (`→ name  GET url  200 OK  12 ms`).
With `-v` that line is left out, because the `-v` log already has a line for each exchange.

`-v` logs to stderr: files read, the environment, the request plan, each exchange, and for Entra tokens the scope,
tenant, expiry and credential. It never logs a token or a secret-provider value (shown as `***`), and
combines with `--show`.

## Masking

- Masking covers what the tool prints about the request it built: the request line, request headers, the
  line for each dependency, warnings, errors and the `-v` log. Values from a secret provider show as `***`, and
  `Authorization` as `Bearer ***`.
- The response is shown as the server sent it: status line, headers and body. Keep output out of CI logs if
  the server may echo a token.
- A redirect location is shown as the server sent it, unless it takes parts of the request URL (a relative
  location, or one that takes the request's `#fragment`). Then it is masked.
- Values from `.user` files, `$dotenv`, `$processEnv` and `--var` are not masked.
