# gramps-web-mcp

[![License: AGPL v3](https://img.shields.io/badge/License-AGPL%20v3-blue.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/download/dotnet/8.0)

Companion MCP server for the [Gramps Web](https://www.grampsweb.org/)
open-source genealogy platform. It gives AI agents structured, tool-based
access to family trees through the Model Context Protocol.

This project is **not** a standalone genealogy UI or replacement for Gramps
Web. Run it alongside an existing Gramps Web instance; your users, trees,
media, permissions, and genealogy editing UI stay in Gramps Web.

## Features

- **57 MCP tools** — read, create, update, and delete people, families, events, places,
  sources, citations, notes, media, repositories, and tags
- **Search and browse** — full-text search and paginated object listing
- **Kinship tools** — ancestors, descendants, relationships, and timelines
- **Composite workflows** — quick-add person, add event to person, find by Gramps ID
- **6 MCP resources** — type vocabularies, input guide, tree metadata, name
  settings, and opt-in media thumbnails/files for vision-capable agents
- **Media safeguards** — size limits, MIME allowlists, and private-record defaults
- **MCP prompts** — guided workflows for research, adding people/families, and imports
- **Multiple transports** — stdio (local clients), Streamable HTTP, legacy SSE
- **Read-only mode** — keep all tools visible while blocking create, update, and delete calls

See the [tool catalog](GrampsWeb.Mcp/docs/TOOL_CATALOG.md) for the full list.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (for local development)
- A running [Gramps Web](https://www.grampsweb.org/) instance with API access
- Docker (optional, for container deployment)

## Quick start

### Local development (demo server)

`run-local-server.sh` connects to the public [demo.grampsweb.org](https://demo.grampsweb.org)
instance using well-known demo credentials (`owner` / `owner`):

```bash
./run-local-server.sh
```

The server starts with HTTP transport at `http://127.0.0.1:8080/mcp`.

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
  ghcr.io/scormave/gramps-web-mcp:latest
```

The image exposes a **`GET /health`** endpoint for Docker `HEALTHCHECK`, Unraid
container health, and other uptime monitors. It returns HTTP 200 when the MCP
server can authenticate against Gramps Web, or HTTP 503 otherwise. Startup logs
include a line such as `Connected to Gramps Web at …` once the API is reachable.

For read-only mode, add `-e GRAMPS_READ_ONLY=true`:

```bash
docker run -p 8080:8080 \
  -e GRAMPS_API_URL=https://your-gramps.example.com \
  -e GRAMPS_USERNAME=your-user \
  -e GRAMPS_PASSWORD=your-password \
  -e GRAMPS_TREE_ID=your-tree-uuid \
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
3. Set the Gramps Web connection values:
   `GRAMPS_API_URL`, `GRAMPS_USERNAME`, `GRAMPS_PASSWORD`, and
   `GRAMPS_TREE_ID`.
4. Keep the default container port `8080`, or map it to another host port.
5. Start the container and check `/health`; it returns HTTP 200 once the service
   can authenticate to Gramps Web.

For the easiest pairing, run Gramps Web and `gramps-web-mcp` on the same Unraid
Docker network and set `GRAMPS_API_URL` to the Gramps Web container URL. The MCP
endpoint for clients is `http://<unraid-host>:<mapped-port>/mcp`.

### Gramps Web + MCP (Docker Compose)

To run Gramps Web and the MCP server on the same host and Docker network, use
[`docker-compose.example.yml`](docker-compose.example.yml) as a starting point:

```bash
cp docker-compose.example.yml docker-compose.yml
cp .env.example .env
# Complete the Gramps Web setup wizard, then set credentials in .env
docker compose up -d
```

Gramps Web is published on port **5055**; MCP is on **8080** (`/mcp` and
`/health`). Inside the compose network the MCP container reaches Gramps Web at
`http://grampsweb:5000`.

### MCP client configuration

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

To run a stdio server in read-only mode, add `"GRAMPS_READ_ONLY": "true"` to `env`.

**HTTP** (remote / Docker):

Point your MCP client at `http://host:8080/mcp` with Streamable HTTP transport.

Vision-capable agents can read opt-in media through tools (`GetMediaThumbnail`,
`GetMediaFile`) for clients such as Open WebUI, or through binary MCP resources
such as `gramps://media/{handle}/thumbnail/{size}` and
`gramps://media/{handle}/file` in clients with resource support. End-to-end
image/document analysis depends on the MCP client forwarding image or binary
content to a model with vision support.

## Configuration

### Required (Gramps connection)

| Variable | Description |
|----------|-------------|
| `GRAMPS_API_URL` | Base URL of your Gramps Web instance (no trailing slash) |
| `GRAMPS_USERNAME` | API user name |
| `GRAMPS_PASSWORD` | API password or token |
| `GRAMPS_TREE_ID` | Tree UUID on that server |

### Runtime mode

| Variable | Behavior | Default |
|---------------------|----------|---------|
| `GRAMPS_READ_ONLY=true` | Blocks create, update, and delete mutation calls while keeping tools visible | read/write |

### Media file access

Media byte tools/resources are disabled by default. `get_media` remains
available for metadata without enabling file downloads.

| Variable | Description | Default |
|----------|-------------|---------|
| `GRAMPS_MEDIA_RESOURCES_ENABLED` | Enables binary media tools/resources for thumbnails and full files | `false` |
| `GRAMPS_MEDIA_MAX_BYTES` | Maximum bytes returned by any media resource | `5242880` |
| `GRAMPS_MEDIA_ALLOWED_MIME_TYPES` | Comma-separated allowlist; exact types and `type/*` wildcards are supported | `image/jpeg,image/png,image/webp,image/avif,application/pdf` |
| `GRAMPS_MEDIA_ALLOW_PRIVATE` | Allows bytes for Gramps media records marked private | `false` |

Prefer `GetMediaThumbnail` or `gramps://media/{handle}/thumbnail/{size}` for AI
analysis. Full files can be large and sensitive, and are still subject to the
same size, MIME, and private-record checks.

### Transports

Set `GRAMPS_API_URL`, `GRAMPS_USERNAME`, `GRAMPS_PASSWORD`, and `GRAMPS_TREE_ID` as usual.

| `MCP_TRANSPORT` | Behavior |
|-----------------|----------|
| *(unset or `stdio`)* | JSON-RPC over stdin/stdout (default; local clients). |
| `http` | [Streamable HTTP](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports#streamable-http) at `MCP_PATH` (default `/mcp`). Responses stream over SSE. Set `ASPNETCORE_URLS` (e.g. `http://127.0.0.1:8080`). |
| `sse` | Legacy MCP SSE: `GET {MCP_PATH}/sse` + `POST {MCP_PATH}/message`. Stateful; use for older clients only. |

### Optional (MCP transport)

| Variable | Description | Default |
|----------|-------------|---------|
| `ASPNETCORE_URLS` | Listen URLs for HTTP/SSE | — |
| `MCP_PATH` | URL prefix for MCP endpoints | `/mcp` |
| `MCP_STATELESS` | Stateless mode for Streamable HTTP | `true` |
| `MCP_ENABLE_LEGACY_SSE` | Expose legacy `/sse` with `http` transport | `false` |

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
| [System prompt](GrampsWeb.Mcp/docs/SYSTEM_PROMPT.md) | Suggested prompt for MCP clients |
| [Architecture](GrampsWeb.Mcp/docs/ARCHITECTURE.md) | System design overview |

## Contributing

Contributions are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

## Security

To report a vulnerability, see [SECURITY.md](SECURITY.md).

## License

Copyright (c) Scormave

This project is licensed under the [GNU Affero General Public License v3.0](LICENSE)
(AGPL-3.0-or-later). Because this is network server software, hosting a modified
version requires making the corresponding source available to users interacting
with it over a network.
