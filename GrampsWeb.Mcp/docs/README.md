# Documentation

| Document | Audience | Description |
|----------|----------|-------------|
| [ARCHITECTURE.md](ARCHITECTURE.md) | Contributors | System design, layers, transport modes, read-only mode, deployment |
| [DEVELOPER_GUIDE.md](DEVELOPER_GUIDE.md) | Contributors | How to add tools, models, tests, and conventions |
| [RELEASING.md](RELEASING.md) | Maintainers and agents | Release checklist for changelog, version metadata, Docker, MCPB, GitHub Releases, and MCP Registry |
| [TOOL_CATALOG.md](TOOL_CATALOG.md) | Users and agents | Complete catalog of 57 MCP tools, resources, prompts, and read-only behavior |
| [API_INVENTORY.md](API_INVENTORY.md) | Contributors | Gramps Web REST endpoints used by the server |
| [DATA_MODEL.md](DATA_MODEL.md) | Contributors | Entity models, serialization, and Flexible* input types |
| [SYSTEM_PROMPT.md](SYSTEM_PROMPT.md) | MCP client users | Suggested system prompt for genealogy assistants |

For Claude Desktop one-click install, see the repo-root [mcpb/README.md](../../mcpb/README.md) and [PRIVACY.md](../../PRIVACY.md).

## Pairing with Gramps Web

`gramps-web-mcp` is a companion service for an existing
[Gramps Web](https://www.grampsweb.org/) deployment. It does not host a
genealogy web UI, manage Gramps users, or store family tree data itself.
Instead, it connects to the Gramps Web REST API and exposes that tree to MCP
clients as tools, resources, and prompts.

Use Gramps Web for the browser UI, user management, tree storage, imports,
media management, and normal genealogy workflows. Use `gramps-web-mcp` when an
MCP-compatible AI client needs controlled access to that same Gramps Web tree.

The repo-root `apispec.yaml` is a **vendored upstream** Gramps Web OpenAPI spec.
Do not edit it in place — replace wholesale when upstream changes.
