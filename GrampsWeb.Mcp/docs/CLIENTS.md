# Connecting MCP clients

MCP clients such as Claude Code, VS Code, Cursor, Codex, Antigravity CLI
and Open WebUI connect in one of three ways:

- **Over HTTP** to a server that already runs in [Docker](INSTALLATION.md#docker), on
  [Unraid](INSTALLATION.md#unraid) or with
  [Docker Compose](INSTALLATION.md#gramps-web--mcp-docker-compose). Each client needs only the
  server's address and API key, nothing is installed on the client's machine,
  and all clients share the one server.
- **Over stdio in Docker**: the client starts the server's image for itself.
  Each machine needs Docker and the Gramps Web credentials. VS Code and Cursor
  add this setup with one click.
- **Over stdio with the release binary**: the client starts the server from a
  single file downloaded from the release. Each machine needs the Gramps Web
  credentials, but neither Docker nor .NET.

Claude Desktop connects to servers added by URL from Anthropic's cloud, not
from your computer, so it cannot reach a server on your home network. Use the
[MCPB extension](INSTALLATION.md#claude-desktop-mcpb-extension) there.

## Over HTTP

Set `MCP_API_KEY` on the server (see [HTTP authentication](CONFIGURATION.md#http-authentication)),
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
- Over stateless HTTP, the default, `delete_records`, `merge_objects` and
  `undo_change` cannot show their confirmation dialog; see
  [Confirming deletes, merges and undos](CONFIGURATION.md#confirming-deletes-merges-and-undos).

To check the address and the key from a terminal, send an `initialize` request.
The key also works as `X-Api-Key: <key>`:

```bash
curl -X POST http://192.168.1.10:8080/mcp \
  -H "Authorization: Bearer $MCP_API_KEY" \
  -H "Accept: application/json, text/event-stream" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"curl","version":"1.0"}},"id":1}'
```

## Over stdio in Docker

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

The install buttons at the [top of the README](../../README.md#gramps-web-mcp) add this
entry to VS Code or Cursor. VS Code asks for the Gramps Web address, user name,
password and tree ID when the server starts for the first time, and keeps the
password in its secret storage. Cursor adds the placeholders shown above;
replace them with your own before the first start.

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
  [settings](CONFIGURATION.md) go the same way.
- [Uploads](CONFIGURATION.md#media-upload) read files inside the container, so mount the
  upload folder with `-v`.

## Over stdio with the release binary

Each [release](https://github.com/Scormave/gramps-web-mcp/releases) also carries
the server as one self-contained file, the same one the Claude Desktop bundles
hold. It starts over stdio unless told otherwise:

| Platform | Archive |
|----------|---------|
| macOS Apple Silicon | `gramps-web-mcp-osx-arm64.tar.gz` |
| macOS Intel | `gramps-web-mcp-osx-x64.tar.gz` |
| Windows x64 | `gramps-web-mcp-win-x64.zip` |
| Linux x64 | `gramps-web-mcp-linux-x64.tar.gz` |
| Linux ARM64 | `gramps-web-mcp-linux-arm64.tar.gz` |

Unpack the archive for your platform into a folder of its own. On macOS or
Linux (here Apple Silicon; change the archive name for yours):

```bash
mkdir -p ~/.local/share/gramps-web-mcp
curl -fL https://github.com/Scormave/gramps-web-mcp/releases/latest/download/gramps-web-mcp-osx-arm64.tar.gz \
  | tar -xz -C ~/.local/share/gramps-web-mcp
```

On Windows, in PowerShell:

```powershell
curl.exe -fLo "$env:TEMP\gramps-web-mcp.zip" https://github.com/Scormave/gramps-web-mcp/releases/latest/download/gramps-web-mcp-win-x64.zip
Expand-Archive -Force "$env:TEMP\gramps-web-mcp.zip" "$env:LOCALAPPDATA\gramps-web-mcp"
```

Then point the client at the binary by its full path, since clients start it
without a shell that would expand `~`:

```json
{
  "mcpServers": {
    "gramps-web": {
      "command": "/Users/you/.local/share/gramps-web-mcp/GrampsWeb.Mcp",
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

On Windows, `command` is
`"C:\\Users\\you\\AppData\\Local\\gramps-web-mcp\\GrampsWeb.Mcp.exe"`: JSON
needs each backslash doubled. Each client takes this entry the way it takes the
Docker entry above.

- Every [setting](CONFIGURATION.md) is an environment variable in `env`, such as
  `GRAMPS_REFRESH_TOKEN` instead of the user name and password, or
  `"GRAMPS_READ_ONLY": "true"`. Unlike in Docker, `localhost` in
  `GRAMPS_API_URL` and the [upload](CONFIGURATION.md#media-upload) folders are those of your
  own computer.
- To update, close the client (Windows cannot replace a running binary), run
  the same commands again and start the client: the archive name carries no
  version, so the link always fetches the latest release, and the new binary
  replaces the old one.
- The binary is not notarized by Apple. `curl` does not mark the download, so
  macOS runs it; if you downloaded the archive in a browser, macOS may refuse
  to start the binary until you remove the mark with
  `xattr -d com.apple.quarantine ~/.local/share/gramps-web-mcp/GrampsWeb.Mcp`.
- On Linux it needs glibc and ICU (`libicu`), which desktop distributions
  include; on Alpine or a minimal system, use Docker.
- `SHA256SUMS` in each release lists the checksums of its files, and every
  file, the bundles included, has a build provenance attestation. To check a
  download, save the archive instead of piping it to `tar` and run
  `gh attestation verify gramps-web-mcp-osx-arm64.tar.gz --repo Scormave/gramps-web-mcp`
  with the [GitHub CLI](https://cli.github.com/): it fails unless this
  repository's release workflow built that file.

The same binary can also serve the other machines over HTTP, like the image
does; see [Transports](CONFIGURATION.md#transports). To run the server from a source checkout
instead, see
[Local development](DEVELOPER_GUIDE.md#local-development) in
the developer guide.

## Images for vision-capable agents

Vision-capable agents can read opt-in media
([media file access](CONFIGURATION.md#media-file-access)) through `read_media`
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
