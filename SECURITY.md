# Security Policy

## Supported versions

Security fixes are applied to the latest release on the `main` branch.

The Docker images of the latest release and of `latest` are scanned every
week. A high or critical vulnerability with a fix in their Ubuntu packages or
.NET libraries leads to a rebuilt image and a patch release.

## Verifying downloads

The release files (MCPB bundles, binary archives and `SHA256SUMS`) and the
images in `ghcr.io/scormave/gramps-web-mcp` have build provenance
attestations, signed through Sigstore for the GitHub Actions workflow that
built them. The [GitHub CLI](https://cli.github.com/) checks a file or an
image against this repository:

```bash
gh attestation verify gramps-web-mcp-linux-x64.tar.gz --repo Scormave/gramps-web-mcp
gh attestation verify oci://ghcr.io/scormave/gramps-web-mcp:latest --repo Scormave/gramps-web-mcp
```

A passing check shows which workflow and commit built the file; it says
nothing about vulnerabilities in the code itself. Releases up to 2.6.0 and
the images pushed before attestations were added have none.

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
full media files through either surface. Keep `GRAMPS_MEDIA_MAX_BYTES` and
`GRAMPS_MEDIA_ALLOW_PRIVATE` set conservatively for the deployment.

Thumbnails are rendered by the server from the original file and re-encoded
from pixels, so EXIF (including GPS location), XMP, IPTC, and ICC metadata are
not passed to the client. Mode `file` returns the original bytes unchanged,
metadata included. Originals are decoded with the managed ImageSharp library,
limited to 50 MiB (or `GRAMPS_MEDIA_MAX_BYTES` if larger) and 100 megapixels.

Media files can contain sensitive photos, scans, and documents. The server
blocks bytes for Gramps media records marked private unless
`GRAMPS_MEDIA_ALLOW_PRIVATE=true`; metadata access through `get_object` with
`objectType: "media"` remains
available. End-to-end exposure also depends on the MCP client and model that
receive the typed tool content (image, audio, or embedded resource) or binary
resource content.

## Media upload

Uploading is off unless media file access is enabled
(`GRAMPS_MEDIA_RESOURCES_ENABLED=true`) and `GRAMPS_MEDIA_UPLOAD_DIRS` lists at
least one folder; until then, and in read-only mode, `create_media` is hidden
from `tools/list`.
`create_media` and `update_media` read files only from those folders and their
subfolders: symbolic links are resolved before the check, so a link cannot lead
outside, and `..` cannot climb out. The type is judged from the file's first
bytes, and only JPEG, PNG, GIF, WebP, TIFF, BMP and PDF files are uploaded.
Files larger than `GRAMPS_MEDIA_UPLOAD_MAX_BYTES` (50 MiB by default) are
refused. Point the setting at a folder that holds only files meant for the
tree, such as a downloads or scan inbox, not at a home directory: any model
connected to the server can upload whatever the folder holds.

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
probe liveness without credentials. Probes reuse the server's cached Gramps Web
token, so anonymous callers cannot make the server log in on every request.

Inside Docker, `ASPNETCORE_URLS` is typically `http://0.0.0.0:8080`, so the
server logs a startup warning when `MCP_API_KEY` is not set even if the host
publishes the port on `127.0.0.1` only. That warning is expected when external
access is already restricted at the host firewall or reverse proxy.

Browsers send an `Origin` header with the requests that carry MCP messages,
and the server refuses with 403 every request whose origin is not listed in
`MCP_ALLOWED_ORIGINS`, `GET /health` included. The list is empty by default
and takes comma-separated origins such as `https://app.example.com`. MCP
clients outside a browser (Claude Desktop, Claude Code, SDK and command-line
clients) send no `Origin` and are not affected. The check keeps a web page on
another site from calling tools with the server's Gramps credentials, also
through DNS rebinding, where the page points a name it controls at your
machine so the browser treats it as the same site as the server; without the
check, that works even on a server bound to `127.0.0.1`. List only the origins
of browser-based MCP clients you trust.

For further hardening, set `AllowedHosts` to the host names clients use,
separated by semicolons, for example
`AllowedHosts=gramps-mcp.example.com;localhost`. ASP.NET Core then answers a
request for any other `Host` with 400, an IP address such as `127.0.0.1`
included unless it is listed. Keep `localhost` in the list in Docker: the
image's `HEALTHCHECK` runs `dotnet /app/GrampsWeb.Mcp.dll --health-check`,
which requests `http://localhost:8080/health`.
