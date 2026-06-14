# Documentation

| Document | Audience | Description |
|----------|----------|-------------|
| [ARCHITECTURE.md](ARCHITECTURE.md) | Contributors | System design, layers, transport modes, read-only mode, deployment |
| [DEVELOPER_GUIDE.md](DEVELOPER_GUIDE.md) | Contributors | How to add tools, models, tests, and conventions |
| [TOOL_CATALOG.md](TOOL_CATALOG.md) | Users and agents | Complete catalog of 57 MCP tools, resources, prompts, and read-only behavior |
| [API_INVENTORY.md](API_INVENTORY.md) | Contributors | Gramps Web REST endpoints used by the server |
| [DATA_MODEL.md](DATA_MODEL.md) | Contributors | Entity models, serialization, and Flexible* input types |
| [SYSTEM_PROMPT.md](SYSTEM_PROMPT.md) | MCP client users | Suggested system prompt for genealogy assistants |

The repo-root `apispec.yaml` is a **vendored upstream** Gramps Web OpenAPI spec.
Do not edit it in place — replace wholesale when upstream changes.
