# MCP Tool Catalog

Complete catalog of the 32 MCP tools exposed by the server.
Tools are grouped by Gramps entity type.  Each tool is a static method
decorated with `[McpServerTool]`.

> `GrampsApiClient client` is injected by the MCP host and is **not** a caller-supplied argument.

## Legend

| Symbol | Meaning |
|--------|---------|
| R | Read-only tool |
| C | Create (write) |
| U | Update (write) |
| D | Delete (destructive write) |

Read-only mode (`GRAMPS_READ_ONLY=true`) publishes only the read-only tools.
Direct calls to a previously known write-tool name still return an MCP error
before any mutation request is sent to Gramps Web.
Binary media resources are read-only GETs and are not blocked by read-only mode.

Create/update/delete HTTP calls are serialized in-process by default
(`GRAMPS_MUTATION_SERIALIZE=true`) and may wait
`GRAMPS_MUTATION_MIN_INTERVAL_MS` between writes. That interval applies to
**each** mutation, so composite tools such as `quick_add_person` and
`add_event_to_person` can take several pauses in one call. The policy is
in-process only; it does not coordinate with the Gramps Web UI or other
API clients. SQLite lock and upstream 429 failures return a retryable MCP
error instead of a generic 500.

## Resources

Read-only reference/discovery data exposed as MCP resources:

| URI | Description |
|-----|-------------|
| `gramps://input-guide` | Complete write-input guide: date strings, structured fields, and full name schema |
| `gramps://types` | Built-in and custom type vocabularies (event/place/note/etc.) |
| `gramps://metadata` | Connection/tree metadata (API version, tree id/name, owner, default person) |
| `gramps://name-settings` | Name display formats and surname grouping rules |
| `gramps://media/{handle}/thumbnail/{size}` | Opt-in binary thumbnail bytes for a media record; recommended for vision agents |
| `gramps://media/{handle}/file` | Opt-in full media file bytes, subject to size, MIME, and private-record safeguards |

Compatibility note: reference payloads are also available through
`get_reference` for clients without native MCP resource reading support.
Media thumbnails/files are also available as image-content tools for clients
such as Open WebUI that handle tool images better than MCP resources.

## Reference (`ReferenceTools.cs`) — 1 tool

### R — `GetReference`
Read-only compatibility access to one reference resource. `topic` selects the
payload; `section` reduces the response when only one part is needed.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `topic` | `string` | yes | `input-guide`, `types`, `metadata`, or `name-settings` |
| `section` | `string?` | no | For `input-guide`: `dates`, `name_schema`, `structured_fields`, or `structured_fields.names`, `.attributes`, `.urls`, `.addresses`, `.person_associations`, `.repository_refs`. For `types`: one returned category key such as `event_types`. For `name-settings`: `formats` or `groups`. Unsupported for `metadata`. |

## Prompts

Workflow templates exposed as MCP prompts (`Prompts/GrampsPrompts.cs`).  Each prompt expands to a user-role chat message that guides an agent through typical Gramps Web MCP tool usage.

| Name | Parameters | Purpose |
|------|------------|---------|
| `add-person` | `name`, `gender` (default Unknown), optional `birthDate`, `birthPlace`, `deathDate`, `deathPlace` | Add a new person with optional birth/death details; instructs use of `quick_add_person` and confirmation with handle and Gramps ID. |
| `research-person` | `person` (handle, Gramps ID such as I0001, or name) | Build a full dossier: resolve identity, `get_object` for the person with extended=true, timeline, and ancestor/descendant trees (3 generations each), then present a structured biographical summary. |
| `add-family` | optional `father`, `mother`, `relationship` (default Married), optional `marriageDate`, `marriagePlace` | Create a couple family: verify or find parents, `create_family`, optionally marriage event via `create_event` and `update_family`, then `get_object` for the family. |
| `find-connections` | `person1`, `person2` (name, handle, or Gramps ID) | Resolve both handles, `get_relations`, explain kinship or compare ancestor trees with `get_person_tree` if no direct link. |
| `import-from-text` | `text` | Parse free-form genealogy text: search/create people with `quick_add_person`, `create_family`, `add_event_to_person`, sources/citations as needed, then report import summary and gaps. |

---

## Object tools (`ObjectTools.cs`) — 2 tools

### R — `GetObject`
Fetch one record by handle or Gramps ID. A Gramps ID determines its type from
its prefix; an opaque handle requires `objectType`. `extended=true` resolves
linked details for people and families only.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `identifier` | `string` | yes | — | Object handle or Gramps ID, such as `I0001` |
| `objectType` | `string?` | no | — | Required for an opaque handle; inferred from a Gramps ID. `person`, `family`, `event`, `place`, `source`, `citation`, `note`, `media`, `repository`, or `tag` |
| `extended` | `bool` | no | `false` | For people and families only, resolve linked details inline |

### D — `DeleteObject`
Delete a person, family, event, place, source, citation, note, media record,
repository, or tag. The server checks backlinks and blocks deletion unless
`force=true`; forcing may leave dangling references. Deleting a media record
does not necessarily delete its file on disk.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `objectType` | `string` | yes | — | `person`, `family`, `event`, `place`, `source`, `citation`, `note`, `media`, `repository`, or `tag` |
| `handle` | `string` | yes | — | Object handle or Gramps ID, such as `I0001` |
| `force` | `bool` | no | `false` | Delete despite backlinks |

---

## Timeline (`TimelineTools.cs`) — 1 tool

### R — `GetTimeline`
Chronological events for a person, family, or place. Person timelines can also
include relatives' events. Place timelines are computed from direct event
backlinks; child places are not included.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `objectType` | `string` | yes | — | `person`, `family`, or `place` |
| `identifier` | `string` | yes | — | Object handle or Gramps ID |
| `events` | `string[]?` | no | all | Event categories: vital, family, religious, vocational, academic, travel, legal, residence, other, custom |
| `relatives` | `string[]?` | no | none | Person only: father, mother, brother, sister, wife, husband, son, daughter |
| `relativeEvents` | `string[]?` | no | none | Person only: event categories for relatives |
| `dates` | `string?` | no | — | Date range `YYYY/M/D-YYYY/M/D` |

---

## Person (`PersonTools.cs`) — 4 tools

### R — `GetPersonTree`
List either ancestors or descendants up to N generations with names, vital
dates/places, and optional kinship labels. Ancestors follow parent-family links;
descendants follow children on families where the person is a parent.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `person` | `string` | yes | — | Root person handle or Gramps ID |
| `direction` | `string` | yes | — | `ancestors` or `descendants` |
| `generations` | `int` | no | 3 | Generations to include (max 10) |
| `kinshipLabels` | `bool` | no | `true` | Add kinship text such as Father's mother or Granddaughter |

### R — `GetRelations`
Genealogical relationship between two people (e.g. "3rd cousin twice removed"),
path distance, common-ancestor handles.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `handle1` | `string` | yes | First person handle |
| `handle2` | `string` | yes | Second person handle |

### C — `CreatePerson`
Create a new person.  Returns handle and Gramps ID.
**Prerequisites:** `gramps://input-guide`, `gramps://types`.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `primaryName` | `FlexibleGrampsName?` | yes | — | Primary name (JSON or shorthand) |
| `gender` | `string` | no | `"Unknown"` | Female, Male, or Unknown |
| `alternateNames` | `FlexibleAlternateNameList?` | no | — | Alternate names |
| `eventRefs` | `FlexibleEventRefList?` | no | — | Event refs with role metadata (`{ref, role}` or `"HANDLE::Role"`, default role: `Primary`) |
| `familyHandles` | `FlexibleHandleList?` | no | — | Families (as parent/spouse) |
| `parentFamilyHandles` | `FlexibleHandleList?` | no | — | Parent families (as child) |
| `mediaHandles` | `FlexibleHandleList?` | no | — | Media handles |
| `citationHandles` | `FlexibleHandleList?` | no | — | Citation handles |
| `noteHandles` | `FlexibleHandleList?` | no | — | Note handles |
| `tagHandles` | `FlexibleHandleList?` | no | — | Tag handles |
| `attributes` | `FlexibleAttributeList?` | no | — | Attributes |
| `addresses` | `FlexibleAddressList?` | no | — | Addresses |
| `urls` | `FlexibleUrlList?` | no | — | URLs |
| `personAssociations` | `FlexiblePersonRefList?` | no | — | Person associations |
| `isPrivate` | `bool` | no | `false` | Mark record private |

### U — `UpdatePerson`
Update an existing person.  Only include arguments to change.
Empty list `[]` clears links; omit to keep unchanged.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `handle` | `string` | yes | Person handle |
| All fields from `CreatePerson` | — | no | Same as create (all optional) |

---

## Family (`FamilyTools.cs`) — 2 tools

### C — `CreateFamily`
Create a family unit.  **Prerequisites:** `gramps://types`, `gramps://input-guide`.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `fatherHandle` | `string?` | no | — | Father person handle |
| `motherHandle` | `string?` | no | — | Mother person handle |
| `relationshipType` | `string?` | no | `"Married"` | Married, Unmarried, Civil Union, Unknown |
| `childRefs` | `FlexibleChildRefList?` | no | — | Child refs (`{ref, frel, mrel}` or `"HANDLE::RelType"`; default: `Birth`, sets both `frel`/`mrel`) |
| `eventRefs` | `FlexibleEventRefList?` | no | — | Event refs with role metadata (`{ref, role}` or `"HANDLE::Role"`, default role: `Primary`) |
| `mediaHandles`, `citationHandles`, `noteHandles`, `tagHandles` | `FlexibleHandleList?` | no | — | Linked object handles |
| `attributes` | `FlexibleAttributeList?` | no | — | Attributes |
| `isPrivate` | `bool` | no | `false` | Mark private |

### U — `UpdateFamily`
Update an existing family.  Same field set as create (all optional).

---

## Event (`EventTools.cs`) — 2 tools

### C — `CreateEvent`
Create an event.  **Prerequisites:** `gramps://types`, `gramps://input-guide`.
Link to persons/families via their create/update tools' `eventRefs`.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `eventType` | `string` | yes | — | Event type key from tree |
| `date` | `string?` | no | — | Date text |
| `placeHandle` | `string?` | no | — | Place handle |
| `description` | `string?` | no | — | Event description |
| `citationHandles`, `noteHandles`, `tagHandles`, `mediaHandles` | `FlexibleHandleList?` | no | — | Linked handles |
| `attributes` | `FlexibleAttributeList?` | no | — | Attributes |
| `isPrivate` | `bool` | no | `false` | Mark private |

### U — `UpdateEvent`
Update an existing event (same field set, all optional).

---

## Place (`PlaceTools.cs`) — 2 tools

### C — `CreatePlace`
Create a place.  **Prerequisites:** `gramps://types`.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `name` | `string` | yes | — | Primary display name |
| `placeType` | `string?` | no | — | Place type key |
| `lat` | `string?` | no | — | Latitude |
| `lon` | `string?` | no | — | Longitude |
| `enclosedBy` | `FlexiblePlaceRefList?` | no | — | Parent place refs + optional dates (`enclosedBy`, not `enclosedByHandles`). Dashes → span (`from…to`); open `1991-` → From. English months OK (`from 1 Oct 1929 to 27 Sep 1937`). Unrecognized dates fail validation. Examples: `[{ref, date:"1708-1927"}]`, `HANDLE::1991-` |
| `nameLang` | `string?` | no | — | Language code for primary name |
| `alternateNames` | `FlexiblePlaceNameList?` | no | — | Alternate names `{value, lang?, date?}`. Dashes → span; open `1991-` → From; English months / ISO day ranges / mixed `1703-1914-08-31` supported. Unrecognized dates fail validation |
| `noteHandles`, `mediaHandles`, `citationHandles`, `tagHandles` | `FlexibleHandleList?` | no | — | Linked handles |
| `code` | `string?` | no | — | Place code / postal reference |
| `isPrivate` | `bool` | no | `false` | Mark private |

### U — `UpdatePlace`
Update a place. Same fields as create (all optional). `enclosedBy` and `alternateNames` follow omit-to-keep / empty-to-clear list semantics. Use `enclosedBy` (not `enclosedByHandles`) for parent refs and enclosure dates.

---

## Source (`SourceTools.cs`) — 2 tools

### C — `CreateSource`
Create a source.  Create sources **before** citations.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `title` | `string` | yes | — | Source title |
| `author` | `string?` | no | — | Author |
| `pubinfo` | `string?` | no | — | Publication info |
| `abbrev` | `string?` | no | — | Abbreviation |
| `repositoryHandles`, `noteHandles`, `mediaHandles`, `tagHandles` | `FlexibleHandleList?` | no | — | Linked handles |
| `attributes` | `FlexibleAttributeList?` | no | — | Attributes |
| `isPrivate` | `bool` | no | `false` | Mark private |

### U — `UpdateSource`
Update a source (same field set, all optional).

---

## Citation (`CitationTools.cs`) — 2 tools

### C — `CreateCitation`
Create a citation.  `sourceHandle` must point to an existing source.
Attach to persons/events/places via their `citationHandles`.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `sourceHandle` | `string` | yes | — | Source handle |
| `page` | `FlexibleString?` | no | — | Page reference (JSON string or number, coerced to text) |
| `confidence` | `string` | no | `"Normal"` | Very Low / Low / Normal / High / Very High |
| `date` | `string?` | no | — | Access or reference date |
| `noteHandles` | `FlexibleHandleList?` | no | — | Note handles |
| `text` | `string?` | no | — | Transcript text |
| `mediaHandles` | `FlexibleHandleList?` | no | — | Media handles |
| `tagHandles` | `FlexibleHandleList?` | no | — | Tag handles |
| `attributes` | `FlexibleAttributeList?` | no | — | Attributes |
| `isPrivate` | `bool` | no | `false` | Mark private |

### U — `UpdateCitation`
Update a citation (same field set, all optional).

---

## Note (`NoteTools.cs`) — 2 tools

### C — `CreateNote`
Create a note.  **Prerequisites:** `gramps://types`.
Link via `noteHandles` on other objects.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `text` | `string` | yes | — | Note body |
| `noteType` | `string` | no | `"General"` | Note type key |
| `format` | `string` | no | `"Plain"` | Plain or Html |
| `tagHandles` | `FlexibleHandleList?` | no | — | Tag handles |
| `isPrivate` | `bool` | no | `false` | Mark private |

### U — `UpdateNote`
Update a note (same field set, all optional).

---

## Media (`MediaTools.cs`) — 3 tools

Use `get_object(objectType: "media", ...)` for media metadata. It does not
upload or download file bytes. Media byte tools/resources require
`GRAMPS_MEDIA_RESOURCES_ENABLED=true`, respect `GRAMPS_MEDIA_MAX_BYTES` and
`GRAMPS_MEDIA_ALLOWED_MIME_TYPES`, and block private media records unless
`GRAMPS_MEDIA_ALLOW_PRIVATE=true`.

### R — `GetMediaThumbnail`
Download a media thumbnail as MCP image content for vision-capable tool clients
such as Open WebUI. Preferred before requesting a full media file.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `handle` | `string` | yes | — | Media handle |
| `size` | `int` | no | `256` | Thumbnail size in pixels; must be positive |

### R — `GetMediaFile`
Download the full media file as typed MCP tool content: image for images, audio
for audio MIME types, and embedded blob resources for other allowlisted types
such as PDF. Requires `GRAMPS_MEDIA_RESOURCES_ENABLED=true` and an allowlisted MIME.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `handle` | `string` | yes | — | Media handle |

### U — `UpdateMedia`
Update media metadata (no binary upload).

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `handle` | `string` | yes | — | Media handle |
| `description` | `string?` | no | — | Description |
| `date` | `string?` | no | — | Date text |
| `noteHandles`, `tagHandles`, `citationHandles` | `FlexibleHandleList?` | no | — | Linked handles |
| `attributes` | `FlexibleAttributeList?` | no | — | Attributes |
| `isPrivate` | `bool?` | no | — | Private flag |

---

## Repository (`RepositoryTools.cs`) — 2 tools

### C — `CreateRepository`
Create a repository.  **Prerequisites:** `gramps://types`.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `name` | `string` | yes | — | Name |
| `repoType` | `string?` | no | — | Repository type key |
| `address` | `string?` | no | — | Street address |
| `url` | `string?` | no | — | Website URL |
| `noteHandles`, `tagHandles` | `FlexibleHandleList?` | no | — | Linked handles |
| `isPrivate` | `bool` | no | `false` | Mark private |

### U — `UpdateRepository`
Update a repository (same field set, all optional).

---

## Tag (`TagTools.cs`) — 1 tool

### C — `CreateTag`
Create a tag.  Call `list_objects('tags')` first to avoid duplicates.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `name` | `string` | yes | — | Display name |
| `color` | `string` | no | `"000000"` | RRGGBB (no `#`) |
| `priority` | `int` | no | `0` | Sort priority |

---

## Search (`SearchTools.cs`) — 2 tools

### R — `Search`
Full-text search across all object types.  Supports `*` wildcards.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `query` | `string` | yes | — | Search query (`*` for wildcard) |
| `page` | `int` | no | `1` | 1-based page |
| `pagesize` | `int` | no | `20` | Page size (max 100) |

### R — `ListObjects`
Paginated list of one object type with optional filtering.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `objectType` | `string` | yes | — | `people`, `families`, `events`, `places`, `sources`, `citations`, `repositories`, `notes`, `media`, `tags` |
| `page` | `int` | no | `1` | 1-based page |
| `pagesize` | `int` | no | `20` | Page size (max 100) |
| `grampsId` | `string?` | no | — | Filter by Gramps ID (I0001-style) |
| `sourceHandle` | `string?` | no | — | For citations only: filter by source |
| `gql` | `string?` | no | — | Gramps QL expression |
| `sort` | `string?` | no | — | Sort field (prefix `-` for descending) |

---

## System (`SystemTools.cs`) — 2 tools

### R — `GetRecentChanges`
Recent transaction history (most recently changed objects).

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `limit` | `int` | no | `20` | Number of rows (max 100) |

### R — `GetBookmarks`
Gramps Web user bookmarks (saved shortcuts).

---

## Composite Tools (`CompositeTools.cs`) — 2 tools

Multi-step convenience tools that combine several API calls into one.

### C — `QuickAddPerson`
Create a person with optional birth and death events in a single call.
Automatically creates place and event objects as needed, then links them.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `name` | `string` | yes | — | Name as `"Given Surname"` or `"Given\|Surname"` |
| `gender` | `string` | no | `"Unknown"` | Female, Male, or Unknown |
| `birthDate` | `string?` | no | — | Birth date text |
| `birthPlace` | `string?` | no | — | Birth place name |
| `deathDate` | `string?` | no | — | Death date text |
| `deathPlace` | `string?` | no | — | Death place name |

### C — `AddEventToPerson`
Create an event and attach it to an existing person in one call.
Handles event creation + person update automatically.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `personHandle` | `string` | yes | — | Person handle or Gramps ID |
| `eventType` | `string` | yes | — | Event type (e.g. Birth, Death, Baptism) |
| `date` | `string?` | no | — | Event date text |
| `place` | `string?` | no | — | Place name or handle |
| `description` | `string?` | no | — | Event description |
| `role` | `string` | no | `"Primary"` | Person's role in the event |

---

## Tool count summary

| Domain | R | C | U | D | Total |
|--------|---|---|---|---|-------|
| Timeline | 1 | 0 | 0 | 0 | 1 |
| Person | 2 | 1 | 1 | 0 | 4 |
| Family | 0 | 1 | 1 | 0 | 2 |
| Event | 0 | 1 | 1 | 0 | 2 |
| Place | 0 | 1 | 1 | 0 | 2 |
| Source | 0 | 1 | 1 | 0 | 2 |
| Citation | 0 | 1 | 1 | 0 | 2 |
| Note | 0 | 1 | 1 | 0 | 2 |
| Media | 2 | 0 | 1 | 0 | 3 |
| Repository | 0 | 1 | 1 | 0 | 2 |
| Tag | 0 | 1 | 0 | 0 | 1 |
| Object | 1 | 0 | 0 | 1 | 2 |
| Search | 2 | 0 | 0 | 0 | 2 |
| System | 2 | 0 | 0 | 0 | 2 |
| Composite | 0 | 2 | 0 | 0 | 2 |
| Reference | 1 | 0 | 0 | 0 | 1 |
| **Total** | **11** | **11** | **9** | **1** | **32** |

## Prerequisites for write tools

Before calling create/update tools, agents should call discovery tools to
learn valid values.  Type strings are also validated server-side by
`TypeCache`, which returns helpful error messages with suggestions on typos.

| Resource | When useful |
|----------|-------------|
| `gramps://types` | Before setting any type/role/origin string (server validates, but checking first avoids round-trip errors) |
| `gramps://input-guide` | Before any `date`, `Flexible*`, or structured name parameter (covers dates, structured fields, and name schema) |

Clients without MCP resource support can use `get_reference(topic: "types")` or
`get_reference(topic: "input-guide", section: "dates")` instead.
For structured write fields, request only the relevant subsection, such as
`get_reference(topic: "input-guide", section: "structured_fields.addresses")`.

## Delete safety

`delete_object` checks for backlinks before deleting. If the object is
referenced by other objects, deletion is **blocked** unless `force=true`.
Using `force=true` can leave **dangling references** in the database.
