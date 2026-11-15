# Behaviour

[Back to README](../README.md)

- **Redirects** are followed, up to 50. `Authorization`, `Cookie` and secret-holding headers are not sent to
  another origin. `https` to `http` is not followed.
- **TLS:** certificates are validated. For a dev certificate, run `dotnet dev-certs https --trust` or pass
  `--insecure`.
- **Cookies** are not stored between requests. Write a `Cookie` header to send one.
- **Timeout:** 100 seconds per request, redirects included.
- **Compressed responses:** gzip and deflate are decoded. Other codings, such as `br`, give a warning.
