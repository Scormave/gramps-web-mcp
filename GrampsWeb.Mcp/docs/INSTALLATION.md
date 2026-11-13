# Installation

`gramps-web-mcp` runs next to an existing [Gramps Web](https://www.grampsweb.org/)
instance and works on its tree through the Gramps Web API. You need:

- A running Gramps Web instance with API access, and an account on it. It must
  be Gramps Web 26.6.2 or newer (Gramps Web API 3.16 or newer); with an older
  server every tool call returns an error that names both versions. Keep Gramps
  Web MCP 2.x until you can update Gramps Web
- Docker for the [image](#docker), [Docker Compose](#gramps-web--mcp-docker-compose)
  and [Unraid](#unraid). The [Claude Desktop extension](#claude-desktop-mcpb-extension)
  and the [release binary](CLIENTS.md#over-stdio-with-the-release-binary) need
  neither Docker nor .NET.
- The [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) only to
  run the server from source; see the [developer guide](DEVELOPER_GUIDE.md#quick-start)

Once the server runs, [connect your MCP clients](CLIENTS.md) and adjust the
[configuration](CONFIGURATION.md).

## Claude Desktop (MCPB extension)

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
machine. To check that a downloaded bundle came from this repository, see the
attestation note under
[the release binary](CLIENTS.md#over-stdio-with-the-release-binary).
See [`mcpb/README.md`](../../mcpb/README.md) for packaging details and
[`PRIVACY.md`](../../PRIVACY.md) for the privacy policy.

To build a bundle locally:

```bash
./scripts/pack-release.sh osx-arm64   # or osx-x64, win-x64, linux-x64, linux-arm64
```

## Docker

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

The image defaults to Streamable HTTP (`MCP_TRANSPORT=http`) on port 8080, which
is what the commands above use. Clients that spawn the container themselves
(such as MCP Registry installs) instead run it over stdio with
`-e MCP_TRANSPORT=stdio` and stdin kept open (`docker run -i`); that is the mode
declared in `server.json`.

### Health check

The image exposes a **`GET /health`** endpoint for Docker `HEALTHCHECK`, Unraid
container health, and other uptime monitors. It returns HTTP 200 when the MCP
server can authenticate against Gramps Web, or HTTP 503 otherwise, including
for a Gramps Web server older than API 3.16. Probes reuse
the token the tools use, so they do not log in to Gramps Web each time. The
public response is minimal: `{ "status": "healthy" }` or
`{ "status": "unhealthy" }`. Startup logs include a line such as
`Connected to Gramps Web at …` once the API is reachable, with the Gramps and
Gramps Web API versions, or one error that the server is too old.

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

### Verifying the image

Each published image has a build provenance attestation: a signed record,
kept by GitHub, of the repository, workflow and commit that built it. With the [GitHub CLI](https://cli.github.com/), check that an
image was built by this repository's GitHub Actions (with Docker Desktop
installed, start it first: `gh` asks it for registry credentials):

```bash
gh attestation verify oci://ghcr.io/scormave/gramps-web-mcp:latest --repo Scormave/gramps-web-mcp
```

## Gramps Web + MCP (Docker Compose)

To run Gramps Web and the MCP server on the same host and Docker network, use
[`docker-compose.example.yml`](../../docker-compose.example.yml) as a starting point:

```bash
cp docker-compose.example.yml docker-compose.yml
cp .env.example .env
# Complete the Gramps Web setup wizard, then set an authentication method in .env
docker compose up -d
```

Gramps Web is published on port **5055**; MCP is on **8080** (`/mcp` and
`/health`). Inside the compose network the MCP container reaches Gramps Web at
`http://grampsweb:5000`.

## Unraid

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
[Connecting MCP clients](CLIENTS.md) to connect them.

## Upgrading

To upgrade, pull the new image and recreate the container, install the bundle
of the new release in Claude Desktop, or
[download the release binary again](CLIENTS.md#over-stdio-with-the-release-binary).
The [changelog](../../CHANGELOG.md) lists what each release changed. Upgrading
from 2.x? Follow the [3.0 migration guide](MIGRATING_TO_3.md) to update saved
tool calls and client settings; from 1.x, follow the
[2.0 migration guide](MIGRATING_TO_2.md) first.

### Data dropped by updates before 2.3.1

Before 2.3.1 the `update_*` tools could drop stored data the call did not
mention ([#5](https://github.com/Scormave/gramps-web-mcp/issues/5)). If you
edited a tree with an earlier version,
[`scripts/find-update-losses.py`](../../scripts/find-update-losses.py) reads the
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
