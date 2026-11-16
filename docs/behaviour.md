# Behaviour

[Back to README](../README.md)

- **Content headers:** a `Content-Type` on a request without a body is left out, as Visual Studio does (logged
  under `-v`). Other content headers on such a request give a warning.
- **Redirects** are followed, up to 50. `Authorization`, `Proxy-Authorization`, `Cookie` and secret-holding
  headers are not sent to another origin. A redirect from `https` to `http`, or to a scheme that is not `http`
  or `https`, is not followed.
- **TLS:** certificates are validated. For a dev certificate, run `dotnet dev-certs https --trust` or pass
  `--insecure`.
- **Cookies** are not stored between requests. Write a `Cookie` header to send one.
- **Timeout:** 100 seconds per request, redirects included.
- **Compressed responses:** gzip and deflate are decoded, and `x-gzip` is read as gzip. Other codings, such
  as `br`, give a warning and the body is left as received. A body that is not valid in its coding (a
  truncated gzip, zlib or deflate body included), or that decodes to more than 100 MiB, is an error: nothing
  of the response is printed and the exit code is 1. An empty body is never decoded.
