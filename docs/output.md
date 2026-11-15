# Output

[Back to README](../README.md)

The values of `--show` are not case-sensitive. Without `--verbose` or `--show`, the tool prints the full view
in a terminal and the body when the output is piped or redirected. `--verbose` and `--show` cannot be used
together.

**Full view** (the default when stdout is a terminal, or with `--verbose` or `--show full`): the request
line and request headers, the status line with the time taken and the body size, the response headers, then
the body.

- A body is pretty-printed when its content type is `application/json`, `application/xml`, or one that ends
  in `+json` or `+xml`. Any other text, `text/xml` included, is printed as it was received.
- A binary body is replaced by a one-line summary. Use `--show body` to write the bytes.
- A body with `Content-Encoding` `gzip` or `deflate` is decoded before it is shown, written or referenced, and
  the size in the status line is the decoded size. The headers are shown as received. A body in another
  coding, such as `br`, is shown as a binary summary with a warning naming the coding, and referencing it from
  another request is an error. A body that is not valid in its coding, or that decodes to more than 100 MiB,
  ends the run with exit code 1 and nothing printed. A truncated compressed body is decoded as far as it goes,
  because no .NET stream reports a missing end marker; HTTP framing normally catches truncation first.
- Each dependency shows as one summary line: name, method, URL, status and time.
- Secrets are masked, as described below.
- Control characters that come from a response are shown as `\xHH` (for example `\x1B` for ESC), so a server
  can't drive your terminal with escape sequences.

**Body only** (the default when stdout is piped or redirected, or with `--show body`): the response body is
written to stdout as it was received, with gzip and deflate decoded. The status line, dependency lines,
warnings and errors go to stderr.

**Status only** (`--show status`): stdout gets exactly one line, the status line without the time and the
size.

```
> dotnet http-file ToDo.http --name getToDos --show status
HTTP/1.1 200 OK
```

**Headers** (`--show headers`): stdout gets that status line, then the response headers, one per line. There
is no request part and no body.

```
> dotnet http-file ToDo.http --name getToDos --show headers
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Date: Sun, 15 Nov 2026 09:12:44 GMT
```

Both are masked like the full view and have their control characters shown as `\xHH`. Dependency lines,
warnings and errors go to stderr, so stdout stays easy to parse. The time and the size are not printed.
Neither is ever chosen automatically.

## What is masked, and what is not

In the full view, and in what `--show headers` and `--show status` print:

- A value that came from a secret provider is shown as `***` wherever it appears: in the URL, in request and
  response headers, and in the body.
- The value of a request's `Authorization` and `Proxy-Authorization` header is shown as the scheme followed
  by `***` (for example `Bearer ***`), and a request's `Cookie` header as `***`, wherever the value came
  from.
- Other values are shown as they are, including ones from `http-client.env.json.user`, `$dotenv` and
  `$processEnv`.

Limits you should know about:

- **Body-only output is never masked.** With `--show body`, or whenever the output is piped or redirected, the
  bytes are written as received (gzip and deflate decoded). If the server echoes a secret back, it is visible
  in that output. When the tool finds a provider value in such a body, it prints one line to stderr and still
  writes the bytes unchanged:

  ```
  warning: the response body contains a secret value from this run; it is written unmasked.
  ```

  The check reads the body as UTF-8 and looks for the same spellings the full view masks. It does not see a
  secret in a body in a coding the tool does not decode, in another encoding, or in a spelling the tool does
  not predict, so the absence of the warning is not proof that the body is clean.
- **A CI system's log masker does not know a token the tool fetched while it ran.** A CI system masks the
  secrets it holds itself. A value a provider resolved during the run (an access token, a Key Vault secret)
  was never registered with it, so a body that echoes one is printed in the job log as it is. In a pipeline,
  keep such a body out of the log: redirect it to a file, or use `--show status` or `--show headers`.
- **A secret that the server re-encodes in its own way can't always be masked.** The full view masks the
  usual spellings of a secret (as written, percent-encoded, JSON-escaped and XML-escaped). A server can still
  return it in a form the tool does not predict: `%2f` in place of `%2F`, `+` for a space, different JSON
  escapes inside a plain-text body, or a secret that is itself a JSON document and comes back reformatted.

- **A very short secret makes the output hard to read.** Every occurrence of a provider value is masked, so
  a value of one character (for example `1`) turns each `1` in the URL, the headers and the body into `***`.

The tool never writes a file. Tokens and secrets are held in memory for one run and are not cached to disk
by the tool.
