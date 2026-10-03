# MCP Tool Catalog

Complete catalog of up to 31 MCP tools exposed by the server.
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
When `GRAMPS_MEDIA_RESOURCES_ENABLED=false`, `read_media`
is also omitted from the catalog; `get_object` still reads
media metadata.
Binary media resources are read-only GETs and are not blocked by read-only mode.

Create/update/delete HTTP calls are serialized in-process by default
(`GRAMPS_MUTATION_SERIALIZE=true`) and may wait
`GRAMPS_MUTATION_MIN_INTERVAL_MS` between writes. That interval applies to
**each** mutation, so composite tools such as `quick_add_person` and
`add_event_to_person` can take several pauses in one call. The policy is
in-process only; it does not coordinate with the Gramps Web UI or other
API clients. SQLite lock and upstream 429 failures return a retryable MCP
error instead of a generic 500.

## Incremental link updates

The nine `update_*` tools with link lists accept `linkMode`: `replace` (default),
`add`, or `remove`. It applies to every supplied link list in that call:
notes, citations, tags, media, events, children, families, parent families,
person associations, repository references and enclosing places where supported.
Names, attributes, addresses and URLs still use replacement semantics.

```text
update_person(handle: "PERSON_HANDLE", noteHandles: ["NOTE_HANDLE"], linkMode: "add")
update_family(handle: "FAMILY_HANDLE", childRefs: ["CHILD_HANDLE"], linkMode: "remove")
```

Use actual handles in link lists. `add` appends missing handles in input order
and leaves existing links and their metadata unchanged. `remove` removes all
references matching a supplied handle, regardless of role or other metadata;
missing handles are harmless. Omitted lists stay unchanged; `[]` clears only
in `replace` mode. To change existing link metadata, use `replace`. Adding and
removing in the same workflow requires separate calls or one full replacement.

When the mutation gate is enabled, updates and `add_event_to_person` serialize
their entire read/modify/write sequence against each other. HTTP write throttling
still applies. This is not a transaction or a lock against external API clients,
other server processes, creates, or deletes.

## Identifiers and errors

Parameters that take a handle also accept a Gramps ID: an uppercase prefix
letter followed by digits, 2–8 characters in total. The server resolves it by
the Gramps default prefixes: `I` person, `F` family, `E` event, `P` place,
`S` source, `C` citation, `R` repository, `N` note, `O` media, `T` tag. An ID
with another type's prefix, or one that no object has, is passed on unchanged
and ends in a not-found result.

A missing object returns `<Type> not found: <identifier>` as normal tool
output, with a hint when the identifier looks like a Gramps ID:

- An ID with another type's prefix names that type and suggests
  `get_object(identifier: "…")` to read it.
- An ID with an unknown prefix gets the prefix the tool expects.
- An ID with the right prefix that no object has points to `search` and
  `list_objects(objectType: "…")`.
- An identifier shorter than five characters that is not a Gramps ID gets a
  reminder that handles are long strings and Gramps IDs need their prefix.

### Argument validation

Before a tool runs, the server checks the argument names against the tool's
input schema. An unknown argument (with a suggestion for a close match, such as
`extend` for `extended`), a missing required argument, or a value the server
cannot convert to the parameter's type returns a tool error that names the
argument and lists the tool's parameters, their types, and which are required.
Numbers sent as strings are accepted.

```text
An error occurred invoking 'get_object': Unknown argument: extend (did you mean extended?). Parameters: identifier (string, required), objectType (string), extended (boolean).
```

## Resources

Read-only reference/discovery data exposed as MCP resources:

| URI | Description |
|-----|-------------|
| `gramps://input-guide` | Complete write-input guide: date strings, structured fields, full name schema, and incremental link updates |
| `gramps://types` | Built-in and custom type vocabularies (event/place/note/etc.) |
| `gramps://metadata` | Connection/tree metadata (API version, tree id/name, owner, default person) |
| `gramps://name-settings` | Name display formats and surname grouping rules |
| `gramps://media/{handle}/thumbnail/{size}` | Opt-in JPEG preview (PNG when transparent) rendered from an image original, at most `{size}` (1–4096) pixels on the long edge, metadata stripped; recommended for vision agents |
| `gramps://media/{handle}/file` | Opt-in full media file bytes, subject to size and private-record safeguards |

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
| `section` | `string?` | no | For `input-guide`: `dates`, `name_schema`, `link_updates`, `structured_fields`, or `structured_fields.names`, `.attributes`, `.urls`, `.addresses`, `.person_associations`, `.repository_refs`. For `types`: one returned category key such as `event_types`. For `name-settings`: `formats` or `groups`. Unsupported for `metadata`. |

## Prompts

Workflow templates exposed as MCP prompts (`Prompts/GrampsPrompts.cs`).  Each prompt expands to a user-role chat message that guides an agent through typical Gramps Web MCP tool usage.

| Name | Parameters | Purpose |
|------|------------|---------|
| `add-person` | `name`, `gender` (default Unknown), optional `birthDate`, `birthPlace`, `deathDate`, `deathPlace` | Add a new person with optional birth/death details; instructs use of `quick_add_person` and confirmation with handle and Gramps ID. |
| `research-person` | `person` (handle, Gramps ID such as I0001, or name) | Start with the person record; retrieve extended details, a timeline, or the requested tree branch only when relevant. |
| `add-family` | optional `father`, `mother`, `relationship` (default Married), optional `marriageDate`, `marriagePlace` | Create a couple family: verify or find parents, `create_family`, optionally marriage event via `create_event` and `update_family`, then `get_object` for the family. |
| `find-connections` | `person1`, `person2` (name, handle, or Gramps ID) | Resolve both handles, `get_relations`, explain kinship or compare ancestor trees with `get_person_tree` if no direct link. |
| `import-from-text` | `text` | Parse free-form genealogy text: search/create people with `quick_add_person`, `create_family`, `add_event_to_person`, sources/citations as needed, then report import summary and gaps. |
| `change-link` | `ownerType`, `owner`, `linkField`, `target`, optional `action` (`add` by default) | Resolve existing records, add or remove one link with `linkMode`, then verify the owner. |
| `cite-fact` | `recordType`, `record`, `source`, optional `page` | Reuse or create a source and citation, attach it with `linkMode: "add"`, and verify the target. |

---

## Object tools (`ObjectTools.cs`) — 2 tools

### R — `GetObject`
Fetch one record by handle or Gramps ID. A Gramps ID determines its type from
its prefix; an opaque handle requires `objectType`. `extended=true` resolves
linked details for people and families only; an extended person names the
parents, spouse, and children in each family, with the marriage and divorce,
from the person request, and names the others in each event the person takes
part in under another role, such as the child of a `[Father]` birth, in one
more batch. Without it, a family names its parents and children and lists its
events with type, date, and place, and a place names its enclosing places and
full hierarchy, each in one request. Without it, every card also names the
citations, notes, media, tags, and repositories it links to, as in `search`
results (`• [General] Born at home [handle: …]`); a person also names its
families, events, and associated people, and events, places, sources,
citations, repositories, notes, and media name the objects in their
`Referenced by` sections. This takes one batch per type; past 200 of one type,
and for objects that cannot be read, the handle stays bare. A person card is
headed by the name in the tree's display format, as lists and `search` show it
(`Petrova, Anna Ivanovna`), and spells out the primary name as stored below.

Validation errors name the problem: an opaque handle without `objectType`, an
unknown `objectType`, an `objectType` that disagrees with the Gramps ID prefix
(such as `objectType: "family"` with `I0001`), or `extended=true` for a type
other than person or family.

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
include relatives' events, named with their Gramps ID and age; rows show the
role when the person is not the primary participant and leave out a zero age,
such as "0 days" on birth rows. A row of the person's own event under another
role, such as `Birth [Father]`, names the others in it on a `Participants:`
line below, read in one batch for all such rows. Place timelines are computed
from direct event backlinks, name the place once above the rows, and list each
event's participants with their roles; child places are not included.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `objectType` | `string` | yes | — | `person`, `family`, or `place` |
| `identifier` | `string` | yes | — | Object handle or Gramps ID |
| `events` | `string[]?` | no | all | Event categories: vital, family, religious, vocational, academic, travel, legal, residence, other, custom |
| `relatives` | `string[]?` | no | none | Person only: father, mother, brother, sister, wife, husband, son, daughter |
| `relativeEvents` | `string[]?` | no | none | Person only: event categories for relatives |
| `dates` | `string?` | no | — | Any date the date parser takes: a year (`1850`), month (`1850-03`), day, range (`1850-1860`, `between 1850-03 and 1851`), `from`/`to` (inclusive), or `before`/`after` (exclusive), in any Gramps calendar; Gramps Web's `1850/1/1-1860/12/31` also works |

A year or month in `dates` covers all its days, so `1850-1860` runs from
1 January 1850 to 31 December 1860. MCP converts the bounds to the Gregorian
`y/m/d` form Gramps Web takes, so `1856-07-20 (Julian)` asks for 1856/8/1.
Approximate dates (`about`, `estimated`, `calculated`) are rejected, since a
filter needs exact bounds, and so is a new year on a year or month alone.

Without `dates`, undated events are included and listed after the dated ones.
A place timeline with `dates` leaves out undated events.

Without `dates`, a person timeline also keeps relatives' events from before
the person's first event and after their last one (MCP sends `first=false` and
`last=false`). Gramps Web would cut the timeline to the person's own events
with Gramps' fuzzy date matching: an `about 1876` birth spans 1826 to 1926, so
every relative's event before 1926 was dropped, the person's own marriage too.
Pass `dates` to narrow the timeline to the years you need.

---

## Person (`PersonTools.cs`) — 4 tools

### R — `GetPersonTree`
List either ancestors or descendants up to N generations with names, vital
dates/places, and optional kinship labels. Ancestors follow parent-family links;
descendants follow children on families where the person is a parent.

The header names the root person: `Root: Petrov, Ivan, b. 1880 in Dublin
[I0012] [handle: …]`. Each row starts with `Gen N`, followed by the kinship
label when enabled (such as `Gen 2 — Father's mother`), then the person summary
ending in the Gramps ID and, on the next line, the handle. Vitals read
`b. 1880 in Dublin` and `d. 1950`; when Gramps Web falls back to another event
because birth or death is missing, the event type is named instead, such as
`baptism 1880` or `burial 1950`.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `person` | `string` | yes | — | Root person handle or Gramps ID |
| `direction` | `string` | yes | — | `ancestors` or `descendants` |
| `generations` | `int` | no | 3 | Generations to include (max 10) |
| `kinshipLabels` | `bool` | no | `true` | Add kinship text such as Father's mother or Granddaughter |

### R — `GetRelations`
How two people are related, read as "person 2 is the X of person 1" (e.g.
"third cousin twice removed", "husband"). Shows both people by name, generations
from each to the common ancestor, and every relationship Gramps finds, closest
first, with its common ancestors by name (up to 10 named, the rest by handle).
Searches blood relatives up to 15 generations, plus spouses; unrelated people
get a clear message. Two identifiers for the same person return "both handles
refer to the same person" without a relationship lookup, and a missing person
returns the usual not-found message.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `handle1` | `string` | yes | First person handle or Gramps ID |
| `handle2` | `string` | yes | Second person handle or Gramps ID |

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
`linkMode: "replace" | "add" | "remove"` applies to supplied link lists; see [Incremental link updates](#incremental-link-updates).

Update an existing person.  Only include arguments to change.
Empty list `[]` clears links in `replace` mode; omit to keep unchanged.

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
`linkMode: "replace" | "add" | "remove"` applies to supplied link lists; see [Incremental link updates](#incremental-link-updates).

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
`linkMode: "replace" | "add" | "remove"` applies to supplied link lists; see [Incremental link updates](#incremental-link-updates).

Update an existing event (same field set, all optional). Omit `date` to keep
the current date; an empty string removes it.

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
`linkMode: "replace" | "add" | "remove"` applies to supplied link lists; see [Incremental link updates](#incremental-link-updates).

Update a place. Same fields as create (all optional). `enclosedBy` is a link list and follows `linkMode`; `alternateNames` always uses replacement. Use `enclosedBy` (not `enclosedByHandles`) for parent refs and enclosure dates.

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
| `repositoryHandles` | `FlexibleRepositoryRefList?` | no | — | Repository refs: handle strings, `"Ref : CallNumber : MediaType"` strings, or `{ref, callNumber, mediaType}` objects (snake_case also accepted) |
| `noteHandles`, `mediaHandles`, `tagHandles` | `FlexibleHandleList?` | no | — | Linked handles |
| `attributes` | `FlexibleAttributeList?` | no | — | Attributes |
| `isPrivate` | `bool` | no | `false` | Mark private |

### U — `UpdateSource`
`linkMode: "replace" | "add" | "remove"` applies to supplied link lists; see [Incremental link updates](#incremental-link-updates).

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
`linkMode: "replace" | "add" | "remove"` applies to supplied link lists; see [Incremental link updates](#incremental-link-updates).

Update a citation (same field set, all optional). Omit `date` to keep the
current date; an empty string removes it.

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
`linkMode: "replace" | "add" | "remove"` applies to supplied link lists; see [Incremental link updates](#incremental-link-updates).

Update a note (same field set, all optional).

---

## Media (`MediaTools.cs`) — 2 tools

Use `get_object(objectType: "media", ...)` for media metadata. It does not
upload or download file bytes. Media byte tools/resources require
`GRAMPS_MEDIA_RESOURCES_ENABLED=true`, respect `GRAMPS_MEDIA_MAX_BYTES`, and
block private media records unless `GRAMPS_MEDIA_ALLOW_PRIVATE=true`.

### R — `ReadMedia`
Download media bytes as typed MCP content. Default mode `thumbnail` returns a
JPEG preview (PNG when the image has transparency) rendered by the server from
the original, with EXIF, GPS, and other metadata stripped. Previews can be
rendered from JPEG, PNG, GIF, WebP, BMP, TIFF, TGA, PBM, and QOI originals;
multi-page files use the first page. Non-image media such as PDF, and AVIF,
HEIC/HEIF, JPEG XL, and SVG images, are rejected before download. Mode `file`
returns the original as image, audio, or embedded blob resource content
according to MIME type; images other than JPEG, PNG, GIF, and WebP come back as
an embedded blob with a text hint. Prefer thumbnails before full files.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `handle` | `string` | yes | — | Media handle or Gramps ID |
| `mode` | `string` | no | `thumbnail` | `thumbnail` or `file` |
| `size` | `int?` | no | — | Thumbnail long edge in pixels, 1 to 4096; omitted means 1568, which keeps document scans legible. Images are never upscaled. Must be omitted in file mode |

Migration: replace `get_media_thumbnail(handle, size)` with
`read_media(handle, mode: "thumbnail", size: size)` and replace
`get_media_file(handle)` with `read_media(handle, mode: "file")`.
The old tool names are no longer registered. Media resource URIs are unchanged.

### U — `UpdateMedia`
`linkMode: "replace" | "add" | "remove"` applies to supplied link lists; see [Incremental link updates](#incremental-link-updates).

Update media metadata (no binary upload).

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `handle` | `string` | yes | — | Media handle |
| `description` | `string?` | no | — | Description |
| `date` | `string?` | no | — | Date text; omit to keep the current date, an empty string removes it |
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
`linkMode: "replace" | "add" | "remove"` applies to supplied link lists; see [Incremental link updates](#incremental-link-updates).

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
Full-text search across all object types.  Supports `*` wildcards. The header
reads `Search Results (Page 1 of 3, Total: 47):`; each row is a one-line
summary such as `Person: …` or `Place: … (City)` followed by
`— handle: … | gramps_id: …`. A page past the last one says so and gives the
total instead of returning nothing.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `query` | `string` | yes | — | Search query (`*` for wildcard) |
| `page` | `int` | no | `1` | 1-based page |
| `pagesize` | `int` | no | `20` | Page size (max 100) |

### R — `ListObjects`
Paginated list of one object type with optional filtering. The header reads
`PEOPLE (Page 1 of 5, Total: 93)`; rows are numbered across pages and use the
same summary and `handle | gramps_id` suffix as `search`. An empty page returns
`No <objectType> found.`

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
Recent transaction history, newest first, under the header
`RECENT CHANGES (20 of 512, newest first)`. Each transaction row shows its UTC
commit time, description, user, an `(undo)` marker for undo transactions, and
`[transaction: id]`. Below it, each changed object appears as `Added`,
`Updated`, `Deleted`, or `Changed` with its class and handle, up to 10 objects
per transaction followed by `(+N more changes)`. A link that an edit adds or
removes, which Gramps records as a reference change, appears as
`Added Reference from [handle: …] to [handle: …]`.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `limit` | `int` | no | `20` | Number of transactions (clamped 1–100) |

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

A place name reuses the place whose name matches exactly, ignoring case, and
otherwise creates a new place.

### C — `AddEventToPerson`
Create an event and attach it to an existing person in one call.
Handles event creation + person update automatically.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `personHandle` | `string` | yes | — | Person handle or Gramps ID |
| `eventType` | `string` | yes | — | Event type (e.g. Birth, Death, Baptism) |
| `date` | `string?` | no | — | Event date text |
| `place` | `string?` | no | — | Place Gramps ID, handle, or name |
| `description` | `string?` | no | — | Event description |
| `role` | `string` | no | `"Primary"` | Person's role in the event |

`place` takes an existing place by Gramps ID or handle. Any other value is a
name: it reuses the place whose name matches exactly, ignoring case, or creates
a new place, so an ID or handle that matches no place creates a place with
that text as its name.

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
| Media | 1 | 0 | 1 | 0 | 2 |
| Repository | 0 | 1 | 1 | 0 | 2 |
| Tag | 0 | 1 | 0 | 0 | 1 |
| Object | 1 | 0 | 0 | 1 | 2 |
| Search | 2 | 0 | 0 | 0 | 2 |
| System | 2 | 0 | 0 | 0 | 2 |
| Composite | 0 | 2 | 0 | 0 | 2 |
| Reference | 1 | 0 | 0 | 0 | 1 |
| **Total when media access is enabled** | **10** | **11** | **9** | **1** | **31** |

With the default `GRAMPS_MEDIA_RESOURCES_ENABLED=false`, `read_media` is hidden
and the catalog contains 30 tools in read/write mode. Read-only mode publishes
10 tools with media access enabled, or 9 with it disabled.

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
