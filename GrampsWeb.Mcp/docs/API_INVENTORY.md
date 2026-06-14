# Gramps Web API call inventory (DTO mapping)

Call sites are under `GrampsWeb.Mcp/Tools/`, `GrampsWeb.Mcp/Formatters/`, and `GrampsWeb.Mcp/Client/GrampsApiClient.cs`. Paginated browsing of each `GET /api/{type}/` list is exposed only via MCP `list_objects` (and `search`), which formats rows through `SearchFormatter` (shared with search results).

**Vendored OpenAPI spec:** repo-root `apispec.yaml` is the upstream Gramps Web OpenAPI spec. **Do not edit it in place** — when the upstream spec changes, replace the file wholesale from Gramps Web.

**Contract checks:** `dotnet test` runs `[Trait("Category","Contract")]` tests that compare `[JsonPropertyName]` on mapped DTOs to `definitions` in repo-root `apispec.yaml`. The enforced mapping is `GrampsWeb.Mcp.Tests/Contract/swagger-dto-map.json` (update it when adding typed API surfaces). The test project copies `apispec.yaml` into the build output next to `swagger-dto-map.json`.

**Read-only mode:** when `GRAMPS_READ_ONLY=true` is set,
`PostMutationAsync`, `PutMutationAsync`, and `DeleteAsync` block tree mutations
before sending HTTP requests. Authentication POSTs (`/api/token/` and
`/api/token/refresh/`) and binary media GETs are still allowed because they do
not mutate tree data.

| HTTP path pattern | Response / body type | Model / notes |
|-------------------|----------------------|----------------|
| `GET /api/search/` | `JsonElement` → parsed hits | `GrampsSearchHit[]` via `SearchTools.ParseSearchHits` (response root is typically a JSON array) |
| `GET /api/{type}/` (list) | Paged or bare array | `GrampsPagedResult<T>` via `GetPagedListAsync<T>` + `GrampsPagedResultParser` |
| `GET /api/people/{handle}` | Person | `GrampsPerson` (`primary_name`, `alternate_names`) |
| `GET /api/people/{handle}?extend=all` | Person extended | `GrampsPersonExtended` |
| `GET /api/people/{h}/ancestors`, `/descendants` | Array | `GrampsPerson[]` |
| `GET /api/people/{handle}/timeline` | Array | `GrampsTimelineEntry[]` (query: `events`, `relatives`, `relativeEvents`, `dates`) |
| `GET /api/relations/{handle1}/{handle2}` | Object | `JsonElement` |
| `GET /api/families/{handle}` | Family | `GrampsFamily` |
| `GET /api/families/{handle}?extend=all` | Family extended | `GrampsFamilyExtended` |
| `GET /api/families/{h}/timeline` | Array | `GrampsTimelineEntry[]` |
| `GET /api/places/{h}?backlinks=true` + `GET /api/events/{eh}` | Derived | `GrampsTimelineEntry[]` (MCP synthesizes; no `/places/{h}/timeline` in apispec) |
| `GET /api/events/{handle}` | Event | `GrampsEvent` |
| `GET /api/places/{handle}` | Place | `GrampsPlace` |
| `GET /api/sources/{handle}` | Source | `GrampsSource` |
| `GET /api/citations/{handle}` | Citation | `GrampsCitation` |
| `GET /api/repositories/{handle}` | Repository | `GrampsRepository` |
| `GET /api/notes/{handle}` | Note | `GrampsNote` |
| `GET /api/media/{handle}` | Media | `GrampsMedia` |
| `GET /api/media/{handle}/thumbnail/{size}` | Binary | MCP resource `gramps://media/{handle}/thumbnail/{size}` via `GetBytesAsync`; opt-in safeguards apply |
| `GET /api/media/{handle}/file` | Binary | MCP resource `gramps://media/{handle}/file` via `GetBytesAsync`; opt-in safeguards apply |
| `GET /api/tags/{handle}` | Tag | `GrampsTag` |
| `GET ...?backlinks=true` | Backlinks | `JsonElement` |
| `GET /api/types/default/` | Types | `JsonElement` → `TypesPayloadParser.ParseCategories` (per-category string lists; see `DefaultTypes` in apispec) |
| `GET /api/types/custom/` | Nested lists | `JsonElement` → `TypesPayloadParser.ParseCategories` (same shape as default; see `CustomTypes` in apispec) |
| `GET /api/metadata/`, `/api/transactions/history/`, `/api/bookmarks/` | Various | `JsonElement` |
| `GET /api/name-formats/`, `/api/name-groups/` | Various | `dynamic` |
| `POST/PUT /api/{type}/` (create/update) | Often JSON array of changes `{ _class, type, old, new }` (not in apispec); may be bare entity | `PostMutationAsync` / `PutMutationAsync` unwrap `new` via `GrampsMutationParser` into `Gramps*` |

High-risk JSON fields (polymorphic or spec vs runtime): `parent_family_list`, `family_list` / `media_list` (handle strings vs `{ref}` / `{handle}` objects), `child_ref_list` (object vs string), `reporef_list` (object vs string), search root (array vs wrapped), list endpoints (array vs `{ objects, total, page }`).

Shared handling: `GrampsWeb.Mcp/Serialization/`, `GrampsJson.Options` (`UnmappedMemberHandling.Skip`), and unit tests under `GrampsWeb.Mcp.Tests/Fixtures/`.
