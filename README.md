# gramps-web-mcp
MCP Server for Gramps Web open-source genealogy system

## Transports

Set `GRAMPS_API_URL`, `GRAMPS_USERNAME`, `GRAMPS_PASSWORD`, and `GRAMPS_TREE_ID` as usual.

| `MCP_TRANSPORT` | Behavior |
|-----------------|----------|
| *(unset or `stdio`)* | JSON-RPC over stdin/stdout (default; local clients). |
| `http` | [Streamable HTTP](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports#streamable-http) at `MCP_PATH` (default `/mcp`). Responses stream over SSE. Set `ASPNETCORE_URLS` (e.g. `http://127.0.0.1:8080`). |
| `sse` | Legacy MCP SSE: `GET {MCP_PATH}/sse` + `POST {MCP_PATH}/message`. Stateful; use for older clients only. |

Optional: `MCP_STATELESS=true|false` (default `true` for `http`), `MCP_ENABLE_LEGACY_SSE=true` with `MCP_TRANSPORT=http` to expose legacy `/sse` alongside Streamable HTTP (stateful).
