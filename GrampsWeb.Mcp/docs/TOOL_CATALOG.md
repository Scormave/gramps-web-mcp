# MCP Tool Catalog

Complete catalog of the 56 MCP tools exposed by the server.
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

---

## Person (`PersonTools.cs`) — 8 tools

### R — `GetPerson`
Fetch one person by handle.  With `extended=true`, resolves linked objects
(event dates/places, note text, tag names, citations, media) for a fuller
picture in one call.  Default `extended=false` returns core fields with handles
only (faster).

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `handle` | `string` | yes | — | Person handle |
| `extended` | `bool` | no | `false` | Resolve linked events/notes/tags/citations/media inline |

### R — `GetAncestors`
List ancestors up to N generations with names, vital dates/places, and
optional kinship labels (Father, Mother's father, …).

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `handle` | `string` | yes | — | Root person handle |
| `generations` | `int` | no | 3 | Generations to include (max 10) |
| `kinship_labels` | `bool` | no | `true` | Add kinship text |

### R — `GetDescendants`
List descendants up to N generations with names, vital dates/places, and
optional kinship (Son, Granddaughter, …).

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `handle` | `string` | yes | — | Root person handle |
| `generations` | `int` | no | 3 | Generations to include (max 10) |
| `kinship_labels` | `bool` | no | `true` | Add kinship text |

### R — `GetPersonTimeline`
Chronological timeline of events for one person, with optional relative events.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `handle` | `string` | yes | — | Person handle |
| `events` | `string[]?` | no | all | Event categories: vital, family, religious, vocational, academic, travel, legal, residence, other, custom |
| `relatives` | `string[]?` | no | none | Relatives: father, mother, brother, sister, wife, husband, son, daughter |
| `relativeEvents` | `string[]?` | no | none | Event categories for relatives |
| `dates` | `string?` | no | — | Date range `YYYY/M/D-YYYY/M/D` |
| `includeUndated` | `bool` | no | `true` | Include events with sortval 0 |

### R — `GetRelations`
Genealogical relationship between two people (e.g. "3rd cousin twice removed"),
path distance, common-ancestor handles.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `handle1` | `string` | yes | First person handle |
| `handle2` | `string` | yes | Second person handle |

### C — `CreatePerson`
Create a new person.  Returns handle and Gramps ID.
**Prerequisites:** `get_name_schema`, `get_types`, `get_date_input_guide`, `get_structured_field_input_guide`.

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

### D — `DeletePerson`
Delete a person.  Blocked when backlinks exist unless `force=true`.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `handle` | `string` | yes | — | Person handle |
| `force` | `bool` | no | `false` | Delete despite backlinks |

---

## Family (`FamilyTools.cs`) — 5 tools

### R — `GetFamily`
One family by handle: parents, children with frel/mrel, relationship type,
linked events/notes/tags.  With `extended=true`, resolves member names, event
dates/places, citations, media.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `handle` | `string` | yes | — | Family handle |
| `extended` | `bool` | no | `false` | Resolve linked names/events/places inline |

### R — `GetFamilyTimeline`
Chronological events for one family.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `handle` | `string` | yes | — | Family handle |
| `events` | `string[]?` | no | all | Event categories |
| `dates` | `string?` | no | — | Date range filter |

### C — `CreateFamily`
Create a family unit.  **Prerequisites:** `get_types`, `get_structured_field_input_guide`.

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

### D — `DeleteFamily`
Delete a family.  Does not remove the family from person records automatically.

---

## Event (`EventTools.cs`) — 4 tools

### R — `GetEvent`
One event: type, date/modifiers, place, description, citations, notes, tags, media.

### C — `CreateEvent`
Create an event.  **Prerequisites:** `get_types`, `get_date_input_guide`, `get_structured_field_input_guide`.
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

### D — `DeleteEvent`
Delete an event.  Blocked when persons/families reference it unless `force=true`.

---

## Place (`PlaceTools.cs`) — 5 tools

### R — `GetPlace`
One place: name, type, coordinates, place hierarchy.

### R — `GetPlaceTimeline`
Chronological events whose place equals this handle.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `handle` | `string` | yes | — | Place handle |
| `events` | `string[]?` | no | all | Event categories |
| `dates` | `string?` | no | — | Date range filter |
| `includeUndated` | `bool` | no | `true` | Include events with sortval 0 |

### C — `CreatePlace`
Create a place.  **Prerequisites:** `get_types`.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `name` | `string` | yes | — | Primary display name |
| `placeType` | `string?` | no | — | Place type key |
| `lat` | `string?` | no | — | Latitude |
| `lon` | `string?` | no | — | Longitude |
| `enclosedByHandles` | `FlexibleHandleList?` | no | — | Parent place handles |
| `nameLang` | `string?` | no | `"en"` | Language code |
| `noteHandles`, `mediaHandles`, `citationHandles`, `tagHandles` | `FlexibleHandleList?` | no | — | Linked handles |
| `code` | `string?` | no | — | Place code / postal reference |
| `isPrivate` | `bool` | no | `false` | Mark private |

### U — `UpdatePlace`
Update a place (same field set, all optional).

### D — `DeletePlace`
Delete a place.  Blocked when events or child places reference it unless `force=true`.

---

## Source (`SourceTools.cs`) — 4 tools

### R — `GetSource`
One source: title, author, publication info, abbreviation, repository refs.

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

### D — `DeleteSource`
Delete a source.  Citations pointing at it break or lose their link.

---

## Citation (`CitationTools.cs`) — 4 tools

### R — `GetCitation`
One citation: source title/handle, page, confidence, access date.

### C — `CreateCitation`
Create a citation.  `sourceHandle` must point to an existing source.
Attach to persons/events/places via their `citationHandles`.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `sourceHandle` | `string` | yes | — | Source handle |
| `page` | `string?` | no | — | Page reference |
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

### D — `DeleteCitation`
Delete a citation.  Blocked when linked from other objects unless `force=true`.

---

## Note (`NoteTools.cs`) — 4 tools

### R — `GetNote`
One note: text, type, format (Plain / Html).

### C — `CreateNote`
Create a note.  **Prerequisites:** `get_types`.
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

### D — `DeleteNote`
Delete a note.  Blocked when attached elsewhere unless `force=true`.

---

## Media (`MediaTools.cs`) — 3 tools

### R — `GetMedia`
Media object metadata: path, MIME type, checksum, description.
Does **not** upload/download file bytes.

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

### D — `DeleteMedia`
Delete a media record.  Removes the Gramps object, not necessarily the file on disk.

---

## Repository (`RepositoryTools.cs`) — 4 tools

### R — `GetRepository`
One repository: name, type, address, URLs.

### C — `CreateRepository`
Create a repository.  **Prerequisites:** `get_types`.

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

### D — `DeleteRepository`
Delete a repository.  Blocked when sources reference it unless `force=true`.

---

## Tag (`TagTools.cs`) — 4 tools

### R — `GetTag`
One tag: name, color (hex), priority.

### C — `CreateTag`
Create a tag.  Call `list_objects('tags')` first to avoid duplicates.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `name` | `string` | yes | — | Display name |
| `color` | `string` | no | `"000000"` | RRGGBB (no `#`) |
| `priority` | `int` | no | `0` | Sort priority |

### U — `UpdateTag`
Update a tag.  Only include arguments you want to change.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `handle` | `string` | yes | — | Tag handle |
| `name` | `string?` | no | — | Display name |
| `color` | `string?` | no | — | RRGGBB (no `#`) |
| `priority` | `int?` | no | — | Sort priority |

### D — `DeleteTag`
Delete a tag.  Blocked when objects carry it unless `force=true`.

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

## System (`SystemTools.cs`) — 3 tools

### R — `GetMetadata`
Connection and tree metadata: API version, tree id/name, owner, default person.
Call early to confirm which database is connected.

### R — `GetRecentChanges`
Recent transaction history (most recently changed objects).

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `limit` | `int` | no | `20` | Number of rows (max 100) |

### R — `GetBookmarks`
Gramps Web user bookmarks (saved shortcuts).

---

## Type & Schema discovery (`TypeTools.cs`) — 2 tools

### R — `GetTypes`
Gramps type vocabularies (event_types, place_types, note_types, etc.).
When `includeCustom` is true (default), custom types from this database
are merged with built-in types.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `includeCustom` | `bool` | no | `true` | Merge custom types from this database |

### R — `GetInputGuide`
Combined reference for all write-tool input formats: date strings, structured
fields (attributes, URLs, addresses, person refs, name shorthand), and the
full Gramps Name object schema.  Call once before using any create/update tool.

---

## Name (`NameTools.cs`) — 1 tool

### R — `GetNameSettings`
Name display format definitions and surname grouping rules configured in this
tree (combines the former `GetNameFormats` and `GetNameGroups`).

---

## Composite Tools (`CompositeTools.cs`) — 3 tools

Multi-step convenience tools that combine several API calls into one.

### R — `FindByGrampsId`
Find any Gramps object by its Gramps ID (e.g. `I0001`, `F0023`, `E0005`).
Automatically detects the object type from the ID prefix, resolves the handle,
and returns full details.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `grampsId` | `string` | yes | Gramps ID (e.g. I0001, F0023) |

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
| Person | 5 | 1 | 1 | 1 | 8 |
| Family | 2 | 1 | 1 | 1 | 5 |
| Event | 1 | 1 | 1 | 1 | 4 |
| Place | 2 | 1 | 1 | 1 | 5 |
| Source | 1 | 1 | 1 | 1 | 4 |
| Citation | 1 | 1 | 1 | 1 | 4 |
| Note | 1 | 1 | 1 | 1 | 4 |
| Media | 1 | 0 | 1 | 1 | 3 |
| Repository | 1 | 1 | 1 | 1 | 4 |
| Tag | 1 | 1 | 1 | 1 | 4 |
| Search | 2 | 0 | 0 | 0 | 2 |
| System | 3 | 0 | 0 | 0 | 3 |
| Types/Schema | 2 | 0 | 0 | 0 | 2 |
| Name | 1 | 0 | 0 | 0 | 1 |
| Composite | 1 | 2 | 0 | 0 | 3 |
| **Total** | **25** | **11** | **10** | **10** | **56** |

## Prerequisites for write tools

Before calling create/update tools, agents should call discovery tools to
learn valid values.  Type strings are also validated server-side by
`TypeCache`, which returns helpful error messages with suggestions on typos.

| Discovery tool | When useful |
|----------------|-------------|
| `get_types` | Before setting any type/role/origin string (server validates, but calling first avoids round-trip errors) |
| `get_input_guide` | Before any `date`, `Flexible*`, or structured name parameter (covers dates, structured fields, and name schema) |

## Delete safety

All delete tools check for backlinks before deleting.  If the object is
referenced by other objects, deletion is **blocked** unless `force=true`.
Using `force=true` can leave **dangling references** in the database.
