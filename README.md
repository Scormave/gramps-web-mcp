# gramps-web-mcp

[![License: AGPL v3](https://img.shields.io/badge/License-AGPL%20v3-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![MCP server version](https://img.shields.io/github/v/release/Scormave/gramps-web-mcp?label=MCP%20server&color=14b8a6)](https://github.com/Scormave/gramps-web-mcp/releases)
[![CI](https://github.com/Scormave/gramps-web-mcp/actions/workflows/ci.yml/badge.svg)](https://github.com/Scormave/gramps-web-mcp/actions/workflows/ci.yml)
[![Docker on GHCR](https://img.shields.io/badge/Docker-GHCR-2496ED?logo=docker&logoColor=white)](https://github.com/Scormave/gramps-web-mcp/pkgs/container/gramps-web-mcp)
[![Gramps Web MCP MCP server – quality and maintenance score on Glama](https://glama.ai/mcp/servers/Scormave/gramps-web-mcp/badges/score.svg)](https://glama.ai/mcp/servers/Scormave/gramps-web-mcp)
[![Sponsor on GitHub](https://img.shields.io/badge/Sponsor-GitHub-db61a2?logo=githubsponsors&logoColor=white)](https://github.com/sponsors/Scormave)

> [!WARNING]
> **Update to 2.3.1 or later.** Before 2.3.1 the `update_*` tools could drop
> stored data that the call did not mention
> ([#5](https://github.com/Scormave/gramps-web-mcp/issues/5)). If you edited a
> tree with an earlier version,
> [check it for dropped data](GrampsWeb.Mcp/docs/INSTALLATION.md#data-dropped-by-updates-before-231).

Companion MCP server for the [Gramps Web](https://www.grampsweb.org/)
open-source genealogy platform. It lets AI agents read and edit your family
tree through the Model Context Protocol. It is **not** a genealogy UI: run it
alongside an existing Gramps Web instance, where your users, trees, media and
permissions stay.

[![Install in VS Code](https://img.shields.io/badge/VS_Code-Install_Server-0098FF)][install-vscode]
[![Install in VS Code Insiders](https://img.shields.io/badge/VS_Code_Insiders-Install_Server-24bfa5)][install-vscode-insiders]
[![Add to Cursor](https://img.shields.io/badge/Cursor-Install_Server-000000)][install-cursor]

The buttons add the server to VS Code or Cursor, which start it from the
Docker image, so Docker must be installed. For Claude Desktop, install the
[MCPB extension](#claude-desktop); other clients are listed under
[Install](#install).

## Features

- **Up to 26 MCP tools** — read, create, update, merge, and delete people,
  families, events, places, sources, citations, notes, media, repositories,
  and tags
- **Search and kinship** — full-text search, listings filtered by Gramps QL or
  Gramps filter rules, ancestors, descendants, relationships, and timelines
- **Careful edits** — add or remove one link with `linkMode` and keep the
  rest; save a whole register entry in one transaction with `create_records`;
  dates in every Gramps form and calendar
- **Duplicates** — `find_duplicates` pairs people entered twice, across
  spellings and name forms, and `create_records` refuses a person the tree
  already has
- **Undo and confirmation** — `undo_change` reverts one transaction of the
  edit history; it, `delete_records` and `merge_records` ask the user, not
  the model, before they change the tree, in clients that support MCP
  elicitation
- **Scans and photos** — opt-in previews that vision-capable agents can read,
  with EXIF/GPS metadata stripped, and uploads from a folder you choose
- **8 MCP resources and guided prompts** — type vocabularies, tree metadata,
  filter rules, and workflows for research, imports, citing facts, and
  tree audits
- **Read-only mode** — publish only read tools and block direct mutation calls
- **Any MCP client** — stdio, Streamable HTTP, and legacy SSE, from a Claude
  Desktop extension, a Docker image, or a single-file release binary

The [tool catalog](GrampsWeb.Mcp/docs/TOOL_CATALOG.md) describes every tool,
resource and prompt. Upgrading from 1.x? Follow the
[2.0 migration guide](GrampsWeb.Mcp/docs/MIGRATING_TO_2.md).

## What to ask

Ask in your own words; the agent chooses the tools. The names stand for people
in your tree, and a Gramps ID such as `I0012` works as well.

| You ask | You get |
|---------|---------|
| "Tell me about Maria García." | Her names, events with dates and places, parents, spouses and children, citations and notes |
| "Who were Kenji Tanaka's great-grandparents?" | Each ancestor with a kinship label such as Father's mother, and birth and death dates and places |
| "How is Ivan Petrov related to Maria García?" | The relationship in words, such as second cousin once removed, and the common ancestors by name |
| "Transcribe the baptism scan O0012." | The scan for the agent to read, and the transcription in its reply |
| "Add Thomas Weber, born 3 May 1881 in Hamburg, son of Karl and Anna Weber." | The new records with their Gramps IDs, people already in the tree reused, and what the text left open |
| "Enter entry 12 of the 1884 marriage register: the couple, their fathers and the witnesses." | The citation, the marriage, the new people and families saved in one transaction, which one `undo_change` takes back |
| "What changed today? Undo the last edit." | The edit history, then the earlier versions back, or a refusal naming the later edits the undo would overwrite |

Reading scans needs [media access](GrampsWeb.Mcp/docs/CONFIGURATION.md#media-file-access).
The [tool catalog](GrampsWeb.Mcp/docs/TOOL_CATALOG.md#what-to-ask) lists more
requests and the tools each one uses.

## Install

You need a running Gramps Web instance, version 26.6.2 or newer (Gramps Web API
3.16 or newer), and an account on it. Choose the setup by client:

| Client | Setup |
|--------|-------|
| Claude Desktop | The [MCPB extension](#claude-desktop), installed from one file; no Docker |
| VS Code, Cursor | The install buttons above; the client starts the Docker image |
| Claude Code, Codex, Gemini CLI, Antigravity CLI, Open WebUI and others | [Connecting MCP clients](GrampsWeb.Mcp/docs/CLIENTS.md): over HTTP to a shared server, or started by the client from the Docker image or the release binary |
| Several clients or machines | One server in [Docker](#docker), [Docker Compose](GrampsWeb.Mcp/docs/INSTALLATION.md#gramps-web--mcp-docker-compose) or on [Unraid](GrampsWeb.Mcp/docs/INSTALLATION.md#unraid), reached over HTTP |

### Claude Desktop

1. Download the `.mcpb` bundle for your platform from the
   [latest release](https://github.com/Scormave/gramps-web-mcp/releases/latest),
   such as `gramps-web-mcp-claude-desktop-osx-arm64-v*.mcpb` for Apple Silicon.
2. Double-click it, or drag it into the Claude Desktop window.
3. Enter your Gramps Web URL, username, password/token, and tree UUID.
4. Leave **Read-only mode** enabled for your first session; disable it only when you
   want Claude to create or edit records.
5. Complete installation and start a new chat.

Claude Desktop connects to servers added by URL from Anthropic's cloud, not
from your computer, so it cannot reach a server on your home network; use the
extension there. [Installation](GrampsWeb.Mcp/docs/INSTALLATION.md#claude-desktop-mcpb-extension)
lists the bundle for each platform.

### Docker

For one server that several clients share:

```bash
docker run -p 8080:8080 \
  -e GRAMPS_API_URL=https://your-gramps.example.com \
  -e GRAMPS_USERNAME=your-user \
  -e GRAMPS_PASSWORD=your-password \
  -e GRAMPS_TREE_ID=your-tree-uuid \
  -e MCP_API_KEY=your-secret-api-key \
  ghcr.io/scormave/gramps-web-mcp:latest
```

Clients then connect to `http://<host>:8080/mcp` with the API key, as shown
in [Over HTTP](GrampsWeb.Mcp/docs/CLIENTS.md#over-http). Add
`-e GRAMPS_READ_ONLY=true` for read-only mode.
[Installation](GrampsWeb.Mcp/docs/INSTALLATION.md) covers refresh tokens,
health checks, image verification, Docker Compose with Gramps Web, Unraid and
upgrades.

## Configuration

Every setting is an environment variable. The main ones:

| Variable | Description | Default |
|----------|-------------|---------|
| `GRAMPS_API_URL` | Base URL of your Gramps Web instance (no trailing slash) | — |
| `GRAMPS_USERNAME`, `GRAMPS_PASSWORD` | The Gramps Web account the server works as | — |
| `GRAMPS_REFRESH_TOKEN` | Used instead of the user name and password when Gramps Web has password login turned off | — |
| `GRAMPS_TREE_ID` | Tree UUID on that server | — |
| `GRAMPS_READ_ONLY` | `true` publishes only read tools and blocks writes | `false` (the Claude Desktop extension: `true`) |
| `GRAMPS_MEDIA_RESOURCES_ENABLED` | Lets agents read scans and photos, and enables uploads | `false` |
| `GRAMPS_MEDIA_UPLOAD_DIRS` | Folders that `upload_media` may upload from | unset (off) |
| `MCP_TRANSPORT` | `stdio`, `http` (Streamable HTTP) or `sse` | `stdio` (the Docker image: `http`) |
| `MCP_API_KEY` | Key that HTTP clients must send; set it whenever other machines can reach the port | — |

[Configuration](GrampsWeb.Mcp/docs/CONFIGURATION.md) describes every setting,
including write pacing for SQLite, confirmation dialogs, media limits,
transports, browser origins and logging.

## Documentation

| Document | Description |
|----------|-------------|
| [Installation](GrampsWeb.Mcp/docs/INSTALLATION.md) | Claude Desktop, Docker, Docker Compose, Unraid, health checks, and upgrades |
| [Connecting MCP clients](GrampsWeb.Mcp/docs/CLIENTS.md) | Claude Code, VS Code, Cursor, Codex, Gemini and Antigravity CLI, and Open WebUI, over HTTP or stdio |
| [Configuration](GrampsWeb.Mcp/docs/CONFIGURATION.md) | Every environment variable |
| [Tool catalog](GrampsWeb.Mcp/docs/TOOL_CATALOG.md) | Every tool, resource and prompt, and more example requests |
| [System prompt](GrampsWeb.Mcp/docs/SYSTEM_PROMPT.md) | Suggested prompt for MCP clients |
| [Migrating to 2.0](GrampsWeb.Mcp/docs/MIGRATING_TO_2.md) | Tool replacements and setup changes for 1.x users |
| [Claude Desktop MCPB](mcpb/README.md) | Desktop extension packaging |
| [Architecture](GrampsWeb.Mcp/docs/ARCHITECTURE.md) | System design overview |
| [Developer guide](GrampsWeb.Mcp/docs/DEVELOPER_GUIDE.md) | Running from source, tests, and adding tools |
| [docs index](GrampsWeb.Mcp/docs/README.md) | All documentation files |

## Contributing

Contributions are welcome. `./run-local-server.sh` runs the server from source
against the public [demo.grampsweb.org](https://demo.grampsweb.org) tree at
`http://127.0.0.1:8080/mcp`, and `dotnet test` runs the tests. See
[CONTRIBUTING.md](CONTRIBUTING.md) and the
[developer guide](GrampsWeb.Mcp/docs/DEVELOPER_GUIDE.md).

## Sponsors

gramps-web-mcp is free and open source. If it helps your research, you can
support its development through
[GitHub Sponsors](https://github.com/sponsors/Scormave), monthly or once.
Sponsors whose tier includes a mention are listed here.

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

[install-vscode]: https://insiders.vscode.dev/redirect/mcp/install?name=gramps-web&inputs=%5B%7B%22type%22%3A%22promptString%22%2C%22id%22%3A%22gramps-web-api-url%22%2C%22description%22%3A%22Gramps%20Web%20address%2C%20such%20as%20https%3A%2F%2Fyour-gramps.example.com%22%7D%2C%7B%22type%22%3A%22promptString%22%2C%22id%22%3A%22gramps-web-username%22%2C%22description%22%3A%22Gramps%20Web%20user%20name%22%7D%2C%7B%22type%22%3A%22promptString%22%2C%22id%22%3A%22gramps-web-password%22%2C%22description%22%3A%22Gramps%20Web%20password%22%2C%22password%22%3Atrue%7D%2C%7B%22type%22%3A%22promptString%22%2C%22id%22%3A%22gramps-web-tree-id%22%2C%22description%22%3A%22Gramps%20Web%20tree%20ID%22%7D%5D&config=%7B%22type%22%3A%22stdio%22%2C%22command%22%3A%22docker%22%2C%22args%22%3A%5B%22run%22%2C%22-i%22%2C%22--rm%22%2C%22-e%22%2C%22MCP_TRANSPORT%3Dstdio%22%2C%22-e%22%2C%22GRAMPS_API_URL%22%2C%22-e%22%2C%22GRAMPS_USERNAME%22%2C%22-e%22%2C%22GRAMPS_PASSWORD%22%2C%22-e%22%2C%22GRAMPS_TREE_ID%22%2C%22ghcr.io%2Fscormave%2Fgramps-web-mcp%3Alatest%22%5D%2C%22env%22%3A%7B%22GRAMPS_API_URL%22%3A%22%24%7Binput%3Agramps-web-api-url%7D%22%2C%22GRAMPS_USERNAME%22%3A%22%24%7Binput%3Agramps-web-username%7D%22%2C%22GRAMPS_PASSWORD%22%3A%22%24%7Binput%3Agramps-web-password%7D%22%2C%22GRAMPS_TREE_ID%22%3A%22%24%7Binput%3Agramps-web-tree-id%7D%22%7D%7D
[install-vscode-insiders]: https://insiders.vscode.dev/redirect/mcp/install?name=gramps-web&inputs=%5B%7B%22type%22%3A%22promptString%22%2C%22id%22%3A%22gramps-web-api-url%22%2C%22description%22%3A%22Gramps%20Web%20address%2C%20such%20as%20https%3A%2F%2Fyour-gramps.example.com%22%7D%2C%7B%22type%22%3A%22promptString%22%2C%22id%22%3A%22gramps-web-username%22%2C%22description%22%3A%22Gramps%20Web%20user%20name%22%7D%2C%7B%22type%22%3A%22promptString%22%2C%22id%22%3A%22gramps-web-password%22%2C%22description%22%3A%22Gramps%20Web%20password%22%2C%22password%22%3Atrue%7D%2C%7B%22type%22%3A%22promptString%22%2C%22id%22%3A%22gramps-web-tree-id%22%2C%22description%22%3A%22Gramps%20Web%20tree%20ID%22%7D%5D&config=%7B%22type%22%3A%22stdio%22%2C%22command%22%3A%22docker%22%2C%22args%22%3A%5B%22run%22%2C%22-i%22%2C%22--rm%22%2C%22-e%22%2C%22MCP_TRANSPORT%3Dstdio%22%2C%22-e%22%2C%22GRAMPS_API_URL%22%2C%22-e%22%2C%22GRAMPS_USERNAME%22%2C%22-e%22%2C%22GRAMPS_PASSWORD%22%2C%22-e%22%2C%22GRAMPS_TREE_ID%22%2C%22ghcr.io%2Fscormave%2Fgramps-web-mcp%3Alatest%22%5D%2C%22env%22%3A%7B%22GRAMPS_API_URL%22%3A%22%24%7Binput%3Agramps-web-api-url%7D%22%2C%22GRAMPS_USERNAME%22%3A%22%24%7Binput%3Agramps-web-username%7D%22%2C%22GRAMPS_PASSWORD%22%3A%22%24%7Binput%3Agramps-web-password%7D%22%2C%22GRAMPS_TREE_ID%22%3A%22%24%7Binput%3Agramps-web-tree-id%7D%22%7D%7D&quality=insiders
[install-cursor]: https://cursor.com/en/install-mcp?name=gramps-web&config=eyJjb21tYW5kIjoiZG9ja2VyIiwiYXJncyI6WyJydW4iLCItaSIsIi0tcm0iLCItZSIsIk1DUF9UUkFOU1BPUlQ9c3RkaW8iLCItZSIsIkdSQU1QU19BUElfVVJMIiwiLWUiLCJHUkFNUFNfVVNFUk5BTUUiLCItZSIsIkdSQU1QU19QQVNTV09SRCIsIi1lIiwiR1JBTVBTX1RSRUVfSUQiLCJnaGNyLmlvL3Njb3JtYXZlL2dyYW1wcy13ZWItbWNwOmxhdGVzdCJdLCJlbnYiOnsiR1JBTVBTX0FQSV9VUkwiOiJodHRwczovL3lvdXItZ3JhbXBzLmV4YW1wbGUuY29tIiwiR1JBTVBTX1VTRVJOQU1FIjoieW91ci11c2VyIiwiR1JBTVBTX1BBU1NXT1JEIjoieW91ci1wYXNzd29yZCIsIkdSQU1QU19UUkVFX0lEIjoieW91ci10cmVlLXV1aWQifX0%3D
