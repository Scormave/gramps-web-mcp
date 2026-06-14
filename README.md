# gramps-web-mcp

[![License: AGPL v3](https://img.shields.io/badge/License-AGPL%20v3-blue.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/download/dotnet/8.0)

MCP server for the [Gramps Web](https://www.grampsweb.org/) open-source genealogy platform.
Gives AI agents structured, tool-based access to family trees through the Model Context Protocol.

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

Pre-built images are published to GitHub Container Registry:

```bash
docker pull ghcr.io/scormave/gramps-web-mcp:latest

docker run -p 8080:8080 \
  -e GRAMPS_API_URL=https://your-gramps.example.com \
  -e GRAMPS_USERNAME=your-user \
  -e GRAMPS_PASSWORD=your-password \
  -e GRAMPS_TREE_ID=your-tree-uuid \
  ghcr.io/scormave/gramps-web-mcp:latest
```

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
