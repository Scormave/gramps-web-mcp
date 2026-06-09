# Architecture Overview

## What is this project?

**gramps-web-mcp** is an MCP (Model Context Protocol) server that gives AI agents
structured, tool-based access to the [Gramps Web](https://grampsweb.org/) genealogy
platform.  The server acts as a bridge: it translates MCP tool calls coming from
an AI client (Claude Desktop, Cursor, custom agents, …) into authenticated
Gramps Web REST API requests and returns formatted results.

```
┌────────────┐   MCP (stdio / HTTP)   ┌──────────────────┐   REST / JSON   ┌────────────────┐
│  AI Agent  │ ◄─────────────────────► │  gramps-web-mcp  │ ◄─────────────► │  Gramps Web API│
│  (client)  │                         │  (.NET 8 server)  │                │  (remote)      │
└────────────┘                         └──────────────────┘                 └────────────────┘
```

## Solution structure

```
gramps-web-mcp.sln
├── GrampsWeb.Mcp/          — main application (MCP server)
│   ├── Program.cs          — entry point, transport selection
│   ├── Client/             — HTTP client for Gramps Web API
│   ├── Config/             — environment-based configuration
│   ├── Dates/              — date parsing for agent-friendly input
│   ├── Exceptions/         — domain exceptions
│   ├── Formatters/         — model → human-readable text
│   ├── Input/              — Flexible* types (agent-friendly deserialization)
│   ├── Models/             — Gramps entity DTOs
│   ├── Requests/           — create/update request DTOs
│   ├── Prompts/            — MCP workflow prompts (add-person, research-person, …)
│   ├── Resources/          — MCP resources (gramps://* reference data)
│   ├── Serialization/      — JSON converters, wire-format adapters
│   ├── Tools/              — MCP tool implementations (one file per domain)
│   │   └── Parsing/        — small parsers (gender, confidence, note format)
│   └── docs/               — internal documentation (this folder)
└── GrampsWeb.Mcp.Tests/    — test project
    ├── Contract/           — DTO ↔ OpenAPI spec sync tests
    ├── Fixtures/           — JSON test payloads
    ├── IntegrationTests/   — formatter integration tests
    └── UnitTests/          — unit tests
```

## Runtime & dependencies

| Component | Version |
|-----------|---------|
| .NET SDK / Runtime | 8.0 |
| `ModelContextProtocol` | 1.2.0 |
| `ModelContextProtocol.AspNetCore` | 1.2.0 |
| `Microsoft.Extensions.Hosting` | 10.0.5 |
| `Microsoft.Extensions.Http` | 10.0.5 |

Test-only: xUnit 2.7, Moq 4.20, YamlDotNet 16.3 (for OpenAPI spec parsing).

## Transport modes

The server supports three MCP wire transports, chosen via the `MCP_TRANSPORT`
environment variable.  See `McpTransportConfig` for details.

| Value | Protocol | Default |
|-------|----------|---------|
| `stdio` | JSON-RPC over stdin/stdout | Yes (local clients) |
| `http` | Streamable HTTP (SSE responses) | Default in Docker image |
| `sse` | Legacy MCP SSE (`GET /sse` + `POST /message`) | Stateful only |

`Program.cs` selects between `RunStdioAsync` (empty host) and `RunHttpAsync`
(ASP.NET Core + `MapMcp`).

## Configuration

Configuration is loaded from **environment variables** (no appsettings files).
Read-only mode can also be enabled with a server CLI argument.

### Required (Gramps connection)

| Variable | Description |
|----------|-------------|
| `GRAMPS_API_URL` | Base URL of the Gramps Web instance |
| `GRAMPS_USERNAME` | API user name |
| `GRAMPS_PASSWORD` | API password |
| `GRAMPS_TREE_ID` | Tree UUID on that server |

### Optional (MCP transport)

| Variable | Description | Default |
|----------|-------------|---------|
| `MCP_TRANSPORT` | `stdio`, `http`, or `sse` | `stdio` |
| `ASPNETCORE_URLS` | Listen URLs for HTTP/SSE | — |
| `MCP_PATH` | URL prefix for MCP endpoints | `/mcp` |
| `MCP_STATELESS` | Stateless mode for Streamable HTTP | `true` |
| `MCP_ENABLE_LEGACY_SSE` | Expose legacy `/sse` with `http` transport | `false` |

### Optional (runtime mode)

| Variable / argument | Description | Default |
|---------------------|-------------|---------|
| `GRAMPS_READ_ONLY=true` or `--read-only` | Keep all MCP tools visible, but block create/update/delete mutation calls before they reach Gramps Web | read/write |
| `--read-only=false` | Explicitly disable read-only mode, overriding `GRAMPS_READ_ONLY=true` | — |

## Architectural layers

### 1. Tools (`Tools/`)

Each file exposes a set of `[McpServerTool]` static methods grouped by Gramps
entity type (Person, Family, Event, Place, Source, Citation, Note, Media, Tag,
Repository) plus cross-cutting tools (Search, System, Reference) and
multi-step convenience tools (Composite).

Tools are the **public API surface** of the MCP server.  They:
- validate input parameters (with server-side type validation via `TypeCache`)
- call `GrampsApiClient` to interact with the REST API
- resolve Gramps IDs to handles automatically via `HandleResolver`
- format responses via `Formatters/` into human-readable text
- return helpful context on not-found via `NotFoundHelper`
- map errors to `McpException` via `McpToolErrors`

`CompositeTools.cs` provides multi-step convenience tools (`FindByGrampsId`,
`QuickAddPerson`, `AddEventToPerson`) that combine multiple API calls into
a single tool invocation.

The MCP SDK discovers tools at startup via `WithToolsFromAssembly()`.
Read-only mode intentionally keeps this discovery unchanged: write tools remain
visible to clients, but mutation calls fail with a clear MCP error.

### 1b. Resources (`Resources/`)

`GrampsResources.cs` exposes read-only reference payloads as MCP resources:
`gramps://input-guide`, `gramps://types`, `gramps://metadata`,
`gramps://name-settings`.

For clients that cannot call MCP `resources/read`, the same payloads are also
available through compatibility tools in `ReferenceTools.cs`.

The MCP SDK discovers resources at startup via `WithResources<GrampsResources>()`.

### 1c. Prompts (`Prompts/`)

`GrampsPrompts.cs` exposes workflow templates as MCP prompts (`add-person`,
`research-person`, `add-family`, `find-connections`, `import-from-text`).
Each prompt expands to a user-role chat message that guides an agent through
typical tool usage.

The MCP SDK discovers prompts at startup via `WithPrompts<GrampsPrompts>()`.

### 2. Client (`Client/`)

`GrampsApiClient` is the HTTP client with:
- JWT authentication (automatic token acquisition and refresh)
- typed GET/POST/PUT/DELETE with `System.Text.Json`
- mutation response parsing (`PostMutationAsync` / `PutMutationAsync`)
  that handles Gramps' change-array responses
- read-only enforcement for mutation helpers (`PostMutationAsync`,
  `PutMutationAsync`, `DeleteAsync`) before authentication or request creation
- request/response logging with sensitive field redaction
- paged list support (`GetPagedListAsync<T>`)

`GrampsApiClientExtensions` adds null-on-404 helpers.
`ExtendedEntityEnrichment` refetches nested objects for `?extend=all` responses
where the API doesn't deeply populate references.

`HandleResolver` detects Gramps ID patterns (e.g. `I0001`, `F0023`) and
resolves them to opaque API handles via a list-endpoint query. This lets
agents pass either handles or Gramps IDs to any tool parameter.

`TypeCache` is a thread-safe, TTL-based in-memory cache of Gramps type
vocabularies (default + custom). Write tools use `TypeCache.ValidateTypeAsync`
to check type strings before sending requests to the API, providing helpful
error messages with suggestions on typos.

### 3. Models (`Models/`)

C# record/class DTOs matching the Gramps Web JSON schema.  Key aspects:
- `[JsonPropertyName("snake_case")]` maps to API field names
- custom `JsonConverter` attributes handle polymorphic wire shapes
  (string-or-object handles, wire-type objects, dates)
- `*Extended` classes add an `extended` property with resolved sub-entities

### 4. Serialization (`Serialization/`)

Custom `JsonConverter<T>` implementations that normalize the Gramps Web API's
inconsistencies:
- handle fields that may be strings or `{ref}` objects
- type fields as plain strings or `{_class, string}` objects
- date wire format ↔ flat `GrampsDate`
- note text as string or StyledText
- paged results as bare arrays or `{objects, total, page}`

`GrampsJson.Options` is the shared `JsonSerializerOptions` instance:
camelCase naming, case-insensitive read, skip unknown members, ignore nulls
on write, omit empty collections.

### 5. Input (`Input/`)

`Flexible*` wrapper types let agents pass arguments in multiple formats:
- `FlexibleHandleList`: JSON array, single string, comma-separated, `{ref}` objects
- `FlexibleGrampsName`: full JSON or shorthand string notation
- `FlexibleAttributeList`: objects or `"Type: Value"` strings
- etc.

Each type has a `[JsonConverter]` that normalizes input into the canonical
model type and an `implicit operator` cast.

### 6. Requests (`Requests/`)

DTOs for POST/PUT operations.  Mirror the Gramps Web JSON schema with
`_class`, optional `handle`/`gramps_id`/`change`, and snake_case naming.
`GrampsRequestMapping` converts GET models → request DTOs.

### 7. Formatters (`Formatters/`)

Convert models into human-readable text for MCP tool responses.  Strategy:
- structured sections with headers (e.g., `PERSON`, `EVENTS`, `FAMILIES`)
- one-line summaries for list/search results
- resolved type labels via `GrampsDefaultTypeLabels`
- kinship labels for ancestor/descendant trees
- indented JSON fallback for dynamic payloads (`JsonResponseFormatter`)
- `ResponseEnvelope` adds machine-readable YAML-like headers with type,
  handle, gramps_id, and action metadata to tool responses, plus suggested
  next-step hints for create operations

### 8. Dates (`Dates/`)

- `AgentDateParser`: parses agent-friendly date strings (ISO, slash/dot,
  with modifiers like "about", "before", "between") into Gramps date requests
- `GrampsDateSortVal`: computes sortable integer values from date components

## Error handling

```
GrampsApiException          →  McpToolErrors.ToMcpException()  →  McpException
(HTTP errors from API)         (catch in each tool method)        (isError=true in MCP)

Validation errors           →  McpToolErrors.ValidationError() →  McpException
(bad input from agent)
```

All tool methods follow the same pattern: `try { ... } catch (Exception ex) { throw McpToolErrors.ToMcpException(ex); }`.

## Deployment

### Local development

```bash
./run-local-server.sh
# Connects to demo.grampsweb.org with HTTP transport on localhost:8080
```

### Docker

```bash
docker build -t gramps-web-mcp .
docker run -p 8080:8080 \
  -e GRAMPS_API_URL=https://your-gramps.example.com \
  -e GRAMPS_USERNAME=user \
  -e GRAMPS_PASSWORD=pass \
  -e GRAMPS_TREE_ID=uuid \
  gramps-web-mcp
```

Enable read-only mode with either an environment variable or an app argument:

```bash
docker run -p 8080:8080 \
  -e GRAMPS_API_URL=https://your-gramps.example.com \
  -e GRAMPS_USERNAME=user \
  -e GRAMPS_PASSWORD=pass \
  -e GRAMPS_TREE_ID=uuid \
  -e GRAMPS_READ_ONLY=true \
  gramps-web-mcp

docker run -p 8080:8080 \
  -e GRAMPS_API_URL=https://your-gramps.example.com \
  -e GRAMPS_USERNAME=user \
  -e GRAMPS_PASSWORD=pass \
  -e GRAMPS_TREE_ID=uuid \
  gramps-web-mcp --read-only
```

The Dockerfile uses multi-stage build (SDK → ASP.NET runtime) and defaults to
HTTP transport on port 8080.

### CI

- **GitHub Actions** (`.github/workflows/ci.yml`): build and test on push/PR.
- **GitHub Actions** (`.github/workflows/docker.yml`): build and publish the
  Docker image to `ghcr.io/scormave/gramps-web-mcp`.
- **Gitea Actions** (`.gitea/workflows/docker.yml`): builds and publishes the
  Docker image to a private Gitea container registry.

## Testing strategy

- **Contract tests** (`Contract/`): verify that C# DTOs match the OpenAPI spec
  (`apispec.yaml`).  `swagger-dto-map.json` defines the mapping; run with
  `[Trait("Category","Contract")]`.
- **Unit tests** (`UnitTests/`): cover serialization, date parsing, formatters,
  flexible input types, mutation parsing.
- **Integration tests** (`IntegrationTests/`): end-to-end formatter tests with
  realistic JSON fixtures.
- **Fixtures** (`Fixtures/`): JSON files representing actual API responses for
  deserialization tests.
