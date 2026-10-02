# Gramps Web API call inventory (DTO mapping)

Call sites are under `GrampsWeb.Mcp/Tools/`, `GrampsWeb.Mcp/Formatters/`, `GrampsWeb.Mcp/Resources/`, `GrampsWeb.Mcp/Health/`, and `GrampsWeb.Mcp/Client/` (`GrampsApiClient`, `GrampsAuthTokenProvider`, `HandleResolver`, `GrampsBatchFetch`, `ExtendedEntityEnrichment`). Paginated browsing of each `GET /api/{type}/` list is exposed only via MCP `list_objects` (and `search`), which formats rows through `SearchFormatter` (shared with search results).

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
| `GET /api/search/?query=…&page=…&pagesize=…&profile=self` | Paged array | `GrampsPagedResult<GrampsSearchHit>` via `GetPagedListAsync<T>`; `pagesize` is clamped to 1–100 and the total comes from `X-Total-Count`. Each hit's `object` carries the person, family, event, or citation `profile` used for its summary line |
| `GET /api/{type}/` (list) | Paged or bare array | `GrampsPagedResult<T>` via `GetPagedListAsync<T>` + `GrampsPagedResultParser`; `list_objects` sends `page`, `pagesize` (1–100), and optional `gramps_id`, `gql`, and `sort`, and adds `profile=self` for people, families, events, and citations. A citation `sourceHandle` becomes `gql=source_handle="…"`, joined with any caller `gql` by `and` |
| `GET /api/{type}/?gramps_id=…&pagesize=1` | Paged or bare array | `HandleResolver` lookup behind every Gramps ID argument; the handle is cached for 10 minutes in `HandleCache` |
| `GET /api/people/{handle}` | Person | `GrampsPerson` (`primary_name`, `alternate_names`) |
| `GET /api/people/{handle}?profile=self` | Person | `GrampsPerson`; `Profile.NameDisplay` heads the `get_object` card in the tree's name display format |
| `GET /api/people/{handle}?extend=all&profile=families` | Person extended | `GrampsPersonExtended`; `Profile` (`GrampsPersonProfile`) heads the card with `NameDisplay` and names parents, spouses, and children for `get_object` |
| `GET /api/{type}/?handles=h1,h2&page=1&pagesize=N` | Array | `T[]` via `GrampsBatchFetch.GetByHandlesAsync` (API 3.14+; up to 50 handles per request, 4 requests at a time; a single handle uses `GET /api/{type}/{h}`; a 400/422 or a reply with objects that were not asked for switches that API URL and tree to per-handle reads for the life of the process) |
| `GET /api/citations/?handles=…&extend=all`, `GET /api/events/?handles=…&extend=place`, `GET /api/media/?handles=…` | Arrays | `ExtendedEntityEnrichment` refills citations, event places, and media of an extended person or family, one batch per type |
| `GET /api/{type}/?handles=…` (`&profile=self` for people, families, events, and citations) | Arrays | `LinkedObjectLabels` names the linked objects of every card for `get_object` that is not extended, and the backlinks of an event, place, source, citation, repository, note, or media card, as in `search` rows: one batch per type, up to 200 objects per type |
| `GET /api/people/{h}?profile=self` (+ `?backlinks=true` when an ancestor root lists no parent family), then `GET /api/people/?handles=…&profile=self[&backlinks=true]` + `GET /api/families/?handles=…` | Derived | `PersonTree` (the root and `PersonTreeRow[]`) for `get_person_tree`: the root, then one batch of each per generation (no `/people/{h}/ancestors` or `/descendants` in OpenAPI) |
| `GET /api/people/{handle}/timeline` | Array | `GrampsTimelineEntry[]` (query: `event_classes`, `relatives`, `relative_event_classes`, `dates`, always `discard_empty=false`; `dates` is normalized from `Y/M/D-Y/M/D`, `-Y/M/D`, `Y/M/D-`, or a single date; without `dates`, `first=false&last=false`, because Gramps' fuzzy matching of an imprecise first event drops relatives' events) |
| `GET /api/relations/{handle1}/{handle2}`, `.../all` | Object, array | `GrampsRelationship`, `GrampsRelationshipItem[]` (closest first); both people and the common ancestors are read with `profile=self` |
| `GET /api/families/{handle}` | Family | `GrampsFamily` |
| `GET /api/families/{handle}?profile=self,events` | Family | `GrampsFamily.Profile` (`GrampsFamilyProfile`): members and events by name for `get_object` |
| `GET /api/families/{handle}?extend=all` | Family extended | `GrampsFamilyExtended` |
| `GET /api/families/{h}/timeline` | Array | `GrampsTimelineEntry[]` (query: `event_classes`, `dates`, always `discard_empty=false`) |
| `GET /api/places/{h}?backlinks=true` + `GET /api/events/?handles=…&profile=participants` | Derived | `GrampsTimelineEntry[]` (MCP synthesizes; no `/places/{h}/timeline` in OpenAPI) |
| `GET /api/events/?handles=…&profile=participants` | Array | `GrampsEvent.Profile.Participants`: the others in the events a person takes part in under a role other than Primary, for person timelines (`OtherParticipants`) and extended people |
| `GET /api/events/{handle}` | Event | `GrampsEvent` |
| `GET /api/events/{handle}?backlinks=true` + `GET /api/people/{h}` per participant | Event card | Participants by name for `get_object` on an event |
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
| `GET ...?backlinks=true` | Backlinks | `JsonElement`; `GrampsPerson.Backlinks` (`GrampsBacklinks`, families only) in `get_person_tree` batches |
| `GET /api/types/default/` | Types | `JsonElement` via `GetDefaultTypesAsync` → `TypesPayloadParser.ParseCategories` (per-category string lists; see `DefaultTypes` in OpenAPI); kept for the process lifetime by `GrampsTypeVocabularies` |
| `GET /api/types/default/{datatype}` | String array | `JsonElement` via `GetDefaultTypesAsync(category)` for type labels, with the bulk endpoint as fallback; kept for the process lifetime |
| `GET /api/types/custom/` | Nested lists | `JsonElement` via `GetCustomTypesAsync` → `TypesPayloadParser.ParseCategories` (same shape as default; see `CustomTypes` in OpenAPI); kept for 10 minutes, read again for `gramps://types` and before rejecting a type in write tools |
| `GET /api/transactions/history/?page=1&pagesize=N&sort=-id` | Paged array | `GrampsPagedResult<GrampsTransaction>` via `GetPagedListAsync<T>` |
| `GET /api/metadata/`, `/api/bookmarks/` | Various | `JsonElement`; `/api/metadata/` also serves the `GET /health` connectivity check (`GrampsHealthService`) |
| `GET /api/places/?pagesize=5&keys=handle,gramps_id,name` | Array | `quick_add_person` / `add_event_to_person` place lookup by exact name (`CompositeTools.ResolveOrCreatePlaceAsync`). Without `page`, Gramps Web returns every place, which this lookup relies on |
| `GET /api/name-formats/`, `/api/name-groups/` | Various | `dynamic` |
| `POST /api/{type}/`, `PUT /api/{type}/{h}` (create/update) | Often JSON array of changes `{ _class, type, old, new }` (not in OpenAPI); may be bare entity | `PostMutationAsync` reads the new handle and Gramps ID from `new` via `GrampsMutationParser`; `PutMutationAsync` only checks the status |
| `GET /api/{type}/{h}?backlinks=true`, then `DELETE /api/{type}/{h}` | Backlinks, empty | `delete_object` (`DeleteHelper`): blocks the delete while backlinks exist unless `force` is set |

High-risk JSON fields (polymorphic or spec vs runtime): `parent_family_list`, `family_list` / `media_list` (handle strings vs `{ref}` / `{handle}` objects), `child_ref_list` (object vs string), `reporef_list` (object vs string), search root (array vs wrapped), list endpoints (array vs `{ objects, total, page }`).

Shared handling: `GrampsWeb.Mcp/Serialization/`, `GrampsJson.Options` (`UnmappedMemberHandling.Skip`), and unit tests under `GrampsWeb.Mcp.Tests/Fixtures/`.
