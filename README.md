# gramps-web-mcp

[![License: AGPL v3](https://img.shields.io/badge/License-AGPL%20v3-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![MCP server version](https://img.shields.io/github/v/release/Scormave/gramps-web-mcp?label=MCP%20server&color=14b8a6)](https://github.com/Scormave/gramps-web-mcp/releases)
[![CI](https://github.com/Scormave/gramps-web-mcp/actions/workflows/ci.yml/badge.svg)](https://github.com/Scormave/gramps-web-mcp/actions/workflows/ci.yml)
[![Docker on GHCR](https://img.shields.io/badge/Docker-GHCR-2496ED?logo=docker&logoColor=white)](https://github.com/Scormave/gramps-web-mcp/pkgs/container/gramps-web-mcp)
[![Gramps Web MCP MCP server – quality and maintenance score on Glama](https://glama.ai/mcp/servers/Scormave/gramps-web-mcp/badges/score.svg)](https://glama.ai/mcp/servers/Scormave/gramps-web-mcp)

> [!WARNING]
> **Update to 2.3.1 or later.** Before 2.3.1 the `update_*` tools could drop
> stored data that the call did not mention
> ([#5](https://github.com/Scormave/gramps-web-mcp/issues/5)). If you edited a
> tree with an earlier version,
> [check it for dropped data](#data-dropped-by-updates-before-231).

Companion MCP server for the [Gramps Web](https://www.grampsweb.org/)
open-source genealogy platform. It gives AI agents structured, tool-based
access to family trees through the Model Context Protocol.

This project is **not** a standalone genealogy UI or replacement for Gramps
Web. Run it alongside an existing Gramps Web instance; your users, trees,
media, permissions, and genealogy editing UI stay in Gramps Web.

## Features

- **Up to 32 MCP tools** — read, create, update, and delete people, families, events, places,
  sources, citations, notes, media, repositories, and tags
- **Search and browse** — full-text search and paginated object listing,
  filtered by Gramps QL or by Gramps filter rules (everyone related to a
  person, people with no family links)
- **Kinship tools** — ancestors, descendants, relationships, and timelines
- **Incremental links** — add or remove one event, child, citation, or note
  with `linkMode`, keeping every other link
- **Undo** — revert one transaction of the edit history, deletions included,
  after a check that it would not overwrite later edits or break links
- **Confirmation dialogs** — `delete_object` and `undo_change` ask the user, not
  the model, before they change the tree, in clients that support MCP elicitation
- **Dates as Gramps writes them** — ranges, before/after/about, and every Gramps
  calendar (`1856-07-20 (Julian)`), stored with the sort value Gramps computes
- **8 MCP resources** — type vocabularies, input guide, tree metadata, name
  settings, filter rules, and opt-in media previews/files for vision-capable
  agents
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

The image is built on Ubuntu Chiseled and has no shell or `curl`. Its
`HEALTHCHECK` runs `dotnet /app/GrampsWeb.Mcp.dll --health-check`, which
requests `/health` on the port from `ASPNETCORE_URLS` and exits with 0 on
HTTP 200 and 1 otherwise. A health check you define yourself, in Compose or in
a NAS app template, runs the same command:

```yaml
healthcheck:
  test: ["CMD", "dotnet", "/app/GrampsWeb.Mcp.dll", "--health-check"]
```

If the server listens on an address that `ASPNETCORE_URLS` does not give, pass
the URL after the option, for example `--health-check http://localhost:9000`.

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
endpoint for clients is `http://<unraid-host>:<mapped-port>/mcp`; see
[Other MCP clients](#other-mcp-clients) to connect them.

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

### Other MCP clients

Other MCP clients, such as Claude Code, VS Code, Cursor, Codex, Antigravity CLI
and Open WebUI, connect in one of two ways:

- **Over HTTP** to a server that already runs in [Docker](#docker), on
  [Unraid](#unraid-installation) or with
  [Docker Compose](#gramps-web--mcp-docker-compose). Each client needs only the
  server's address and API key, nothing is installed on the client's machine,
  and all clients share the one server.
- **Over stdio**: the client starts the server in Docker for itself. Each
  machine needs Docker and the Gramps Web credentials.

Claude Desktop connects to servers added by URL from Anthropic's cloud, not
from your computer, so it cannot reach a server on your home network. Use the
[MCPB extension](#claude-desktop-mcpb-extension) there.

#### Over HTTP

Set `MCP_API_KEY` on the server (see [HTTP authentication](#http-authentication)),
then give each client the address and the key. The examples use
`http://192.168.1.10:8080/mcp` and `your-secret-api-key`; replace both with
your own.

**Claude Code:**

```bash
claude mcp add --transport http --scope user gramps-web http://192.168.1.10:8080/mcp \
  --header "Authorization: Bearer your-secret-api-key"
```

**VS Code**, in the user `mcp.json` or a workspace's `.vscode/mcp.json`. VS Code
asks for the key when the server starts for the first time:

```json
{
  "inputs": [
    {
      "type": "promptString",
      "id": "gramps-web-mcp-key",
      "description": "Gramps Web MCP API key",
      "password": true
    }
  ],
  "servers": {
    "gramps-web": {
      "type": "http",
      "url": "http://192.168.1.10:8080/mcp",
      "headers": { "Authorization": "Bearer ${input:gramps-web-mcp-key}" }
    }
  }
}
```

**Cursor**, in `~/.cursor/mcp.json`:

```json
{
  "mcpServers": {
    "gramps-web": {
      "url": "http://192.168.1.10:8080/mcp",
      "headers": { "Authorization": "Bearer your-secret-api-key" }
    }
  }
}
```

**Codex**, in `~/.codex/config.toml`. Codex reads the key from the environment
variable that `bearer_token_env_var` names, so set `GRAMPS_WEB_MCP_KEY` before
you start it:

```toml
[mcp_servers.gramps-web]
url = "http://192.168.1.10:8080/mcp"
bearer_token_env_var = "GRAMPS_WEB_MCP_KEY"
```

**Antigravity CLI**, in `~/.gemini/config/mcp_config.json`. Gemini CLI takes
the same entry in `~/.gemini/settings.json`, with `httpUrl` in place of
`serverUrl`:

```json
{
  "mcpServers": {
    "gramps-web": {
      "serverUrl": "http://192.168.1.10:8080/mcp",
      "headers": { "Authorization": "Bearer your-secret-api-key" }
    }
  }
}
```

**Open WebUI** 0.6.31 or later: an administrator opens **Settings → Admin →
Integrations**, and under **External Tool Servers** adds a connection of type
**MCP (Streamable HTTP)** with the server URL, auth **Bearer** and the API key
in **Key**. Open WebUI connects from its own container, where `localhost` is
that container: use the host's address, or the MCP container's name when both
share a Docker network.

Keep in mind:

- Plain HTTP carries the key and the tree data unencrypted. Keep the port on a
  network you trust, and from elsewhere connect to that network through a VPN
  rather than publishing the port on the internet.
- The Cursor and Antigravity CLI files hold the key itself, so keep them out of
  a project's repository.
- Every client works as the Gramps Web account the server is configured with,
  and Gramps Web records all their edits under that account.
- Over stateless HTTP, the default, `delete_object` and `undo_change` cannot
  show their confirmation dialog; see
  [Confirming deletes and undos](#confirming-deletes-and-undos).

To check the address and the key from a terminal, send an `initialize` request.
The key also works as `X-Api-Key: <key>`:

```bash
curl -X POST http://192.168.1.10:8080/mcp \
  -H "Authorization: Bearer $MCP_API_KEY" \
  -H "Accept: application/json, text/event-stream" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"curl","version":"1.0"}},"id":1}'
```

#### Over stdio in Docker

The client starts a container for each session and removes it when the session
ends. `MCP_TRANSPORT=stdio` is required because the image defaults to HTTP, and
`-e NAME` without a value hands the container the variable from `env`, so the
password stays off the `docker` command line:

```json
{
  "mcpServers": {
    "gramps-web": {
      "command": "docker",
      "args": [
        "run", "-i", "--rm",
        "-e", "MCP_TRANSPORT=stdio",
        "-e", "GRAMPS_API_URL",
        "-e", "GRAMPS_USERNAME",
        "-e", "GRAMPS_PASSWORD",
        "-e", "GRAMPS_TREE_ID",
        "ghcr.io/scormave/gramps-web-mcp:latest"
      ],
      "env": {
        "GRAMPS_API_URL": "https://your-gramps.example.com",
        "GRAMPS_USERNAME": "your-user",
        "GRAMPS_PASSWORD": "your-password",
        "GRAMPS_TREE_ID": "your-tree-uuid"
      }
    }
  }
}
```

- Cursor (`~/.cursor/mcp.json`), Antigravity CLI
  (`~/.gemini/config/mcp_config.json`) and Gemini CLI
  (`~/.gemini/settings.json`) take this entry as it is.
- Claude Code: pass the `gramps-web` object to
  `claude mcp add-json --scope user gramps-web '<object>'`.
- VS Code: put the entry under `servers` instead of `mcpServers` and add
  `"type": "stdio"`.
- Codex: put the same `command`, `args` and `env` under
  `[mcp_servers.gramps-web]` in `~/.codex/config.toml`.
- Open WebUI connects over HTTP only.

Before the first start:

- Pull the image once with `docker pull ghcr.io/scormave/gramps-web-mcp:latest`.
  Some clients give a server only a few seconds to start, too few for the
  download.
- Inside the container `localhost` is the container itself. If Gramps Web runs
  on the same computer, use `host.docker.internal` in `GRAMPS_API_URL`; on
  Linux, also add `"--add-host=host.docker.internal:host-gateway"` to `args`.
- For a refresh token, replace `GRAMPS_USERNAME` and `GRAMPS_PASSWORD` with
  `GRAMPS_REFRESH_TOKEN` in both lists. For read-only mode, add
  `"-e", "GRAMPS_READ_ONLY=true"` before the image name; other
  [settings](#configuration) go the same way.
- [Uploads](#media-upload) read files inside the container, so mount the
  upload folder with `-v`.

To run the server from a source checkout instead, see
[Local development](GrampsWeb.Mcp/docs/DEVELOPER_GUIDE.md#local-development) in
the developer guide.

#### Images for vision-capable agents

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
  of update tools against other such calls in this process.
- `GRAMPS_MUTATION_MIN_INTERVAL_MS`: minimum pause between mutation HTTP calls.

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

### Confirming deletes and undos

`delete_object` and `undo_change` ask the user to confirm in a dialog before
they change the tree, when the client supports
[MCP elicitation](https://modelcontextprotocol.io/specification/2025-11-25/client/elicitation).
The dialog names the record, or lists what the undo will change, and the user
ticks a box to go ahead. Declining or closing it changes nothing. Dry runs and
refusals do not ask.

| Variable | Description | Default |
|----------|-------------|---------|
| `GRAMPS_CONFIRMATION` | `when-supported` asks clients that support elicitation and runs as before in the others; `required` refuses deletes and undos in clients that cannot ask; `off` never asks | `when-supported` |

- Stateless Streamable HTTP (`MCP_STATELESS=true`, the default) cannot send
  requests to the client, so it never asks, and `required` refuses every delete
  and undo there. Set `MCP_STATELESS=false` to confirm over HTTP; stdio and
  legacy SSE keep a session and can ask.
- Both tools also carry the `anthropic/requiresUserInteraction` tool metadata,
  whatever the setting. Clients that honour it, such as Claude Code, ask their
  own permission first, so the user may confirm twice; `off` keeps only the
  client's prompt.
- The tool call lasts until the user answers. If the client cancels the call
  first, nothing changes.
- The server logs once per client whether it asks.

See the [tool catalog](GrampsWeb.Mcp/docs/TOOL_CATALOG.md#confirmation-dialogs)
for the dialog and the replies.

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

### Data dropped by updates before 2.3.1

Before 2.3.1 the `update_*` tools could drop stored data the call did not
mention ([#5](https://github.com/Scormave/gramps-web-mcp/issues/5)). If you
edited a tree with an earlier version,
[`scripts/find-update-losses.py`](scripts/find-update-losses.py) reads the
Gramps Web transaction history and lists every value an update emptied, with
the value it had and whether the record still lacks it:

```bash
GRAMPS_API_URL=https://gramps.example.com GRAMPS_USERNAME=me GRAMPS_PASSWORD=... \
  python3 scripts/find-update-losses.py --user MCP_ACCOUNT --until 2026-10-07 --json losses.json
```

It needs Python 3.9 or later and an account that may see private records, and
it only reads. The history also holds edits made in the Gramps Web interface,
and a value cleared on purpose looks the same as a dropped one, so check each
finding before you restore it.

### Media file access

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

### Media upload

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
| `GRAMPS_MEDIA_UPLOAD_DIRS` | Absolute folders, separated by `:` (`;` on Windows), that `create_media` and `update_media` may upload from | unset (off) |
| `GRAMPS_MEDIA_UPLOAD_MAX_BYTES` | Largest file `create_media` or `update_media` uploads | `52428800` |

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
| `MCP_ALLOWED_ORIGINS` | Browser origins allowed to call HTTP/SSE endpoints, comma-separated; requests from other origins get 403 | — |

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

### Browser origins

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

[SECURITY.md](SECURITY.md) also describes the optional `AllowedHosts` check of
the `Host` header.

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
