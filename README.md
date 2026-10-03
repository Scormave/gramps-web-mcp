# gramps-web-mcp

[![License: AGPL v3](https://img.shields.io/badge/License-AGPL%20v3-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![MCP server version](https://img.shields.io/github/v/release/Scormave/gramps-web-mcp?label=MCP%20server&color=14b8a6)](https://github.com/Scormave/gramps-web-mcp/releases)
[![CI](https://github.com/Scormave/gramps-web-mcp/actions/workflows/ci.yml/badge.svg)](https://github.com/Scormave/gramps-web-mcp/actions/workflows/ci.yml)
[![Docker on GHCR](https://img.shields.io/badge/Docker-GHCR-2496ED?logo=docker&logoColor=white)](https://github.com/Scormave/gramps-web-mcp/pkgs/container/gramps-web-mcp)

Companion MCP server for the [Gramps Web](https://www.grampsweb.org/)
open-source genealogy platform. It gives AI agents structured, tool-based
access to family trees through the Model Context Protocol.

This project is **not** a standalone genealogy UI or replacement for Gramps
Web. Run it alongside an existing Gramps Web instance; your users, trees,
media, permissions, and genealogy editing UI stay in Gramps Web.

## Features

- **Up to 31 MCP tools** — read, create, update, and delete people, families, events, places,
  sources, citations, notes, media, repositories, and tags
- **Search and browse** — full-text search and paginated object listing
- **Kinship tools** — ancestors, descendants, relationships, and timelines
- **Composite workflows** — quick-add person and add event to person
- **Dates as Gramps writes them** — ranges, before/after/about, and every Gramps
  calendar (`1856-07-20 (Julian)`), stored with the sort value Gramps computes
- **6 MCP resources** — type vocabularies, input guide, tree metadata, name
  settings, and opt-in media previews/files for vision-capable agents
- **Media safeguards** — size limits, private-record defaults, and previews with
  EXIF/GPS metadata stripped
- **MCP prompts** — guided workflows for research, finding connections, adding
  people/families, imports, changing links, and citing facts
- **Multiple transports** — stdio (local clients), Streamable HTTP, legacy SSE
- **Read-only mode** — publish only read tools and block direct mutation calls

See the [tool catalog](GrampsWeb.Mcp/docs/TOOL_CATALOG.md) for the full list.
Upgrading from 1.x? Follow the [2.0 migration guide](GrampsWeb.Mcp/docs/MIGRATING_TO_2.md)
to update saved tool calls and client settings.

All outgoing requests to Gramps Web, including authentication, media downloads,
and health checks, automatically identify this application with
`User-Agent: gramps-web-mcp/<version> (+https://github.com/Scormave/gramps-web-mcp)`.
The version comes from the application build; no configuration is required.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (for local development)
- A running [Gramps Web](https://www.grampsweb.org/) instance with API access
- Docker (optional, for container deployment)

## Quick start

### Local development (demo server)

`run-local-server.sh` connects to the public [demo.grampsweb.org](https://demo.grampsweb.org)
instance using well-known demo credentials (`owner` / `owner`):

```bash
./run-local-server.sh
```

The server starts with HTTP transport at `http://127.0.0.1:8080/mcp`. No API key is
required when binding to loopback only.

### Docker

Pre-built multi-arch images (`linux/amd64`, `linux/arm64`) are published to
GitHub Container Registry. Docker picks the matching architecture automatically;
`amd64` covers most Unraid and x86 hosts, `arm64` covers Apple Silicon and ARM
SBCs:

```bash
docker pull ghcr.io/scormave/gramps-web-mcp:latest

docker run -p 8080:8080 \
  -e GRAMPS_API_URL=https://your-gramps.example.com \
  -e GRAMPS_USERNAME=your-user \
  -e GRAMPS_PASSWORD=your-password \
  -e GRAMPS_TREE_ID=your-tree-uuid \
  -e MCP_API_KEY=your-secret-api-key \
  ghcr.io/scormave/gramps-web-mcp:latest
```

For refresh-token authentication, replace the `GRAMPS_USERNAME` and
`GRAMPS_PASSWORD` lines with `-e GRAMPS_REFRESH_TOKEN=your-refresh-token`.

The image exposes a **`GET /health`** endpoint for Docker `HEALTHCHECK`, Unraid
container health, and other uptime monitors. It returns HTTP 200 when the MCP
server can authenticate against Gramps Web, or HTTP 503 otherwise. Probes reuse
the token the tools use, so they do not log in to Gramps Web each time. The
public response is minimal: `{ "status": "healthy" }` or
`{ "status": "unhealthy" }`. Startup logs include a line such as
`Connected to Gramps Web at …` once the API is reachable.

The image defaults to Streamable HTTP (`MCP_TRANSPORT=http`) on port 8080, which
is what the commands above use. Clients that spawn the container themselves
(such as MCP Registry installs) instead run it over stdio with
`-e MCP_TRANSPORT=stdio` and stdin kept open (`docker run -i`); that is the mode
declared in `server.json`.

For read-only mode, add `-e GRAMPS_READ_ONLY=true`:

```bash
docker run -p 8080:8080 \
  -e GRAMPS_API_URL=https://your-gramps.example.com \
  -e GRAMPS_USERNAME=your-user \
  -e GRAMPS_PASSWORD=your-password \
  -e GRAMPS_TREE_ID=your-tree-uuid \
  -e MCP_API_KEY=your-secret-api-key \
  -e GRAMPS_READ_ONLY=true \
  ghcr.io/scormave/gramps-web-mcp:latest
```

### Unraid installation

Unraid users can install `gramps-web-mcp` from **Community Applications**. The
template source is maintained at
[Scormave/gramps-web-mcp-unraid](https://github.com/Scormave/gramps-web-mcp-unraid).
For Unraid-specific help, see the
[support thread on the Unraid forums](https://forums.unraid.net/topic/199622-support-gramps-web-mcp-mcp-server-for-gramps-web-ai-genealogy).

Basic setup:

1. In Unraid, open **Apps** / **Community Applications**.
2. Search for `gramps-web-mcp` and install the template.
3. Set `GRAMPS_API_URL`, `GRAMPS_TREE_ID`, and either `GRAMPS_USERNAME` plus
   `GRAMPS_PASSWORD` or `GRAMPS_REFRESH_TOKEN` for your Gramps Web instance.
   Set `MCP_API_KEY` when the MCP port is reachable from other machines on your
   network.
4. Keep the default container port `8080`, or map it to another host port.
5. Start the container and check `/health`; it returns HTTP 200 once the service
   can authenticate to Gramps Web, with a minimal JSON response.

For the easiest pairing, run Gramps Web and `gramps-web-mcp` on the same Unraid
Docker network and set `GRAMPS_API_URL` to the Gramps Web container URL. The MCP
endpoint for clients is `http://<unraid-host>:<mapped-port>/mcp`.

### Gramps Web + MCP (Docker Compose)

To run Gramps Web and the MCP server on the same host and Docker network, use
[`docker-compose.example.yml`](docker-compose.example.yml) as a starting point:

```bash
cp docker-compose.example.yml docker-compose.yml
cp .env.example .env
# Complete the Gramps Web setup wizard, then set an authentication method in .env
docker compose up -d
```

Gramps Web is published on port **5055**; MCP is on **8080** (`/mcp` and
`/health`). Inside the compose network the MCP container reaches Gramps Web at
`http://grampsweb:5000`.

### Claude Desktop (MCPB extension)

One-click install for Claude Desktop is available as an **MCP Bundle** (`.mcpb`) from
[GitHub Releases](https://github.com/Scormave/gramps-web-mcp/releases). Download the
bundle for your platform:

| Platform | Artifact |
|----------|----------|
| macOS Apple Silicon | `gramps-web-mcp-claude-desktop-osx-arm64-v*.mcpb` |
| macOS Intel | `gramps-web-mcp-claude-desktop-osx-x64-v*.mcpb` |
| Windows x64 | `gramps-web-mcp-claude-desktop-win-x64-v*.mcpb` |
| Linux x64 | `gramps-web-mcp-claude-desktop-linux-x64-v*.mcpb` |
| Linux ARM64 | `gramps-web-mcp-claude-desktop-linux-arm64-v*.mcpb` |

1. Download the `.mcpb` file for your OS from the latest release.
2. Double-click it, or drag it into the Claude Desktop window.
3. Enter your Gramps Web URL, username, password/token, and tree UUID.
4. Leave **Read-only mode** enabled for your first session; disable it only when you
   want Claude to create or edit records.
5. Complete installation and start a new chat.

The extension runs locally over stdio and does not require the .NET SDK on your
machine.
See [`mcpb/README.md`](mcpb/README.md) for packaging details and
[`PRIVACY.md`](PRIVACY.md) for the privacy policy.

To build a bundle locally:

```bash
./scripts/pack-mcpb.sh osx-arm64   # or osx-x64, win-x64, linux-x64, linux-arm64
```

### MCP client configuration (manual)

**stdio** (e.g. Claude Desktop, Cursor):

```json
{
  "mcpServers": {
    "gramps-web": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/gramps-web-mcp/GrampsWeb.Mcp/GrampsWeb.Mcp.csproj"],
      "env": {
        "MCP_TRANSPORT": "stdio",
        "GRAMPS_API_URL": "https://your-gramps.example.com",
        "GRAMPS_USERNAME": "your-user",
        "GRAMPS_PASSWORD": "your-password",
        "GRAMPS_TREE_ID": "your-tree-uuid"
      }
    }
  }
}
```

To authenticate with a refresh token, omit `GRAMPS_USERNAME` and
`GRAMPS_PASSWORD` from `env` and add
`"GRAMPS_REFRESH_TOKEN": "your-refresh-token"` instead.

To run a stdio server in read-only mode, add `"GRAMPS_READ_ONLY": "true"` to `env`.

**HTTP** (remote / Docker):

Point your MCP client at `http://host:8080/mcp` with Streamable HTTP transport.
When `MCP_API_KEY` is set, send it as `Authorization: Bearer <key>` or
`X-Api-Key: <key>` on every MCP request.

```bash
curl -X POST http://host:8080/mcp \
  -H "Authorization: Bearer $MCP_API_KEY" \
  -H "Accept: application/json, text/event-stream" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"curl","version":"1.0"}},"id":1}'
```

Vision-capable agents can read opt-in media through `read_media`
or through binary MCP resources such as
`gramps://media/{handle}/thumbnail/{size}` and `gramps://media/{handle}/file`.
`read_media` defaults to a thumbnail: a JPEG preview (PNG when the image has
transparency) rendered by the server from the original, 1568 pixels on the long
edge so handwriting and small print in document scans stay legible. Pass `size`
(1–4096) for a smaller or larger preview; images are never upscaled. Previews are
re-encoded from pixels, so EXIF (including GPS), XMP, IPTC, and ICC metadata are
not sent. They can be rendered from JPEG, PNG, GIF, WebP, BMP, TIFF, TGA, PBM, and
QOI originals; multi-page files use the first page. PDF and other non-image
media, and AVIF, HEIC/HEIF, JPEG XL, and SVG images, have no preview. Use
`mode: "file"` for an original file and omit `size`.
File mode returns image, audio, or embedded blob resource content depending on
MIME type; images that MCP clients cannot display, such as TIFF, come back as
an embedded blob with a hint to use the thumbnail. End-to-end analysis depends
on the MCP client forwarding the typed tool content or binary resource content
to a capable model.

## Configuration

### Gramps connection

| Variable | Description |
|----------|-------------|
| `GRAMPS_API_URL` | Base URL of your Gramps Web instance (no trailing slash) |
| `GRAMPS_USERNAME` | API user name |
| `GRAMPS_PASSWORD` | API password |
| `GRAMPS_TREE_ID` | Tree UUID on that server |

### Refresh token instead of a password

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

### Runtime mode

| Variable | Default |
|----------|---------|
| `GRAMPS_READ_ONLY` | `false` |
| `GRAMPS_MUTATION_SERIALIZE` | `true` |
| `GRAMPS_MUTATION_MIN_INTERVAL_MS` | `0` |

- `GRAMPS_READ_ONLY`: set to `true` to publish only read tools and block direct
  create, update, and delete calls.
- `GRAMPS_MUTATION_SERIALIZE`: runs create/update/delete HTTP calls one at a
  time in this process. It also protects the complete read/modify/write sequence
  of update tools and `add_event_to_person` against other such calls in this process.
- `GRAMPS_MUTATION_MIN_INTERVAL_MS`: minimum pause between mutation HTTP calls,
  including steps inside composite tools.

Runtime notes:

- `GRAMPS_READ_ONLY=false` means the server starts in read/write mode.
- The Claude Desktop MCPB extension is the exception: its setup form defaults to
  read-only for safer first use.
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

### Updating linked records

The nine `update_*` tools that accept link lists have a `linkMode` argument:
`replace` (default), `add`, or `remove`. For example,
`update_person(handle: "PERSON_HANDLE", noteHandles: ["NOTE_HANDLE"], linkMode: "add")`
adds a note without copying the person's existing note list. `remove` takes the
handles to unlink. An omitted list remains unchanged; `[]` clears a list only in
`replace` mode. The mode applies to every supplied link list in the call.
Existing link metadata is preserved by `add`; use `replace` to change that
metadata. See the [tool catalog](GrampsWeb.Mcp/docs/TOOL_CATALOG.md#incremental-link-updates)
for supported lists and concurrency limits.

### Media file access

Media byte tools/resources are disabled by default. `get_object` with
`objectType: "media"` remains
available for metadata without enabling file downloads.
When disabled, `read_media` is omitted from
`tools/list`; direct calls by a client that already knows its name return a
configuration error without downloading bytes.

| Variable | Description | Default |
|----------|-------------|---------|
| `GRAMPS_MEDIA_RESOURCES_ENABLED` | Enables binary media tools/resources for thumbnails and full files | `false` |
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

### Transports

Set `GRAMPS_API_URL`, `GRAMPS_TREE_ID`, and either the username/password pair or
`GRAMPS_REFRESH_TOKEN` as described above.

| Value | Behavior |
|-------|----------|
| *(unset or `stdio`)* | JSON-RPC over stdin/stdout (default; local clients). |
| `http` | Streamable HTTP at `MCP_PATH` (default `/mcp`). |
| `sse` | Legacy MCP SSE: `GET {MCP_PATH}/sse` + `POST {MCP_PATH}/message`. Stateful; use for older clients only. |

For HTTP transport, responses stream over SSE. See the
[Streamable HTTP spec](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports#streamable-http)
for protocol details. Set `ASPNETCORE_URLS` to choose the listen address, for
example `http://127.0.0.1:8080`.

### Optional (MCP transport)

| Variable | Description | Default |
|----------|-------------|---------|
| `ASPNETCORE_URLS` | Listen URLs for HTTP/SSE | — |
| `MCP_PATH` | URL prefix for MCP endpoints | `/mcp` |
| `MCP_STATELESS` | Stateless mode for Streamable HTTP | `true` |
| `MCP_ENABLE_LEGACY_SSE` | Expose legacy `/sse` with `http` transport | `false` |
| `MCP_API_KEY` | Shared secret for HTTP/SSE transport (comma-separated for rotation; min 16 characters) | — |

### HTTP authentication

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

### Logging

| Variable | Description | Default |
|----------|-------------|---------|
| `GRAMPS_LOG_LEVEL` | Log level for the server's own messages: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None` | `Information` |

Logs go to stderr. Claude Desktop saves them to a local file
(`~/Library/Logs/Claude/mcp-server-Gramps Web MCP.log` on macOS).

At `Information`, each Gramps API call is logged as its method, path (without
the query string), status, duration, and body length; media downloads log the
content type and length instead of the body length. Request and response
bodies and full URLs hold genealogy data (names, dates, search terms), so they
are logged only at `Debug` or `Trace`. Fields that look like credentials are
redacted even then. Use `Debug` only while troubleshooting, and delete the log
file afterwards.

## Development

```bash
dotnet test
```

See [CONTRIBUTING.md](CONTRIBUTING.md) and the [developer guide](GrampsWeb.Mcp/docs/DEVELOPER_GUIDE.md).

## Documentation

| Document | Description |
|----------|-------------|
| [docs index](GrampsWeb.Mcp/docs/README.md) | All documentation files |
| [Tool catalog](GrampsWeb.Mcp/docs/TOOL_CATALOG.md) | Complete MCP tool reference |
| [Claude Desktop MCPB](mcpb/README.md) | Desktop extension packaging |
| [Privacy policy](PRIVACY.md) | Data handling for the desktop extension |
| [System prompt](GrampsWeb.Mcp/docs/SYSTEM_PROMPT.md) | Suggested prompt for MCP clients |
| [Architecture](GrampsWeb.Mcp/docs/ARCHITECTURE.md) | System design overview |

## Contributing

Contributions are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

## Security

To report a vulnerability, see [SECURITY.md](SECURITY.md).

## Privacy Policy

The Claude Desktop extension is a **local** MCP server. It sends data only to the
Gramps Web instance you configure and does not collect analytics or conversation
data. See [PRIVACY.md](PRIVACY.md) for full details.

## License

Copyright (c) Scormave

This project is licensed under the [GNU Affero General Public License v3.0](LICENSE)
(AGPL-3.0-or-later). Because this is network server software, hosting a modified
version requires making the corresponding source available to users interacting
with it over a network.

Bundled third-party components, such as ImageSharp, the MCP C# SDK, and the .NET
runtime, keep their own Apache 2.0 or MIT licenses; see
[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt). The MCP Bundles and the
Docker image ship that file next to `LICENSE`.
