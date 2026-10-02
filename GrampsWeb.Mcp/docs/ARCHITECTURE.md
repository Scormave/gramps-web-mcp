# Architecture Overview

## What is this project?

**gramps-web-mcp** is an MCP (Model Context Protocol) server that gives AI agents
structured, tool-based access to the [Gramps Web](https://grampsweb.org/) genealogy
platform.  The server acts as a bridge: it translates MCP tool calls coming from
an AI client (Claude Desktop, Cursor, custom agents, …) into authenticated
Gramps Web REST API requests and returns formatted results.

It is a companion service, not a standalone genealogy application. Gramps Web
remains the browser UI, user and permission system, data store, import/export
surface, and media manager; `gramps-web-mcp` only exposes that existing Gramps
Web tree to MCP clients.

```
┌────────────┐   MCP (stdio / HTTP)   ┌──────────────────┐   REST / JSON   ┌────────────────┐
│  AI Agent  │ ◄─────────────────────► │  gramps-web-mcp  │ ◄─────────────► │  Gramps Web API│
│  (client)  │                         │  (.NET 10 server) │                │  (remote)      │
└────────────┘                         └──────────────────┘                 └────────────────┘
```

## Solution structure

```
gramps-web-mcp.sln
├── GrampsWeb.Mcp/          — main application (MCP server)
│   ├── Program.cs          — entry point, transport selection
│   ├── Client/             — HTTP client for Gramps Web API
│   ├── Config/             — environment-based configuration
│   ├── Auth/               — HTTP/SSE API key authentication
│   ├── Dates/              — date parsing for agent-friendly input
│   ├── Exceptions/         — domain exceptions
│   ├── Formatters/         — model → human-readable text
│   ├── Health/             — Gramps Web connectivity check behind GET /health
│   ├── Hosting/            — DI setup, MCP/health endpoints, tool profile, argument checks
│   ├── Input/              — Flexible* types (agent-friendly deserialization)
│   ├── Logging/            — GRAMPS_LOG_LEVEL parsing, one-line console formatter
│   ├── Models/             — Gramps entity DTOs
│   ├── Requests/           — create/update request DTOs
│   ├── Prompts/            — MCP workflow prompts (add-person, research-person, …)
│   ├── Resources/          — MCP resources (gramps://* reference data, media previews)
│   ├── Serialization/      — JSON converters, wire-format adapters
│   ├── Tools/              — MCP tool implementations (one file per domain)
│   │   └── Parsing/        — small parsers (gender, confidence, note format)
│   └── docs/               — internal documentation (this folder)
└── GrampsWeb.Mcp.Tests/    — test project
    ├── Contract/           — DTO ↔ OpenAPI spec sync tests
    ├── Fixtures/           — JSON test payloads
    ├── IntegrationTests/   — formatter and in-process MCP pipeline tests
    └── UnitTests/          — unit tests
```

## Runtime & dependencies

| Component | Version |
|-----------|---------|
| .NET SDK / Runtime | 10.0 |
| `ModelContextProtocol` | 1.3.0 |
| `ModelContextProtocol.AspNetCore` | 1.3.0 |
| `Microsoft.Extensions.Hosting` | 10.0.5 |
| `Microsoft.Extensions.Http` | 10.0.5 |
| `SixLabors.ImageSharp` | 3.1.12 (media thumbnails; Six Labors Split License, Apache 2.0 terms for open-source use) |

Test-only: xUnit 2.7, Moq 4.20, YamlDotNet 16.3 (for OpenAPI spec parsing),
Microsoft.AspNetCore.TestHost 10.0.5 (in-process MCP server tests).

[`THIRD-PARTY-NOTICES.txt`](../../THIRD-PARTY-NOTICES.txt) lists every package the
server restores, including transitive ones, with its license.
`ThirdPartyNoticesTests` fails when that list and `obj/project.assets.json` differ.

## Transport modes

The server supports three MCP wire transports, chosen via the `MCP_TRANSPORT`
environment variable.  See `McpTransportConfig` for details.

| Value | Protocol | Default |
|-------|----------|---------|
| `stdio` | JSON-RPC over stdin/stdout | Yes (local clients) |
| `http` | Streamable HTTP (SSE responses) | Default in Docker image |
| `sse` | Legacy MCP SSE (`GET /sse` + `POST /message`) | Stateful only |

`Program.cs` selects between `RunStdioAsync` (empty host) and `RunHttpAsync`
(ASP.NET Core + `MapGrampsMcpEndpoints`). HTTP mode optionally gates MCP routes
with `MCP_API_KEY` via `RequireAuthorization()`; `GET /health` stays anonymous.

## Configuration

Configuration is loaded from **environment variables** (no appsettings files).
The server has no command-line options of its own; read-only mode is set only
through `GRAMPS_READ_ONLY`.

### Required (Gramps connection)

| Variable | Description |
|----------|-------------|
| `GRAMPS_API_URL` | Base URL of the Gramps Web instance |
| `GRAMPS_USERNAME` | API user name |
| `GRAMPS_PASSWORD` | API password |
| `GRAMPS_REFRESH_TOKEN` | Refresh token; replaces username and password when set |
| `GRAMPS_TREE_ID` | Tree UUID on that server |

### Optional (MCP transport)

| Variable | Description | Default |
|----------|-------------|---------|
| `MCP_TRANSPORT` | `stdio`, `http`, or `sse` | `stdio` |
| `ASPNETCORE_URLS` | Listen URLs for HTTP/SSE | — |
| `MCP_PATH` | URL prefix for MCP endpoints | `/mcp` |
| `MCP_STATELESS` | Stateless mode for Streamable HTTP | `true` |
| `MCP_ENABLE_LEGACY_SSE` | Expose legacy `/sse` with `http` transport | `false` |
| `MCP_API_KEY` | Shared secret for HTTP/SSE transport | — |

### Optional (runtime mode)

| Variable | Default |
|----------|---------|
| `GRAMPS_READ_ONLY` | `false` |
| `GRAMPS_MUTATION_SERIALIZE` | `true` |
| `GRAMPS_MUTATION_MIN_INTERVAL_MS` | `0` |

- `GRAMPS_READ_ONLY`: set to `true` to publish only read-only MCP tools and
  block create/update/delete mutation calls, including direct calls to a
  previously known write-tool name.
- `GRAMPS_MUTATION_SERIALIZE`: serializes mutation HTTP calls in-process, plus
  read/modify/write sequences of update tools and `add_event_to_person` against
  each other using a separate lock. It does not coordinate with external clients.
  Writes run in parallel only when this is `false` and
  `GRAMPS_MUTATION_MIN_INTERVAL_MS` is `0`; a minimum interval keeps the
  single-flight lock.
- `GRAMPS_MUTATION_MIN_INTERVAL_MS`: minimum milliseconds between mutation HTTP
  calls, including steps inside composite tools.

Runtime notes:

- `GRAMPS_READ_ONLY=false` starts the server in read/write mode.
- SQLite users who still see `database is locked` on sequential edits often want
  `GRAMPS_MUTATION_MIN_INTERVAL_MS=250`.

### Optional (media resources)

| Variable | Description | Default |
|----------|-------------|---------|
| `GRAMPS_MEDIA_RESOURCES_ENABLED` | Enable binary MCP media resources and image-content media tools | `false` |
| `GRAMPS_MEDIA_MAX_BYTES` | Maximum bytes returned by any media resource/tool | `5242880` |
| `GRAMPS_MEDIA_ALLOW_PRIVATE` | Allow bytes for Gramps media records marked private | `false` |

When media access is disabled, `tools/list` omits `read_media`;
media metadata remains available through `get_object`.
`GRAMPS_MEDIA_ALLOWED_MIME_TYPES` is no longer read; `GrampsConfig` lists it as
retired, and startup logs a warning when it is still set.

### Optional (logging)

| Variable | Description | Default |
|----------|-------------|---------|
| `GRAMPS_LOG_LEVEL` | Log level for `GrampsWeb.Mcp` categories | `Information` |

At `Information`, Gramps API calls are logged as method, path without query
string, status, duration, and body length. Request and response bodies and full
URLs contain genealogy data, so `GrampsApiClient` logs them only at `Debug` or
`Trace`, after redacting credential-like fields. Blank, unknown, or unresolved
`${...}` values fall back to `Information`.

## Architectural layers

### 1. Tools (`Tools/`)

Each file exposes a set of `[McpServerTool]` static methods grouped by Gramps
entity type (Person, Family, Event, Place, Source, Citation, Note, Media, Tag,
Repository) plus cross-cutting tools (Search, System, Reference, Object, Timeline) and
multi-step convenience tools (Composite).

Tools are the **public API surface** of the MCP server.  They:
- validate input parameters (with server-side type validation via `TypeCache`)
- call `GrampsApiClient` to interact with the REST API
- resolve Gramps IDs to handles automatically via `HandleResolver`
- format responses via `Formatters/` into human-readable text
- return typed media content for thumbnail/file tools (image, audio, or embedded blob resource)
- return helpful context on not-found via `NotFoundHelper`
- map errors to `McpException` via `McpToolErrors`

`CompositeTools.cs` provides multi-step convenience tools (`QuickAddPerson`,
`AddEventToPerson`) that combine multiple API calls into
a single tool invocation.

`TimelineTools.cs` provides `GetTimeline` for person, family, and place
chronologies. It dispatches to the appropriate API route or place-backlink
fallback while keeping the public catalog to one timeline tool.

Tools are registered through `WithGrampsToolProfile()` in
`Hosting/McpToolProfileExtensions.cs`. It calls `WithToolsFromAssembly()`,
adds the call-tool filters for argument checks and read scopes, and, when
needed, a `tools/list` filter that publishes only tools enabled by the current
configuration:
read-only mode removes write tools, and disabled media access removes
`read_media`. Hidden tools remain registered so direct calls still receive
the normal read-only or configuration error.

### 1b. Resources (`Resources/`)

`GrampsResources.cs` exposes read-only reference payloads as MCP resources:
`gramps://input-guide`, `gramps://types`, `gramps://metadata`,
`gramps://name-settings`.

It also exposes opt-in binary media resources:
`gramps://media/{handle}/thumbnail/{size}` and
`gramps://media/{handle}/file`. These return `BlobResourceContents`, fetch
media metadata first, and enforce enabled/private/size safeguards before
returning bytes to the MCP client.

Gramps Web's own `/api/media/{handle}/thumbnail/{size}` endpoint always returns
AVIF, which MCP clients cannot display, so the server does not use it.
`MediaPreviewRenderer` builds thumbnails from `/api/media/{handle}/file`
with ImageSharp instead:

1. Reject non-image MIME types and image types ImageSharp cannot decode (AVIF,
   HEIC/HEIF, JPEG XL, SVG) before downloading the original.
2. Download the original up to 50 MiB, or `GRAMPS_MEDIA_MAX_BYTES` if larger,
   and identify it; images over 100 megapixels are rejected.
3. Decode only the first frame, scaling during decode when the long edge
   exceeds the requested size, then apply EXIF orientation and downscale to
   the size with Lanczos3. Images are never upscaled.
4. Copy the pixels into a new image so no EXIF, GPS, XMP, IPTC, ICC, or comment
   metadata reaches the encoder, then encode JPEG (quality 85), or PNG when any
   pixel is transparent.
5. Reject the result if it exceeds `GRAMPS_MEDIA_MAX_BYTES`.

Mode `file` returns the original unchanged. Images other than JPEG, PNG, GIF,
and WebP are returned as an embedded blob with a text hint, since clients only
display those four as image content.

For clients that cannot call MCP `resources/read`, the same payloads are also
available through the `GetReference` compatibility tool in `ReferenceTools.cs`.
Media bytes are mirrored through `read_media` for clients that consume MCP
tool content directly. It always returns `CallToolResult`: mode `thumbnail`
(default) contains image content, while mode `file` contains image, audio, or
embedded blob resource content depending on MIME type. Thumbnail size defaults
to 1568 pixels on the long edge, which keeps document scans legible for vision
models, and accepts 1 to 4096; an explicit size is rejected in file mode. Full MCP clients may also
use the unchanged `resources/read` URIs.

The MCP SDK discovers resources at startup via `WithResources<GrampsResources>()`.

### 1c. Prompts (`Prompts/`)

`GrampsPrompts.cs` exposes workflow templates as MCP prompts (`add-person`,
`research-person`, `add-family`, `find-connections`, `import-from-text`,
`change-link`, `cite-fact`).
Each prompt expands to a user-role chat message that guides an agent through
typical tool usage.

The MCP SDK discovers prompts at startup via `WithPrompts<GrampsPrompts>()`.

### 2. Client (`Client/`)

`GrampsApiClient` is the HTTP client with:
- JWT authentication (automatic token acquisition and refresh). When Gramps
  Web rejects the access token (HTTP 401, or 422 after its `SECRET_KEY`
  changed, both with a flask-jwt-extended `{"msg": …}` body), the client
  replaces it through `GrampsAuthTokenProvider` and sends the request once
  more. Gramps Web checks the token before the endpoint runs, so writes are
  retried too; Gramps Web's own HTTP 422 validation errors are not
- typed GET plus mutation POST/PUT/DELETE with `System.Text.Json`
- mutation response parsing in `PostMutationAsync`, which reads the new
  handle and Gramps ID from Gramps' change-array response through
  `GrampsMutationParser`; `PutMutationAsync` only checks the status
- read-only enforcement for mutation helpers (`PostMutationAsync`,
  `PutMutationAsync`, `DeleteAsync`) before authentication or request creation
- in-process write policy via singleton `MutationGate`: optional single-flight
  lock and minimum interval around the mutation HTTP send only (auth stays
  outside the write mutex). The gate does not cover other Gramps Web clients
  or MCP replicas.
- retryable mapping of SQLite `database is locked` (HTTP 500/503) and upstream
  429 onto clear mutation error messages
- request/response logging with sensitive field redaction
- paged list support (`GetPagedListAsync<T>`)
- binary GET support (`GetBytesAsync`) for media resources, with payload-free
  logging and configured maximum-size enforcement

`GrampsApiClientExtensions` adds null-on-404 helpers.
`ExtendedEntityEnrichment` refetches nested objects for `?extend=all` responses
where the API doesn't deeply populate references, one batch per object type.

`HandleResolver` detects Gramps ID patterns (e.g. `I0001`, `F0023`) and
resolves them to opaque API handles via a list-endpoint query. This lets
agents pass either handles or Gramps IDs to any tool parameter. Successful
resolutions are kept in the static `HandleCache` for 10 minutes, keyed by API
URL, tree ID, and Gramps ID; concurrent lookups of the same ID share one
request.

`GrampsTypeVocabularies` keeps Gramps type vocabularies between tool calls.
One instance is registered as a singleton and passed to every
`GrampsApiClient`, which reads types through `GetDefaultTypesAsync` and
`GetCustomTypesAsync`. Default types come with the Gramps version and are kept
for the life of the process; custom types change when someone adds one in
Gramps and are read again after 10 minutes. Concurrent reads share one
request, and failed reads are not kept. A client constructed without an
instance, as in unit tests, gets its own.

`TypeCache` merges the default and custom vocabularies. Write tools use
`TypeCache.ValidateTypeAsync` to check type strings before sending requests to
the API, providing helpful error messages with suggestions on typos. A value
missing from the cached vocabularies reads custom types again before it is
rejected, so a type just added in Gramps is accepted. `gramps://types` and
`get_reference(topic: "types")` read custom types each time.

Read-only MCP tool calls also open a `GrampsReadScope`. Within that call,
identical `GetAsync` requests (same client instance and exact path, including
query parameters) share one HTTP fetch, including concurrent requests. The
cache stores JSON text and deserializes a fresh DTO for each consumer, so
enrichment cannot mutate another consumer's data. Failed requests are removed
to allow retries. The scope is disposed at the end of the call; write tools,
binary downloads, paginated-list requests, and direct client calls outside an
MCP read scope retain their existing behavior. No entity data is shared across
calls or client instances; only Gramps ID resolutions (`HandleCache`), type
vocabularies, and the batch-filter support flag outlive a call.

`GrampsBatchFetch.GetByHandlesAsync` loads many objects of one type through the
list endpoint's `handles` filter (`GET /api/{type}/?handles=a,b`, Gramps Web
API 3.14+), 50 handles per request and up to 4 requests at a time, with the
same `profile` or `backlinks` arguments as a detail read. Place timelines,
`get_person_tree` generations, `get_relations` common ancestors, search hits
without a usable embedded object, and the events, citations, and media of an
extended person or family use it. A
server that answers HTTP 400 or 422, or returns objects that were not asked
for, is remembered per API URL and tree for the life of the process, and its
objects are fetched one per request, up to 4 at a time. Handles missing from a
filtered reply are treated as deleted.

Search formatting loads type-label categories only for the entity types in
the results, through `GrampsTypeVocabularies`, without changing vocabulary
order.

Search requests `profile=self`, so each hit's embedded `object` carries the
summary of its related objects: a person's name, birth, and death, a family's
partners, an event's place name, and a citation's source title. The summary
line is built from the hit alone when its handle matches and it contains the
fields that entity needs. Missing, partial, or incompatible objects are read
again in one `GetByHandlesAsync` batch per type with the same profile; a hit
the batch does not return is shown with "(error loading details)".
`list_objects` requests the same profile for people, families, events, and
citations and formats rows with the same code. Result order follows the hits.

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
on write, omit empty collections. `GrampsJson.UpdateOptions` is the same but
writes empty collections, so `PutMutationAsync` can clear a list.

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

- `AgentDateParser`: parses agent-friendly date strings (ISO, English months, year/ISO/mixed ranges,
  open-ended forms, From/To prefixes, estimated/calculated qualities) into Gramps date requests; unrecognized
  input and non-Gregorian calendars fail validation;
  `DateIntervalPreference` chooses span/From (places) vs range/After (events, citations, media) for ambiguous dashes
- `EnglishMonthNames`: English abbreviated/full month names the parser accepts
- `GrampsDateHelpers`: shared emptiness check for empty API date objects
- `GrampsDateSortVal`: computes sortable integer values from date components

## Error handling

```
GrampsApiException          →  McpToolErrors.ToMcpException()  →  McpException
(HTTP errors from API)         (catch in each tool method)        (isError=true in MCP)
SQLite lock / HTTP 429         rewritten retryable message         same isError path
on mutations

Validation errors           →  McpToolErrors.ValidationError() →  McpException
(bad input from agent)

Argument errors             →  ToolArgumentValidator           →  McpException
(unknown or missing names,     (call-tool filter, input schema)
values of the wrong type)

Composite partial failure   →  McpToolErrors.ToMcpException(ex, createdObjects)
(lock after some writes)       lists already-created IDs so the agent does not retry the whole tool
```

All tool methods follow the same pattern: `try { ... } catch (Exception ex) { throw McpToolErrors.ToMcpException(ex); }`.
Argument binding happens before the tool method runs, and the SDK reports its failures as a
bare "An error occurred invoking", so `McpToolProfileExtensions` checks arguments in a
call-tool filter: an unknown name (with a suggestion such as `extended` for `extend`), a
missing required argument, or a value of the wrong type returns the tool's parameter list.

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

For instances with password login disabled, replace `GRAMPS_USERNAME` and
`GRAMPS_PASSWORD` with `GRAMPS_REFRESH_TOKEN`.

Enable read-only mode with the `GRAMPS_READ_ONLY` environment variable:

```bash
docker run -p 8080:8080 \
  -e GRAMPS_API_URL=https://your-gramps.example.com \
  -e GRAMPS_USERNAME=user \
  -e GRAMPS_PASSWORD=pass \
  -e GRAMPS_TREE_ID=uuid \
  -e GRAMPS_READ_ONLY=true \
  gramps-web-mcp
```

The Dockerfile uses multi-stage build (SDK → ASP.NET runtime) and defaults to
HTTP transport on port 8080.

### CI

- **GitHub Actions** (`.github/workflows/ci.yml`): build and test on pushes
  and pull requests to `main`/`master`.
- **GitHub Actions** (`.github/workflows/docker.yml`): on pushes to
  `main`/`master`, `v*` tags, or manual dispatch, test, then build and publish
  the Docker image to `ghcr.io/scormave/gramps-web-mcp` (`:latest` from the
  default branch, `:x.y.z` from a tag). Tag runs also publish `server.json` to
  the MCP Registry.
- **GitHub Actions** (`.github/workflows/mcpb-release.yml`): on `v*` tags,
  pack the Claude Desktop MCPB bundles for five platforms and attach them to
  the GitHub Release.
- **GitHub Actions** (`.github/workflows/pages.yml`): deploy `site/` to GitHub
  Pages when it changes on `main`.
- **Gitea Actions** (`.gitea/workflows/docker.yml`): builds and publishes the
  Docker image to a private Gitea container registry.

## Testing strategy

- **Contract tests** (`Contract/`): verify that C# DTOs match the OpenAPI spec
  (`openapi.json`; four legacy extended schemas use `apispec.yaml`).
  `swagger-dto-map.json` defines the mapping; run with
  `[Trait("Category","Contract")]`.
- **Unit tests** (`UnitTests/`): cover serialization, date parsing, formatters,
  flexible input types, mutation parsing.
- **Integration tests** (`IntegrationTests/`): end-to-end formatter tests with
  realistic JSON fixtures, plus in-process MCP server tests on ASP.NET Core
  `TestServer` with a stubbed Gramps Web API (API key auth, argument checks,
  per-call read cache) and a DI test of the User-Agent sent to Gramps Web.
- **Fixtures** (`Fixtures/`): JSON files representing actual API responses for
  deserialization tests.
