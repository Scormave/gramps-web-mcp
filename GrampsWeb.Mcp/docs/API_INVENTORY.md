# Gramps Web API call inventory (DTO mapping)

Call sites are under `GrampsWeb.Mcp/Tools/`, `GrampsWeb.Mcp/Formatters/`, and `GrampsWeb.Mcp/Client/GrampsApiClient.cs`. Paginated browsing of each `GET /api/{type}/` list is exposed only via MCP `list_objects` (and `search`), which formats rows through `SearchFormatter` (shared with search results).

**Current OpenAPI spec:** repo-root `openapi.json` is a verbatim snapshot of
the generated [Gramps Web demo schema](https://demo.grampsweb.org/api/openapi.json),
API 3.22.1 (OpenAPI 3.0.3), retrieved 2026-10-01. Refresh it wholesale from
the live endpoint after verifying the reported API version. The upstream
[static Swagger UI](https://gramps-project.github.io/gramps-web-api/) still
serves the older Swagger 2.0 API 3.7.2 schema, retained here as `apispec.yaml`
for four legacy `*Extended` definitions omitted by the generated schema.

**Contract checks:** `dotnet test` compares mapped DTO JSON names against
`components.schemas` in `openapi.json`. Only `PersonExtended`,
`FamilyExtended`, `EventExtended`, and `CitationExtended` fall back to
`apispec.yaml`. The enforced mapping is
`GrampsWeb.Mcp.Tests/Contract/swagger-dto-map.json`; update it when adding
typed API surfaces. Structural checks do not prove runtime response shapes or
endpoint behavior.

**Read-only mode:** when `GRAMPS_READ_ONLY=true` is set,
`PostMutationAsync`, `PutMutationAsync`, and `DeleteAsync` block tree mutations
before sending HTTP requests. Authentication POSTs (`/api/token/` and
`/api/token/refresh/`) and binary media GETs are still allowed because they do
not mutate tree data. When writes are allowed, those three helpers also go
through `MutationGate` (`GRAMPS_MUTATION_SERIALIZE`,
`GRAMPS_MUTATION_MIN_INTERVAL_MS`) around the HTTP send only.

**Media catalog:** when `GRAMPS_MEDIA_RESOURCES_ENABLED=false`, the
`read_media` MCP tool is omitted from `tools/list`. Its binary API routes and
MCP resources remain disabled by the same setting; metadata remains readable.

| HTTP path pattern | Response / body type | Model / notes |
|-------------------|----------------------|----------------|
| `GET /api/search/` | Paged array | `GrampsPagedResult<GrampsSearchHit>` via `GetPagedListAsync<T>`; the total comes from `X-Total-Count` |
| `GET /api/{type}/` (list) | Paged or bare array | `GrampsPagedResult<T>` via `GetPagedListAsync<T>` + `GrampsPagedResultParser` |
| `GET /api/people/{handle}` | Person | `GrampsPerson` (`primary_name`, `alternate_names`) |
| `GET /api/people/{handle}?extend=all` | Person extended | `GrampsPersonExtended` |
| `GET /api/{type}/?handles=h1,h2&page=1&pagesize=N` | Array | `T[]` via `GrampsBatchFetch.GetByHandlesAsync` (API 3.14+; up to 50 handles per request; per-handle `GET /api/{type}/{h}` fallback) |
| `GET /api/people/{h}/ancestors`, `/descendants` | Array | `GrampsPerson[]` |
| `GET /api/people/{handle}/timeline` | Array | `GrampsTimelineEntry[]` (query: `events`, `relatives`, `relativeEvents`, `dates`) |
| `GET /api/relations/{handle1}/{handle2}` | Object | `JsonElement` |
| `GET /api/families/{handle}` | Family | `GrampsFamily` |
| `GET /api/families/{handle}?profile=self,events` | Family | `GrampsFamily.Profile` (`GrampsFamilyProfile`): members and events by name for `get_object` |
| `GET /api/families/{handle}?extend=all` | Family extended | `GrampsFamilyExtended` |
| `GET /api/families/{h}/timeline` | Array | `GrampsTimelineEntry[]` |
| `GET /api/places/{h}?backlinks=true` + `GET /api/events/?handles=…&profile=participants` | Derived | `GrampsTimelineEntry[]` (MCP synthesizes; no `/places/{h}/timeline` in OpenAPI) |
| `GET /api/events/{handle}` | Event | `GrampsEvent` |
| `GET /api/places/{handle}` | Place | `GrampsPlace` |
| `GET /api/places/{handle}?profile=self` | Place | `GrampsPlace.Profile` (`GrampsPlaceProfile`): enclosing places by name for `get_object` |
| `GET /api/sources/{handle}` | Source | `GrampsSource` |
| `GET /api/citations/{handle}` | Citation | `GrampsCitation` |
| `GET /api/repositories/{handle}` | Repository | `GrampsRepository` |
| `GET /api/notes/{handle}` | Note | `GrampsNote` |
| `GET /api/media/{handle}` | Media | `GrampsMedia` |
| `GET /api/media/{handle}/file` | Binary | MCP resources `gramps://media/{handle}/file` and `gramps://media/{handle}/thumbnail/{size}` via `GetBytesAsync`; thumbnails are rendered locally by `MediaPreviewRenderer`; opt-in safeguards apply |
| `GET /api/media/{handle}/thumbnail/{size}` | Binary | Not used: Gramps Web always returns AVIF, which MCP clients cannot display |
| `GET /api/tags/{handle}` | Tag | `GrampsTag` |
| `GET ...?backlinks=true` | Backlinks | `JsonElement` |
| `GET /api/types/default/` | Types | `JsonElement` → `TypesPayloadParser.ParseCategories` (per-category string lists; see `DefaultTypes` in OpenAPI) |
| `GET /api/types/custom/` | Nested lists | `JsonElement` → `TypesPayloadParser.ParseCategories` (same shape as default; see `CustomTypes` in OpenAPI) |
| `GET /api/transactions/history/?page=1&pagesize=N&sort=-id` | Paged array | `GrampsPagedResult<GrampsTransaction>` via `GetPagedListAsync<T>` |
| `GET /api/metadata/`, `/api/bookmarks/` | Various | `JsonElement` |
| `GET /api/name-formats/`, `/api/name-groups/` | Various | `dynamic` |
| `POST/PUT /api/{type}/` (create/update) | Often JSON array of changes `{ _class, type, old, new }` (not in OpenAPI); may be bare entity | `PostMutationAsync` / `PutMutationAsync` unwrap `new` via `GrampsMutationParser` into `Gramps*` |

High-risk JSON fields (polymorphic or spec vs runtime): `parent_family_list`, `family_list` / `media_list` (handle strings vs `{ref}` / `{handle}` objects), `child_ref_list` (object vs string), `reporef_list` (object vs string), search root (array vs wrapped), list endpoints (array vs `{ objects, total, page }`).

Shared handling: `GrampsWeb.Mcp/Serialization/`, `GrampsJson.Options` (`UnmappedMemberHandling.Skip`), and unit tests under `GrampsWeb.Mcp.Tests/Fixtures/`.
