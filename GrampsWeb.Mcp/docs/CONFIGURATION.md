# Configuration

The server reads every setting from an environment variable: `-e` options of
`docker run`, the `.env` file of
[Docker Compose](INSTALLATION.md#gramps-web--mcp-docker-compose), the `env`
object of a [client entry](CLIENTS.md), or the setup form of the
[Claude Desktop extension](INSTALLATION.md#claude-desktop-mcpb-extension).
The Default column gives the value a variable takes when it is unset, and the
one the extension's setup form starts with where that differs.

## Gramps connection

| Variable | Description | Default |
|----------|-------------|---------|
| `GRAMPS_API_URL` | Base URL of your Gramps Web instance (no trailing slash) | — |
| `GRAMPS_USERNAME` | API user name | — |
| `GRAMPS_PASSWORD` | API password | — |
| `GRAMPS_REFRESH_TOKEN` | Used instead of the user name and password; see [Refresh token instead of a password](#refresh-token-instead-of-a-password) | — |
| `GRAMPS_TREE_ID` | Tree UUID on that server | — |

All outgoing requests to Gramps Web, including authentication, media downloads,
and health checks, automatically identify this application with
`User-Agent: gramps-web-mcp/<version> (+https://github.com/Scormave/gramps-web-mcp)`.
The version comes from the application build; no configuration is required.

## Refresh token instead of a password

If Gramps Web has password login turned off (`OIDC_DISABLE_LOCAL_AUTH=true`),
set `GRAMPS_REFRESH_TOKEN` and leave `GRAMPS_USERNAME` and `GRAMPS_PASSWORD`
unset. The server exchanges it for access tokens at `/api/token/refresh/`.

Get one while password login is still on:

```bash
curl -s -X POST https://gramps.example.com/api/token/ \
  -H 'Content-Type: application/json' \
  -d '{"username":"mcp","password":"..."}' | jq -r .refresh_token
```

Gramps Web refresh tokens do not expire. Treat it like a password. Deleting
the user or rotating the Gramps Web secret key invalidates it.

## Runtime mode

| Variable | Description | Default |
|----------|-------------|---------|
| `GRAMPS_READ_ONLY` | `true` publishes only read tools and blocks direct create, update, and delete calls | `false` (the Claude Desktop extension: `true`) |
| `GRAMPS_MUTATION_SERIALIZE` | Runs create/update/delete HTTP calls one at a time in this process, and protects the complete read/modify/write sequence of update tools against other such calls | `true` |
| `GRAMPS_MUTATION_MIN_INTERVAL_MS` | Minimum pause between mutation HTTP calls, in milliseconds | `0` (the Claude Desktop extension: `250`) |

- Write serialization and the optional interval protect typical Gramps Web
  SQLite trees from agent write bursts.
- The write gate is **in-process only**. It does not coordinate across multiple
  MCP replicas, the Gramps Web UI, or other API clients.
- SQLite deployments that still see `database is locked` on sequential edits
  should set `GRAMPS_MUTATION_MIN_INTERVAL_MS=250` or `500`.
- On SQLite lock errors or upstream HTTP 429, mutation tools return a retryable
  MCP error with a short backoff hint instead of a generic 500.
- Set `GRAMPS_MUTATION_SERIALIZE=false` when Gramps Web uses PostgreSQL and you
  want parallel writes.

## Confirming deletes, merges and undos

`delete_object`, `merge_objects` and `undo_change` ask the user to confirm in a
dialog before they change the tree, when the client supports
[MCP elicitation](https://modelcontextprotocol.io/specification/2025-11-25/client/elicitation).
The dialog names the record, or both records of a merge, or lists what the
undo will change, and the user ticks a box to go ahead. Declining or closing it
changes nothing. Dry runs and refusals do not ask.

| Variable | Description | Default |
|----------|-------------|---------|
| `GRAMPS_CONFIRMATION` | `when-supported` asks clients that support elicitation and runs as before in the others; `required` refuses deletes, merges and undos in clients that cannot ask; `off` never asks | `when-supported` |
| `GRAMPS_REQUIRE_USER_INTERACTION` | `true` publishes the three tools with the `anthropic/requiresUserInteraction` tool metadata, so clients that honour it ask their own permission on every call; `false` leaves the metadata out, and the client's permission settings decide | `true` |

- Stateless Streamable HTTP (`MCP_STATELESS=true`, the default) cannot send
  requests to the client, so it never asks, and `required` refuses every
  delete, merge and undo there. Set `MCP_STATELESS=false` to confirm over
  HTTP; stdio and legacy SSE keep a session and can ask.
- All three tools also carry the `anthropic/requiresUserInteraction` tool
  metadata, whatever `GRAMPS_CONFIRMATION` says. Clients that honour it, such
  as Claude Code, ask their own permission first, so the user may confirm
  twice; `off` keeps only the client's prompt.
- Claude Code asks for a tool with that metadata
  [on every call](https://code.claude.com/docs/en/mcp#require-approval-for-a-specific-tool),
  in every permission mode, and no allow rule or hook of the user's skips the
  prompt. `GRAMPS_REQUIRE_USER_INTERACTION=false` leaves the metadata out, for
  an operator who wants the client's own permission settings to decide, such
  as on a tree with one trusted user. The three tools are then asked about
  like any other write tool. Only `true` and `false` are accepted, so a
  mistyped value stops the server instead of removing the prompt, and the
  server logs at startup that the metadata is off.
- The tool call lasts until the user answers. If the client cancels the call
  first, nothing changes.
- The server logs once per client whether it asks.

See the [tool catalog](TOOL_CATALOG.md#confirmation-dialogs)
for the dialog and the replies.

## Media file access

Media byte tools/resources are disabled by default. `get_object` with
`objectType: "media"` remains
available for metadata without enabling file downloads.
When disabled, `read_media` is omitted from
`tools/list`; direct calls by a client that already knows its name return a
configuration error without downloading bytes. The same setting switches
[uploads](#media-upload) on and off.

| Variable | Description | Default |
|----------|-------------|---------|
| `GRAMPS_MEDIA_RESOURCES_ENABLED` | Enables binary media tools/resources for thumbnails and full files, and uploads | `false` |
| `GRAMPS_MEDIA_MAX_BYTES` | Maximum bytes returned by `read_media` or any media resource | `5242880` |
| `GRAMPS_MEDIA_ALLOW_PRIVATE` | Allows bytes for Gramps media records marked private | `false` |

Prefer `read_media(mode: "thumbnail")` or `gramps://media/{handle}/thumbnail/{size}` for AI
analysis. Full files can be large and sensitive, carry their original metadata,
and are still subject to the size and private-record checks.

`GRAMPS_MEDIA_MAX_BYTES` limits what is returned to the client. To render a
thumbnail, the server downloads the original from Gramps Web into memory, up to
50 MiB or `GRAMPS_MEDIA_MAX_BYTES`, whichever is larger, and up to 100
megapixels; only the preview is returned.

`GRAMPS_MEDIA_ALLOWED_MIME_TYPES` is no longer used. Remove it from your
configuration; the server ignores it and logs a warning at startup if it is set.

## Media upload

`create_media` uploads a photo, document scan or PDF from a folder on the
server and shows it on the records named in `attachTo`; `update_media` takes
`filePath` to replace a record's file. Uploading needs media file access
(`GRAMPS_MEDIA_RESOURCES_ENABLED=true`) and at least one folder in
`GRAMPS_MEDIA_UPLOAD_DIRS`; until both are set, `create_media` is hidden. Only
files inside those folders can be read, symbolic links included,
and only JPEG, PNG, GIF, WebP, TIFF, BMP and PDF files, judged by content. A
file the tree already stores is reused, not uploaded twice. In Docker, mount
the folder into the container and set the path as seen inside it.

| Variable | Description | Default |
|----------|-------------|---------|
| `GRAMPS_MEDIA_UPLOAD_DIRS` | Absolute folders, separated by `:` (`;` on Windows), that `create_media` and `update_media` may upload from; a leading `~` or `${HOME}` stands for the home folder | unset (the Claude Desktop extension: `~/Downloads`) |
| `GRAMPS_MEDIA_UPLOAD_MAX_BYTES` | Largest file `create_media` or `update_media` uploads | `52428800` |

## Transports

| Variable | Description | Default |
|----------|-------------|---------|
| `MCP_TRANSPORT` | `stdio`: JSON-RPC over stdin/stdout, for local clients. `http`: Streamable HTTP at `MCP_PATH`. `sse`: legacy MCP SSE, `GET {MCP_PATH}/sse` and `POST {MCP_PATH}/message`; stateful, for older clients only | `stdio` (the Docker image: `http`) |
| `ASPNETCORE_URLS` | Listen URLs for HTTP/SSE, such as `http://127.0.0.1:8080` | `http://localhost:5000` (the Docker image: `http://0.0.0.0:8080`) |
| `MCP_PATH` | URL prefix for MCP endpoints | `/mcp` |
| `MCP_STATELESS` | Stateless mode for Streamable HTTP | `true` |
| `MCP_ENABLE_LEGACY_SSE` | Expose legacy `/sse` with `http` transport | `false` |
| `MCP_API_KEY` | Shared secret for HTTP/SSE transport (comma-separated for rotation; min 16 characters); see [HTTP authentication](#http-authentication) | — |
| `MCP_ALLOWED_ORIGINS` | Browser origins allowed to call HTTP/SSE endpoints, comma-separated; requests from other origins get 403; see [Browser origins](#browser-origins) | — |

With `http`, responses stream over SSE; the
[Streamable HTTP spec](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports#streamable-http)
has the protocol details.

## HTTP authentication

When `MCP_API_KEY` is set, all MCP HTTP/SSE endpoints require the key on every
request. `GET /health` stays anonymous for Docker and load-balancer probes.

Generate a key:

```bash
openssl rand -base64 32
```

Without a key, the server still starts (backward compatible). If the listen
address is not loopback-only, a warning is logged recommending that you set
`MCP_API_KEY`, use a reverse proxy with its own authentication, or bind to
`127.0.0.1` for local use only.

Inside Docker, `ASPNETCORE_URLS` is typically `http://0.0.0.0:8080`, so the
warning appears even when the host publishes the port on `127.0.0.1` only.
That is expected when external access is already restricted.

## Browser origins

Browsers send an `Origin` header with their requests, and the server refuses
with 403 every request whose origin is not listed in `MCP_ALLOWED_ORIGINS`.
This keeps a web page on another site from calling tools with the server's
Gramps credentials, also through DNS rebinding, which works even when the
server listens on `127.0.0.1` only. MCP clients outside a browser, such as
Claude Desktop, Claude Code and SDK clients, send no `Origin` and need no
setting. For a browser-based MCP client you trust, list its origin:

```bash
MCP_ALLOWED_ORIGINS=https://app.example.com
```

[SECURITY.md](../../SECURITY.md) also describes the optional `AllowedHosts` check of
the `Host` header.

## Logging

| Variable | Description | Default |
|----------|-------------|---------|
| `GRAMPS_LOG_LEVEL` | Log level for the server's own messages: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None` (case-insensitive); an unknown value means `Information` | `Information` |

Logs go to stderr. Claude Desktop saves them to a local file
(`~/Library/Logs/Claude/mcp-server-Gramps Web MCP.log` on macOS).

At `Information`, each Gramps API call is logged as its method, path (without
the query string), status, duration, and body length; media downloads log the
content type and length instead of the body length. Request and response
bodies and full URLs hold genealogy data (names, dates, search terms), so they
are logged only at `Debug` or `Trace`. Fields that look like credentials are
redacted even then. Use `Debug` only while troubleshooting, and delete the log
file afterwards.
