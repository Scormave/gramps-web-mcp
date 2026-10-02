# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- Add SixLabors.ImageSharp 3.1.12 to render media thumbnails; it is fully
  managed, so the Docker image and MCP Bundle need no native libraries
- Add `THIRD-PARTY-NOTICES.txt` with the licenses of the bundled NuGet packages
  and the .NET runtime. MCP Bundles and the Docker image now include it and
  `LICENSE`, and a test fails when the restored packages and the list differ

### Changed

- Render `read_media` thumbnails and `gramps://media/{handle}/thumbnail/{size}`
  on the server from the original file instead of the Gramps Web thumbnail
  endpoint, which always returns AVIF that MCP clients cannot display. Previews
  are JPEG, or PNG when the image has transparency, follow EXIF orientation,
  are never upscaled, and are re-encoded from pixels so EXIF, GPS, XMP, IPTC,
  and ICC metadata are not sent
- Default the thumbnail size to 1568 pixels on the long edge, up from 256, so
  handwriting and small print in document scans stay legible; `size` accepts
  1 to 4096
- Render thumbnails from JPEG, PNG, GIF, WebP, BMP, TIFF, TGA, PBM, and QOI
  originals, using the first page of multi-page files; reject PDF, audio,
  AVIF, HEIC, and SVG media before downloading the original
- Download originals for thumbnails up to 50 MiB, or `GRAMPS_MEDIA_MAX_BYTES`
  if larger, and up to 100 megapixels; `GRAMPS_MEDIA_MAX_BYTES` still limits
  the preview returned to the client
- Return media files of any MIME type in `read_media` file mode and the file
  resource; images other than JPEG, PNG, GIF, and WebP are returned as an
  embedded blob with a hint to use thumbnail mode
- Update the README, security and privacy policies, architecture, developer,
  migration, and tool documentation, the system prompt, Docker Compose, MCPB
  metadata, and GitHub Pages for the new media behavior
- Name relatives in `get_timeline` rows with their Gramps ID and age, show the
  role when the person is not the primary participant, and list the people and
  families of each event on place timelines with their roles. A zero age, such
  as "0 days" on birth rows, is left out
- Name the place once above a place timeline, with its Gramps ID, instead of
  on every row
- Show a family's parents and children in `get_object` by name, Gramps ID,
  birth, and death, with the marriage, the divorce, and each event's type,
  date, and place, from one request instead of handles
- Show the parents, spouse, and children in each family of an extended person
  `get_object` by name, Gramps ID, birth, and death, with the family's Gramps
  ID, marriage, and divorce, from the person request instead of handles
- Show a place's enclosing places and hierarchy in `get_object` by name, type,
  and Gramps ID from the same request instead of one request per parent
- Show the page count and total in `search` results, and the total when the
  page is past the last one
- Show `get_recent_changes` as one entry per transaction with its UTC time,
  description, user, undo flag, and each changed object's class, change kind,
  and handle, up to 10 objects per transaction, instead of raw JSON
- Show `get_relations` with both people by name, the closest relationship as a
  sentence, generations to the common ancestor, and every relationship with
  its common ancestors by name, instead of raw JSON; the two people load in
  parallel and the common ancestors in one request
- Load place timeline events, each `get_person_tree` generation, and
  `get_relations` common ancestors in batches of up to 50 objects with the list
  endpoint's `handles` filter (Gramps Web API 3.14+), instead of one request
  per object. Servers that reject or ignore the filter get one request per
  object, up to 4 at a time, and the server answer is remembered per API URL
  and tree
- Read a place once per place timeline instead of twice
- Build `get_person_tree` rows from the person profile in the batch instead of
  reading each person's birth and death events and places; an ancestor tree
  takes about two requests per generation instead of several per person
- Label baptism, christening, burial, and cremation dates by event type in
  `get_person_tree` rows, family members, and `get_relations`, e.g.
  "burial 1950", instead of showing them as birth and death
- Build `search` and `list_objects` lines for people, families, events, and
  citations from the `profile=self` summary in the same response instead of
  reading each person's birth event and place, each family's parents, each
  event's place, and each citation's source; a search page takes one request
  plus type labels instead of up to three per hit. Hits without a usable
  embedded object are read in one batch per type
- Show people in `search` and `list_objects` as "Surname, Given, b. 1815 in
  London, d. 1852" in the tree's name display format, with death and baptism
  or burial fallbacks, instead of "Given Surname (b. 1815 in London)"; family
  partners use the display format too
- Keep type vocabularies between tool calls instead of reading them in every
  call: default types for the life of the process and custom types for 10
  minutes; failed reads are not kept. After the first call, an extended person
  `get_object` takes 2 requests instead of 10 and an event `list_objects` page
  1 instead of 2
- Read custom types each time in `gramps://types` and `get_reference(topic:
  "types")`, and list the default types when the custom types endpoint fails
  instead of failing

### Removed

- Remove `GRAMPS_MEDIA_ALLOWED_MIME_TYPES` and the MCPB "Allowed MIME types"
  setting; access is controlled by `GRAMPS_MEDIA_RESOURCES_ENABLED`,
  `GRAMPS_MEDIA_ALLOW_PRIVATE`, and `GRAMPS_MEDIA_MAX_BYTES`. The server
  ignores the variable and logs a startup warning while it is set

### Fixed

- Resolve media Gramps IDs by the Gramps default `O` prefix instead of `M`, so
  `get_object`, `read_media`, `update_media`, and `delete_object` accept IDs
  like `O0001`
- Filter `list_objects` citations by `sourceHandle` through a Gramps QL
  `source_handle` condition, combined with any `gql`; Gramps Web has no
  `source_handle` query parameter and returned HTTP 422
- Show birth and death from fallback events only where the person has the
  Primary role, so a parent no longer shows a child's birth as their own
- Report server errors, network errors, and timeouts during Gramps ID
  resolution instead of treating them as a missing object; only HTTP 400, 404,
  and 422 still fall back to the original value
- Show relatives' names in `get_timeline`; the server read a `name` field that
  Gramps Web does not send, so relatives' events were unnamed
- Leave a missing type, date, or place out of `search` event lines instead of
  showing dashes
- Sort `get_timeline` events dated before the year 1000 by year; they were
  listed after the undated events. Place timeline events of the same year
  follow their dates instead of the order of the place's backlinks
- Drop the separator of an empty name part from Gramps display names, so a
  person without a surname shows as "Anna" instead of ", Anna" in trees,
  families, relationships, and timelines
- Name the `objectType` parameter in the `list_objects` error for an unknown
  type instead of `object_type`
- Log in to Gramps Web once instead of twice at startup: the connectivity
  check and `GET /health` take their token from the token cache the tools use.
  The first tool call no longer logs in again and hits the rate limit on
  `/api/token/` (HTTP 429), and health probes no longer log in each time. A
  check that gets HTTP 401 for a cached token logs in again once
- Accept a custom type added in Gramps after the server read the types: write
  tools read custom types again before rejecting a type, instead of rejecting
  it for up to 10 minutes

## [2.0.1] - 2026-10-01

### Added

- Add `GRAMPS_LOG_LEVEL` to set the log level for server messages; blank,
  unknown, or unresolved values fall back to `Information`

### Changed

- Log only method, path without query string, status, duration, and body
  length for Gramps API calls at `Information`; log request and response
  bodies and full URLs, which contain genealogy data, only at `Debug` or `Trace`
- Document `GRAMPS_LOG_LEVEL` in README, Docker Compose, the Dockerfile, MCP
  Registry metadata, MCPB docs, and GitHub Pages; correct the data retention
  section of the privacy policy

### Fixed

- Treat unresolved `${...}` placeholders from blank optional MCPB fields as
  unset, so a blank refresh token no longer makes every request fail with
  HTTP 422
- Fall back to password login when Gramps Web rejects a refresh token with
  HTTP 422 as well as 401
- Request the first page in `get_recent_changes` so `pagesize` applies instead
  of returning the entire history

## [2.0.0] - 2026-10-01

### Added

- Vendor the generated Gramps Web API 3.22.1 OpenAPI 3 schema and use it for
  DTO contract checks, retaining the older Swagger 2 schema only for four
  omitted extended definitions
- Add a migration guide from 1.x with all removed tool mappings, changed
  arguments, link-update semantics, and tool availability checks
- Document `linkMode` in the existing input-guide resource and expose it as
  `get_reference(topic: "input-guide", section: "link_updates")`; add
  `change-link` and `cite-fact` MCP prompts
- Add `linkMode` (`replace`, `add`, `remove`) to the nine update tools with
  link lists, preserving existing metadata when adding references; update tool
  descriptions, MCP guidance, README, and catalog examples
- Support `GRAMPS_REFRESH_TOKEN` authentication without a username or password,
  including instances with local password authentication disabled

### Changed

- Include place-only birth/death details in `add-person` and make
  `research-person` request timelines and tree branches only when relevant
- Make person and family creation hints context-aware, including birth/death
  events created by `quick_add_person`, without additional API reads; avoid
  suggesting duplicate events and preserve full-list update warnings
- Replace the ten entity-specific delete tools with `delete_object`, which selects
  the record type through `objectType` while retaining backlink protection and
  explicit `force` handling
- Replace ten entity-specific read tools and `find_by_gramps_id` with
  `get_object`; it infers the object type from a Gramps ID prefix and requires
  `objectType` only for opaque handles
- Consolidate generic read and delete operations in `ObjectTools.cs`, including
  their unit tests
- Replace four compatibility reference tools with `get_reference`, whose `topic`
  selects the resource and whose optional `section` limits input-guide or type
  results to the needed data
- Replace `get_ancestors` and `get_descendants` with `get_person_tree`, whose
  `direction` selects ancestors or descendants
- Replace person, family, and place timeline tools with `get_timeline`, whose
  `objectType` selects the timeline owner and limits relative-event filters to people
- Replace `get_media_thumbnail` and `get_media_file` with `read_media`, defaulting
  to thumbnail mode (256 pixels); file mode returns typed image, audio, or blob
  content and rejects the thumbnail-only `size` parameter. Media resource URIs
  and size, MIME, and private-record safeguards are unchanged
- Reduce the MCP catalog from 57 to 31 tools and update the GitHub Pages site,
  MCP Bundle metadata, prompts, and tool documentation
- Publish only read-only tools when `GRAMPS_READ_ONLY=true`, while retaining
  server-side protection against direct calls to write tools
- Hide `read_media` from `tools/list` when
  `GRAMPS_MEDIA_RESOURCES_ENABLED=false`, while retaining its direct-call
  configuration checks and media metadata access through `get_object`
- Expose refresh-token authentication in Docker Compose, MCP Registry, MCPB,
  GitHub Pages, and the remaining setup documentation
- Deduplicate identical JSON reads within each read-only MCP tool call,
  including concurrent requests; discard the cache after the call and allow
  retries after failed requests without caching write-tool reads
- Load only the type-label categories needed by search results
- Reuse embedded search objects when their handles and required summary fields
  are valid, falling back to detail endpoints for missing or incompatible data;
  preserve sequential processing and result order

### Fixed

- Preserve explicit empty arrays in PUT bodies so removing the last link sends
  an empty list to Gramps Web
- Serialize update read/modify/write sequences and `add_event_to_person` within
  the process when the mutation gate is enabled, retaining per-write throttling
- Calculate paginated-list page totals and row numbers from the requested page
  size, including a partial final page
- Update create-response guidance to use current `eventRefs` and `childRefs`
  parameters, and explain that updates replace full reference lists
- Send refresh tokens in the Bearer authorization header instead of the JSON
  body, avoiding failed token refreshes and unnecessary password logins
- Replace raw HTML from search HTTP 500 errors with guidance to simplify the
  query, use `list_objects`, or check the Gramps Web logs and search index

## [1.0.8] - 2026-09-18

### Fixed

- Send an application name and build version in the `User-Agent` header on all Gramps Web requests, including authentication, media downloads, and health checks ([#2](https://github.com/Scormave/gramps-web-mcp/issues/2))

### Changed

- Upgrade the server and tests to .NET 10 LTS, including SDK selection, CI, MCPB build instructions, and GitHub Pages documentation
- Move Docker build and runtime images to .NET 10 on Ubuntu 24.04 (Noble)
- Clarify runtime modes and configuration defaults in the README

## [1.0.7] - 2026-08-17

### Added

- Optional HTTP/SSE authentication via `MCP_API_KEY` (Bearer or `X-Api-Key` header); startup warning when MCP is reachable on a non-loopback bind without a key
- In-process mutation write policy: serialize create/update/delete HTTP calls by default (`GRAMPS_MUTATION_SERIALIZE`), optional `GRAMPS_MUTATION_MIN_INTERVAL_MS` pacing, and retryable MCP errors for SQLite `database is locked` and upstream HTTP 429
- Linux MCPB release artifacts (`linux-x64`, `linux-arm64`) alongside macOS and Windows bundles

### Changed

- `.env.example` and `docker-compose.example.yml` document optional media and transport settings
- `server.json`, `README.md`, and release docs clarify why the registry package declares `stdio` while the Docker image defaults to HTTP
- MCP Registry publish workflow updates all OCI package identifiers in `server.json`

## [1.0.6] - 2026-07-15

### Added

- Place hierarchy in `get_place` (cycle-safe walk up to 6 levels), dated parent enclosure refs, and alternate names with language/date on create and update
- Date parser accepts ISO day/month ranges, mixed-precision dashes (`1703-1914-08-31`), open-ended forms, richer `between` / `from`…`to` sides, and explicit `from DATE` / `to DATE` (Gramps From/To modifiers)
- Date parser accepts English month forms matching tool output (`1 Jul 1919`, `July 1919`, `from 1 Oct 1929 to 27 Sep 1937`)
- Media list and search rows show the Gramps description alongside the filename when both are present
- GitHub Pages landing site (`site/`) with deploy workflow

### Fixed

- Place hierarchy in `get_place` lists parent places only (subject place no longer duplicated from the header)
- Empty enclosure and alternate-name dates no longer render as blank `[]` brackets in place output

### Changed

- Ambiguous dash dates default to span/From for places; events, citations, and media use range/After via `DateIntervalPreference`
- Place write inputs rename `enclosedByHandles` to `enclosedBy` (handles, objects, or `HANDLE::date`); empty-list semantics: omit = keep, `[]` = clear
- Place tool docs and `get_input_guide` clarify `enclosedBy` and dated alternate-name / enclosure examples
- Unrecognized agent date strings now fail validation instead of being stored as Gramps text-only dates

## [1.0.5] - 2026-07-06

### Added

- `GetMediaFile` returns typed MCP content blocks (image, audio, or embedded blob) for allowlisted MIME types instead of rejecting non-image media

### Fixed

- Re-authenticate automatically when token refresh returns 401 after Gramps Web restarts invalidate cached refresh tokens
- Parse stringified JSON `primaryName` and alternate name values before falling back to plain name lines, so structured name parts (patronymic, prefix, connector, title) are preserved
- Resolve surname origin labels from Gramps Web default and custom `name_origin_types` instead of hardcoded mappings
- Accept camelCase and semantic alias JSON on structured ref inputs (`callNumber`, `noteList`, `relationship`, etc.); outbound API payloads still use snake_case

### Changed

- Console logging uses a minimal one-line format (`info: message`) without category names; framework logs below Warning are suppressed to reduce `/health` probe noise

## [1.0.4] - 2026-06-28

### Fixed

- Tools documented Gramps ID support via handle parameters but passed IDs like `I0002` straight to `/api/.../{handle}`, causing false "not found" errors while `find_by_gramps_id` worked; advertised handle inputs now resolve through `HandleResolver` before API calls
- Console logging on stdio transport wrote to stdout, so Claude Desktop and other MCP clients tried to parse startup and HTTP log lines as JSON-RPC; all console logs now go to stderr

### Changed

- Gramps ID to handle resolution is cached in memory (10-minute TTL, scoped per API URL and tree) to avoid repeated list-endpoint queries during multi-step agent workflows

## [1.0.3] - 2026-06-27

### Fixed

- Shared JWT token cache across concurrent API clients so parallel MCP tool calls no longer each POST `/api/token/` and hit Gramps Web rate limits (HTTP 429)
- Included date in `get_media` output (was omitted from the formatter despite being present in the API response)

### Changed

- Reduced `/health` response to a minimal `{ "status": "healthy" | "unhealthy" }` payload instead of exposing API URL, tree, database, and Gramps version to unauthenticated callers
- Docker release workflow now runs `dotnet test` before building and pushing the container image (GitHub Actions and Gitea Actions)

## [1.0.2] - 2026-06-26

### Added

- Claude Desktop MCPB (`.mcpb`) packaging with self-contained binaries for macOS and Windows
- Tag-driven GitHub Release workflow for per-platform desktop extension bundles
- [`PRIVACY.md`](PRIVACY.md) and MCPB manifest for Connectors Directory readiness
- Explicit MCP tool annotations (`title`, `readOnlyHint`, `destructiveHint`) on all 57 tools

### Changed

- Upgraded `ModelContextProtocol` packages to 1.3.0 for tool annotation support

## [1.0.1] - 2026-06-24

### Added

- `GET /health` HTTP endpoint for Docker `HEALTHCHECK` and Unraid monitoring
- Startup log line confirming Gramps Web connectivity (`Connected to Gramps Web at …`)
- [`docker-compose.example.yml`](docker-compose.example.yml) stack for Gramps Web + MCP on one network
- MCP Registry metadata and automated publishing on version tags
- OCI image ownership label for MCP Registry validation

### Changed

- Docker release workflow now publishes the MCP Registry entry after tagged image builds

## [1.0.0] - 2026-06-23

### Added

- Initial public release of the Gramps Web MCP server (.NET 8)
- 57 MCP tools for people, families, events, places, sources, citations, notes,
  media, repositories, tags, search, and composite workflows
- MCP resources (`gramps://input-guide`, `gramps://types`, `gramps://metadata`,
  `gramps://name-settings`) and compatibility tools
- Opt-in binary MCP resources for media thumbnails and full files, guarded by
  size limits, MIME allowlists, and private-record defaults.
- Open WebUI-compatible media image tools for thumbnail and full-file photo
  analysis when MCP resources are not available to the client.
- MCP prompts for common genealogy workflows
- stdio, Streamable HTTP, and legacy SSE transports
- Read-only mode that keeps tools visible while blocking mutation calls
- Docker image published to `ghcr.io/scormave/gramps-web-mcp`
- Contract tests against vendored Gramps Web OpenAPI spec

[Unreleased]: https://github.com/Scormave/gramps-web-mcp/compare/v2.0.1...HEAD
[2.0.1]: https://github.com/Scormave/gramps-web-mcp/compare/v2.0.0...v2.0.1
[2.0.0]: https://github.com/Scormave/gramps-web-mcp/compare/v1.0.8...v2.0.0
[1.0.8]: https://github.com/Scormave/gramps-web-mcp/compare/v1.0.7...v1.0.8
[1.0.7]: https://github.com/Scormave/gramps-web-mcp/compare/v1.0.6...v1.0.7
[1.0.6]: https://github.com/Scormave/gramps-web-mcp/compare/v1.0.5...v1.0.6
[1.0.5]: https://github.com/Scormave/gramps-web-mcp/compare/v1.0.4...v1.0.5
[1.0.4]: https://github.com/Scormave/gramps-web-mcp/compare/v1.0.3...v1.0.4
[1.0.3]: https://github.com/Scormave/gramps-web-mcp/compare/v1.0.2...v1.0.3
[1.0.2]: https://github.com/Scormave/gramps-web-mcp/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/Scormave/gramps-web-mcp/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/Scormave/gramps-web-mcp/releases/tag/v1.0.0
