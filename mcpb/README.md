# Claude Desktop MCPB Extension

This directory contains assets for packaging **gramps-web-mcp** as a Claude Desktop
extension (`.mcpb` / MCP Bundle).

## Layout

| File | Purpose |
|------|---------|
| `manifest.template.json` | MCPB manifest template; `__VERSION__` and `__PLATFORMS__` are substituted at pack time |
| `icon.png` | Extension icon shown in Claude Desktop |

## Local packaging

Prerequisites: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Optional:
[`@anthropic-ai/mcpb`](https://www.npmjs.com/package/@anthropic-ai/mcpb) for validation.

```bash
# macOS Apple Silicon
./scripts/pack-mcpb.sh osx-arm64

# macOS Intel
./scripts/pack-mcpb.sh osx-x64

# Windows x64 (run on Windows or cross-publish from CI)
./scripts/pack-mcpb.sh win-x64 1.0.0
```

Output: `dist/gramps-web-mcp-claude-desktop-<rid>-v<version>.mcpb`

Install by double-clicking the `.mcpb` file or dragging it into Claude Desktop.

## Configuration

Claude Desktop shows a settings form generated from `user_config` in the manifest:

| Setting | Env var |
|---------|---------|
| Gramps Web URL | `GRAMPS_API_URL` |
| Username | `GRAMPS_USERNAME` |
| Password / token | `GRAMPS_PASSWORD` |
| Tree ID | `GRAMPS_TREE_ID` |
| Read-only mode | `GRAMPS_READ_ONLY` |
| Media file access | `GRAMPS_MEDIA_RESOURCES_ENABLED` |
| Media max bytes | `GRAMPS_MEDIA_MAX_BYTES` |
| Allowed MIME types | `GRAMPS_MEDIA_ALLOWED_MIME_TYPES` |
| Allow private media | `GRAMPS_MEDIA_ALLOW_PRIVATE` |

**Read-only mode defaults to enabled** in the extension for safer first use.

## GitHub Releases

Tag pushes (`v*`) trigger [`.github/workflows/mcpb-release.yml`](../.github/workflows/mcpb-release.yml),
which builds three platform bundles and attaches them to the GitHub Release for that tag.

This workflow is independent of the Docker / MCP Registry pipeline in `docker.yml`.

## Connectors Directory

For Anthropic Connectors Directory submission, see [PRIVACY.md](../PRIVACY.md) and ensure
all MCP tools expose `title`, `readOnlyHint`, and `destructiveHint` annotations (implemented
via `[McpServerTool(Title, ReadOnly, Destructive)]` on each tool).
