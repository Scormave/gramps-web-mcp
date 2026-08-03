# Security Policy

## Supported versions

Security fixes are applied to the latest release on the `main` branch.

## Reporting a vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

Instead, use [GitHub Security Advisories](https://github.com/Scormave/gramps-web-mcp/security/advisories/new)
to report a vulnerability privately, or contact the maintainers through the
repository owner profile.

Include as much detail as possible:

- Description of the vulnerability and potential impact
- Steps to reproduce
- Affected versions or commits
- Suggested fix, if you have one

We aim to acknowledge reports within a few business days and will work with you
on a fix and coordinated disclosure timeline.

## Scope

This policy covers the gramps-web-mcp server code and its default configuration.
Issues in Gramps Web itself should be reported to the [Gramps Web project](https://github.com/gramps-project/gramps-web).

## Media file access

Binary media tools and resources are disabled by default. Operators must set
`GRAMPS_MEDIA_RESOURCES_ENABLED=true` before MCP clients can read thumbnails or
full media files through either surface. Keep `GRAMPS_MEDIA_MAX_BYTES`,
`GRAMPS_MEDIA_ALLOWED_MIME_TYPES`, and `GRAMPS_MEDIA_ALLOW_PRIVATE` set
conservatively for the deployment.

Media files can contain sensitive photos, scans, and documents. The server
blocks bytes for Gramps media records marked private unless
`GRAMPS_MEDIA_ALLOW_PRIVATE=true`; metadata access through `get_media` remains
available. End-to-end exposure also depends on the MCP client and model that
receive the typed tool content (image, audio, or embedded resource) or binary
resource content.

## HTTP MCP exposure

When running with `MCP_TRANSPORT=http` or `sse`, the MCP endpoints are reachable
over the network. Anyone who can reach the port can invoke tools using the
server's configured Gramps credentials unless you protect the endpoint.

Recommended practices:

- Set `MCP_API_KEY` to a secret of at least 16 characters. Clients must send
  `Authorization: Bearer <key>` or `X-Api-Key: <key>` on MCP requests. Generate
  a key with `openssl rand -base64 32`. Comma-separated values support rotation.
- Terminate TLS and authentication at a reverse proxy when exposing MCP on the
  public internet.
- Bind to `127.0.0.1` for local-only use (`ASPNETCORE_URLS=http://127.0.0.1:8080`).
- Set `GRAMPS_READ_ONLY=true` as an additional safeguard while exploring tools.
- Rotate keys by updating `MCP_API_KEY` with a comma-separated list of valid keys.

Limitations of the static shared key:

- All authenticated MCP clients share the same access level.
- There is no per-user audit trail on the MCP layer.

`GET /health` remains anonymous so Docker `HEALTHCHECK` and load balancers can
probe liveness without credentials.

Inside Docker, `ASPNETCORE_URLS` is typically `http://0.0.0.0:8080`, so the
server logs a startup warning when `MCP_API_KEY` is not set even if the host
publishes the port on `127.0.0.1` only. That warning is expected when external
access is already restricted at the host firewall or reverse proxy.
