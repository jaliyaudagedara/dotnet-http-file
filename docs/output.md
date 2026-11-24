# Output

[Back to README](../README.md)

| Mode | When | Prints |
|---|---|---|
| `full` | Default in a terminal | Request (secrets masked), status, headers, formatted body. |
| `body` | Default when piped | The response body, gzip and deflate decoded. |
| `headers` | `--show headers` | Status line and response headers. |
| `status` | `--show status` | Status line only. |

Warnings and errors go to stderr. In `body` mode the status line goes there too, so stdout holds only the body.

In a terminal, the status line is coloured, header names are teal and the request line of `full` is bold. The
body is plain. Colour is off when `NO_COLOR` is set.

Text output shows control characters and bidirectional overrides as their codes (`\x1B`, `‮`). `body`
mode writes the bytes as received.

Each request that the selected request depends on prints a one-line summary
(`→ name  GET url  200 OK  12 ms`), except under `-v`, whose log already has a line for each exchange.

`-v` logs to stderr: files read, the environment, the request plan, each exchange, the scope, tenant and
expiry of Entra tokens, and the Azure credential used. It combines with `--show`.

## Masking

- The tool masks only what it prints about the request it built: the request line, request headers,
  dependency lines, warnings, errors and the `-v` log.
- Values from a secret provider show as `***`. `Authorization` and `Proxy-Authorization` show as their scheme
  and `***` (`Bearer ***`), and `Cookie` as `***`, wherever the value came from.
- Literal values in an environment file or its `.user` file, `--var`, `$dotenv` and `$processEnv` are not
  masked anywhere else, such as in the URL.
- The response is never masked: the status line, headers and body are shown as the server sent them. If the
  server may echo a token, keep the output out of CI logs.
- A redirect location is shown as the server sent it. If it is relative or takes the request's `#fragment`,
  it is built from the request URL and masked.
