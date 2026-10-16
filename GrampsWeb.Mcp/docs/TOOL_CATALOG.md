# MCP Tool Catalog

Complete catalog of up to 32 MCP tools exposed by the server.
Tools are grouped by Gramps entity type.  Each tool is a static method
decorated with `[McpServerTool]`.

> `GrampsApiClient client` is injected by the MCP host and is **not** a caller-supplied argument.

## Legend

| Symbol | Meaning |
|--------|---------|
| R | Read-only tool |
| C | Create (additive write) |
| U | Update (destructive write: overwrites fields, replace mode removes links) |
| D | Delete (destructive write) |

Read-only mode (`GRAMPS_READ_ONLY=true`) publishes only the read-only tools.
Direct calls to a previously known write-tool name still return an MCP error
before any mutation request is sent to Gramps Web.
When `GRAMPS_MEDIA_RESOURCES_ENABLED=false`, `read_media`
is also omitted from the catalog; `get_object` still reads
media metadata. `create_media` is published only when media access is enabled
and `GRAMPS_MEDIA_UPLOAD_DIRS` names at least one upload folder.
Binary media resources are read-only GETs and are not blocked by read-only mode.

Tool descriptions state what a tool does, what it returns, when to use a
sibling tool instead, and what happens on duplicates, partial failure, or bad
input. Whether a tool writes, and whether repeating a write changes
anything more, is carried by its `readOnlyHint`, `destructiveHint` and
`idempotentHint` annotations, not by the description text; every tool has
`openWorldHint: false`, since it works only on the configured tree. Shared
wording (handle lookup, update semantics, date text, known types) comes from
`ToolDescriptionFragments`.

The server also sends **instructions** at initialization
(`Hosting/GrampsServerInstructions.cs`): which tool to start with, how links
and `linkMode` work, the order to create linked records in, and when to delete.
They name only the tools the configuration publishes: read-only mode replaces
the writing guidance with one line saying no tool changes the tree,
`read_media` is mentioned only when media access is enabled, and
`create_media` only when media access is enabled and an upload folder is set.

Create/update/delete HTTP calls are serialized in-process by default
(`GRAMPS_MUTATION_SERIALIZE=true`) and may wait
`GRAMPS_MUTATION_MIN_INTERVAL_MS` between writes. That interval applies to
**each** mutation. The policy is
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
in `replace` mode. In `replace` mode a reference that stays keeps its stored
privacy, citations, notes and crop; fields the call sends (including the
default role or relationship of a bare handle) overwrite them, so use `replace`
to change existing link metadata. An attribute with the same type and value as
a stored one keeps its citations, notes and privacy. Adding and removing in the
same workflow requires separate calls or one full replacement.

`update_person` and `update_family` also take `sort`, which names the lists
to put in date order after the link lists are applied, so a link added with
`linkMode: "add"` lands in its place instead of at the end. It takes a JSON
array (`["Events", "Citations"]`) or one string (`"Events | Citations"`);
names are case-insensitive. It only moves the stored references, so their
roles, relationships, crops and other metadata stay, and it works alone to
sort an existing list. Equal dates keep their stored order.

| List | Tool | Order |
|------|------|-------|
| `Events` | both | Event date. Undated Birth, Baptism and Christening go first; other undated events go last, ending with Death, then Burial and Cremation. On the same date: "before" dates, then Residence and Occupation, Birth, Baptism, other events, Death and Burial, then "after" dates |
| `Children` | `update_family` | Birth date, else baptism or christening date; undated children last |
| `Families` | `update_person` | The person's marriages and other unions: Marriage date, else another marriage event (Engagement, Marriage Banns, …), else the earliest birth of a child; undated families last |
| `Citations`, `Media` | both | The citation's or media object's own date; undated ones last. Gramps shows the first media as the person's picture, so sorting `Media` can change it |

```text
update_person(handle: "PERSON_HANDLE", eventRefs: ["EVENT_HANDLE::Witness"], linkMode: "add", sort: "Events")
update_family(handle: "FAMILY_HANDLE", childRefs: ["CHILD_HANDLE"], linkMode: "add", sort: ["Children"])
```

### Image regions

A media link can show only part of an image, such as one baptism record on a
page scan. `mediaHandles` on the person, family, event, place, source and
citation tools, and `attachTo` on `create_media` and `update_media`, take an
object `{"ref": "MEDIA_HANDLE", "region": [left, top, right, bottom]}` in place
of a handle (`attachTo` takes a Gramps ID as `ref` too). The edges are whole
percent of the image width and height, 0 to 100, from the top-left corner, as
Gramps stores the crop of a media reference: `[10, 20, 60, 45]` runs from 10% to
60% across and from 20% to 45% down. The key `rect` and a string such as
`"10,20,60,45"` are read too; fractions are rounded. `[0, 0, 100, 100]` shows the
whole image again. An empty region or an edge outside 0 to 100 is rejected
before anything is saved or uploaded.

A plain handle keeps the region its link has, in every mode. With
`linkMode: "add"` (and always with `attachTo`), a media the record already shows
keeps its link, with its privacy, citations and notes, and takes the region; a
link that already shows that region is left as it is, so repeating a call
changes nothing. To show two regions of one image on a record, list the media
twice, each with its region. In `replace` mode a region is laid over the
matching stored link like other link fields. Records list each media link with
its region, and `read_media` with `region` shows what a region covers before it
is saved.

```text
read_media(handle: "O0100", region: [5, 50, 95, 75])
update_person(handle: "PERSON_HANDLE", mediaHandles: [{"ref": "MEDIA_HANDLE", "region": [5, 50, 95, 75]}], linkMode: "add")
create_media(filePath: "page12.jpg", attachTo: [{"ref": "I0001", "region": [5, 50, 95, 75]}, "C0001"])
```

### Updates keep stored data

Every update tool sends back the stored object with only the supplied fields
changed, including data the tools cannot show or edit (styled note text, LDS
ordinances, fields from newer Gramps versions). `update_note` keeps the styling
and links of a note when `text` is omitted or unchanged; changed text loses them.
`replaceText` with `withText` changes one fragment of a note and `appendText`
adds to its end; both keep them.

When the mutation gate is enabled, update tools serialize
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
Numbers sent as strings are accepted, and a JSON number sent for a text
parameter is read as its text. An `update_*` call that gives no field to change
besides `handle` and `linkMode` is refused with the same parameter list instead
of saving the record unchanged: clients such as Claude Code drop arguments a
tool does not have, so `update_event(place: …)` arrives without `place`.
For the same reason `create_place` and `update_place` show the place's
`placeType`, `lat`, `lon` and `enclosedBy` in their result (`none` when
empty, Unknown for a place created without a type), so a misnamed argument
shows up as a value that was not set.

Type values (event, place, note, repository and family relationship types) are
checked against the tree's vocabularies from `gramps://types` before anything
is saved, and an unknown value is refused with suggestions. Case does not
matter: an accepted value is stored in the vocabulary's spelling (`village`
becomes `Village`). Event link roles in `eventRefs` of `create_person`,
`update_person`, `create_family` and `update_family` are checked the same way
(`witness` becomes `Witness`); an empty role stays Primary, and
`linkMode: "remove"` does not check roles.

The types inside structured arguments are checked the same way, and the error
names the argument:

| Argument | Value | Vocabulary |
|---|---|---|
| `primaryName`, `alternateNames` of people | name `type`, surname `origintype` | `name_types`, `name_origin_types` |
| `attributes` of people, families, events and media | `type` | `attribute_types` and the custom attribute types of all four |
| `attributes` of sources and citations | `type` | `source_attribute_types` |
| `urls` of people | `type` | `url_types` |
| `childRefs` | `frel`, `mrel` | `child_reference_types` |
| `repositoryHandles` | `media_type` | `source_media_types` |

An empty name type or surname origin stays empty, which Gramps reads as Birth
Name and no origin. The unknown surname origin is sent as Gramps spells it,
with a trailing space. `linkMode: "remove"` does not check `childRefs` or
`repositoryHandles`.

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
| `gramps://filter-rules` | How to write the `rules` of `list_objects`, with the number of rules and the saved filters of each type |
| `gramps://filter-rules/{objectType}` | The filter rules of one type (such as `people`) by category, with the values each takes, and the tree's saved filters |
| `gramps://media/{handle}/thumbnail/{size}` | Opt-in JPEG preview (PNG when transparent) rendered from an image original, at most `{size}` (1–4096) pixels on the long edge, metadata stripped; recommended for vision agents |
| `gramps://media/{handle}/file` | Opt-in full media file bytes, subject to size and private-record safeguards |

Compatibility note: reference payloads are also available through
`get_reference` for clients without native MCP resource reading support.
Media thumbnails/files are also available as image-content tools for clients
such as Open WebUI that handle tool images better than MCP resources.

## Reference (`ReferenceTools.cs`) — 1 tool

### R — `GetReference`
Read the server's reference text before writing when a format or type is
unclear: the same payloads as the MCP resources, for clients that do not read
resources. `topic` selects the payload; `section` reduces the response when
only one part is needed. An unknown topic or section is rejected with the valid
ones.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `topic` | `string` | yes | `input-guide`, `types`, `metadata`, `name-settings`, or `filter-rules` |
| `section` | `string?` | no | For `input-guide`: `dates`, `name_schema`, `link_updates`, `structured_fields`, or `structured_fields.names`, `.attributes`, `.urls`, `.addresses`, `.person_associations`, `.repository_refs`. For `types`: one returned category key such as `event_types`. For `name-settings`: `formats` or `groups`. For `filter-rules`: a type such as `people` or `events` (its rules and values; omitted, an overview of all types). Unsupported for `metadata`. |

## Prompts

Workflow templates exposed as MCP prompts (`Prompts/GrampsPrompts.cs`).  Each prompt expands to a user-role chat message that guides an agent through typical Gramps Web MCP tool usage.

| Name | Parameters | Purpose |
|------|------------|---------|
| `add-person` | `name`, `gender` (default Unknown), optional `birthDate`, `birthPlace`, `deathDate`, `deathPlace` | Add a new person with optional birth/death details: search for the person, find or create the places, `create_event` for birth and death, `create_person` with those events, then confirm with handle and Gramps ID. |
| `research-person` | `person` (handle, Gramps ID such as I0001, or name) | Start with the person record; retrieve extended details, a timeline, or the requested tree branch only when relevant. |
| `add-family` | optional `father`, `mother`, `relationship` (default Married), optional `marriageDate`, `marriagePlace` | Create a couple family: verify or find parents, `create_family`, optionally marriage event via `create_event` and `update_family`, then `get_object` for the family. |
| `find-connections` | `person1`, `person2` (name, handle, or Gramps ID) | Resolve both handles, `get_relations`, explain kinship or compare ancestor trees with `get_person_tree` if no direct link. |
| `import-from-text` | `text` | Parse free-form genealogy text: search for existing people and places, create places, events, people (`create_person` with `eventRefs`) and families, attach events to existing people with `linkMode: "add"`, add sources/citations as needed, then report import summary and gaps. |
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
`force=true`; the refusal lists the backlinks by type. Forcing may leave
dangling references. On success the reply gives the type, `action: deleted`,
and the handle. Deleting a media record does not necessarily delete its file on
disk. `undo_change` with the deletion's transaction from `get_recent_changes`
restores the record with its handle and Gramps ID.

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
| `mediaHandles` | `FlexibleMediaLinkList?` | no | — | Media handles, or `{ref, region}` objects to show part of an image; see [Image regions](#image-regions) |
| `citationHandles` | `FlexibleHandleList?` | no | — | Citation handles |
| `noteHandles` | `FlexibleHandleList?` | no | — | Note handles |
| `tagHandles` | `FlexibleHandleList?` | no | — | Tag handles |
| `attributes` | `FlexibleAttributeList?` | no | — | Attributes, each with optional citations, notes and private flag |
| `addresses` | `FlexibleAddressList?` | no | — | Addresses |
| `urls` | `FlexibleUrlList?` | no | — | URLs |
| `personAssociations` | `FlexiblePersonRefList?` | no | — | Person associations |
| `isPrivate` | `bool` | no | `false` | Mark record private |

The first Birth and the first Death in `eventRefs` with the Primary role become
the person's birth and death (`birth_ref_index`, `death_ref_index`), as in Gramps.

### U — `UpdatePerson`
`linkMode: "replace" | "add" | "remove"` applies to supplied link lists; see [Incremental link updates](#incremental-link-updates).

Update an existing person.  Only include arguments to change.
Empty list `[]` clears links in `replace` mode; omit to keep unchanged.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `handle` | `string` | yes | Person handle |
| All fields from `CreatePerson` | — | no | Same as create (all optional) |
| `sort` | `FlexibleSortList?` | no | Lists to put in date order after the changes: `Events`, `Families`, `Citations`, `Media`; see [Incremental link updates](#incremental-link-updates) |

---

## Family (`FamilyTools.cs`) — 2 tools

### C — `CreateFamily`
Create a family unit.  **Prerequisites:** `gramps://types`, `gramps://input-guide`.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `fatherHandle` | `string?` | no | — | Father person handle |
| `motherHandle` | `string?` | no | — | Mother person handle |
| `relationshipType` | `string?` | no | `"Married"` | Married, Unmarried, Civil Union, Unknown |
| `childRefs` | `FlexibleChildRefList?` | no | — | Child refs (`{ref, frel, mrel}` or `"HANDLE::RelType"`; default: `Birth`, sets both `frel`/`mrel`); relations are checked against `child_reference_types` |
| `eventRefs` | `FlexibleEventRefList?` | no | — | Event refs with role metadata (`{ref, role}` or `"HANDLE::Role"`, default role: `Primary`) |
| `mediaHandles` | `FlexibleMediaLinkList?` | no | — | Media handles, or `{ref, region}` objects; see [Image regions](#image-regions) |
| `citationHandles`, `noteHandles`, `tagHandles` | `FlexibleHandleList?` | no | — | Linked object handles |
| `attributes` | `FlexibleAttributeList?` | no | — | Attributes, each with optional citations, notes and private flag |
| `isPrivate` | `bool` | no | `false` | Mark private |

### U — `UpdateFamily`
`linkMode: "replace" | "add" | "remove"` applies to supplied link lists; see [Incremental link updates](#incremental-link-updates).

Update an existing family.  Same field set as create (all optional), plus
`sort` to put `Events`, `Children`, `Citations` or `Media` in date order; see
[Incremental link updates](#incremental-link-updates).

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
| `citationHandles`, `noteHandles`, `tagHandles` | `FlexibleHandleList?` | no | — | Linked handles |
| `mediaHandles` | `FlexibleMediaLinkList?` | no | — | Media handles, or `{ref, region}` objects; see [Image regions](#image-regions) |
| `attributes` | `FlexibleAttributeList?` | no | — | Attributes, each with optional citations, notes and private flag |
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
| `lat` | `FlexibleString?` | no | — | Latitude in decimal degrees, north positive (JSON string or number, coerced to text) |
| `lon` | `FlexibleString?` | no | — | Longitude in decimal degrees, east positive (JSON string or number, coerced to text) |
| `enclosedBy` | `FlexiblePlaceRefList?` | no | — | Parent place refs + optional dates (`enclosedBy`, not `enclosedByHandles`). Dashes → span (`from…to`); open `1991-` → From. English months OK (`from 1 Oct 1929 to 27 Sep 1937`). Unrecognized dates fail validation. Examples: `[{ref, date:"1708-1927"}]`, `HANDLE::1991-` |
| `nameLang` | `string?` | no | — | Language code for primary name |
| `nameDate` | `string?` | no | — | When the primary name applies, same date rules as `alternateNames` (`from 1937-02-10`, `1918-1937`). Gramps shows the first name, primary first, whose date is empty or matches the event date, so an undated primary name hides dated alternate names |
| `alternateNames` | `FlexiblePlaceNameList?` | no | — | Alternate names `{value, lang?, date?}`. Dashes → span; open `1991-` → From; English months / ISO day ranges / mixed `1703-1914-08-31` supported. Unrecognized dates fail validation |
| `noteHandles`, `citationHandles`, `tagHandles` | `FlexibleHandleList?` | no | — | Linked handles |
| `mediaHandles` | `FlexibleMediaLinkList?` | no | — | Media handles, or `{ref, region}` objects; see [Image regions](#image-regions) |
| `code` | `string?` | no | — | Place code / postal reference |
| `isPrivate` | `bool` | no | `false` | Mark private |

The result's header gives the handle, Gramps ID and name, then the stored
`placeType` (Unknown when none was given), `lat`, `lon` and `enclosedBy`
handles, each `none` when empty.

### U — `UpdatePlace`
`linkMode: "replace" | "add" | "remove"` applies to supplied link lists; see [Incremental link updates](#incremental-link-updates).

Update a place. Same fields as create (all optional). `enclosedBy` is a link list and follows `linkMode`; `alternateNames` always uses replacement. Use `enclosedBy` (not `enclosedByHandles`) for parent refs and enclosure dates. Omit `nameDate` or `nameLang` to keep the primary name's date or language; an empty string removes it. The same goes for `lat` and `lon`. The result's header shows the place as saved: its name, `placeType`, `lat`, `lon` and `enclosedBy` handles, whether or not the call changed them.

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
| `repositoryHandles` | `FlexibleRepositoryRefList?` | no | — | Repository refs: handle strings, `"Ref : CallNumber : MediaType"` strings, or `{ref, callNumber, mediaType}` objects (snake_case also accepted); media types are checked against `source_media_types` |
| `noteHandles`, `tagHandles` | `FlexibleHandleList?` | no | — | Linked handles |
| `mediaHandles` | `FlexibleMediaLinkList?` | no | — | Media handles, or `{ref, region}` objects; see [Image regions](#image-regions) |
| `attributes` | `FlexibleAttributeList?` | no | — | Attributes: type, value and private only (no citations or notes) |
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
| `mediaHandles` | `FlexibleMediaLinkList?` | no | — | Media handles, or `{ref, region}` objects; see [Image regions](#image-regions) |
| `tagHandles` | `FlexibleHandleList?` | no | — | Tag handles |
| `attributes` | `FlexibleAttributeList?` | no | — | Attributes: type, value and private only (no citations or notes) |
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

Update a note (same field set, all optional). `text` replaces the whole body;
`replaceText` with `withText` changes one fragment and `appendText` adds to the
end, both working on the body as stored at the time of the call, so a long
research note can be corrected or extended without the caller sending it back
in full. When both are passed, the fragment is replaced first. A fragment that
is not found, or found more than once, is rejected and nothing is saved.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `handle` | `string` | yes | — | Note handle or Gramps ID |
| `text` | `string?` | no | — | New body; changed text loses the old styling and links |
| `appendText` | `string?` | no | — | Text added on a new line at the end (a line break is put in between unless the body ends with one; start with a line break for an empty line). Keeps the rest of the body, styling and links included. Cannot be combined with `text` |
| `replaceText` | `string?` | no | — | Fragment of the body to replace, exactly as stored (spaces, line breaks, letter case); must occur once. Styling and links after it move with the text, those around it stretch or shrink, those wholly inside it are dropped. Cannot be combined with `text` |
| `withText` | `string?` | no | — | Text put in place of `replaceText`; required with it, an empty string deletes the fragment |
| `noteType` | `string?` | no | — | Note type |
| `format` | `string?` | no | — | Plain or Html |
| `tagHandles` | `FlexibleHandleList?` | no | — | Tag handles |
| `isPrivate` | `bool?` | no | — | Private flag |

---

## Media (`MediaTools.cs`) — 3 tools

Use `get_object(objectType: "media", ...)` for media metadata. It does not
upload or download file bytes. Media byte tools/resources require
`GRAMPS_MEDIA_RESOURCES_ENABLED=true`, respect `GRAMPS_MEDIA_MAX_BYTES`, and
block private media records unless `GRAMPS_MEDIA_ALLOW_PRIVATE=true`.
Uploading with `create_media` or `update_media(filePath)` requires
`GRAMPS_MEDIA_RESOURCES_ENABLED=true` and `GRAMPS_MEDIA_UPLOAD_DIRS`, and
respects `GRAMPS_MEDIA_UPLOAD_MAX_BYTES`.

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
| `region` | `double[]?` | no | — | Show only this part of the image, `[left, top, right, bottom]` in percent as on a media link (see [Image regions](#image-regions)), at up to `size` pixels on its long edge; a small region is read from the full-resolution original. Thumbnail mode only |

Migration: replace `get_media_thumbnail(handle, size)` with
`read_media(handle, mode: "thumbnail", size: size)` and replace
`get_media_file(handle)` with `read_media(handle, mode: "file")`.
The old tool names are no longer registered. Media resource URIs are unchanged.

### C — `CreateMedia`
Upload a photo, document scan or PDF from an upload folder as a new media
record, and show it on people, families, events, places, sources or citations
through `attachTo`. Only files inside `GRAMPS_MEDIA_UPLOAD_DIRS` (subfolders
included) can be read; symbolic links are resolved first, so a link cannot lead
outside. The type is judged from the file's first bytes: JPEG, PNG, GIF, WebP,
TIFF, BMP or PDF. Gramps Web does not check uploads for duplicates, so the tool
first looks for a media record with the same MD5 checksum: when one exists,
nothing is uploaded, its fields stay as they are, only `attachTo` is applied,
and the response says `action: existing`. Repeating a call is therefore safe.
An unreadable date or an unknown `attachTo` record is rejected before anything
is uploaded. Each `attachTo` record gets the media after its existing media,
and a record that already shows it is left unchanged. An `attachTo` entry can
be `{"ref": "I0001", "region": [left, top, right, bottom]}` to show only that
part of the image; see [Image regions](#image-regions).

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `filePath` | `string` | yes | — | Absolute path inside an upload folder, `~/…`, or a name relative to a folder, such as `scan.jpg` |
| `description` | `string?` | no | file name without extension | Description (caption) |
| `date` | `string?` | no | — | Date of the photo or document |
| `attachTo` | `FlexibleMediaLinkList?` | no | — | Records to show the media on: Gramps IDs (`I`, `F`, `E`, `P`, `S`, `C`) or handles, or `{ref, region}` objects |
| `citationHandles`, `noteHandles`, `tagHandles` | `FlexibleHandleList?` | no | — | Links on the media record itself |
| `attributes` | `FlexibleAttributeList?` | no | — | Attributes, each with optional citations, notes and private flag |
| `isPrivate` | `bool` | no | `false` | Mark private |

### U — `UpdateMedia`
`linkMode: "replace" | "add" | "remove"` applies to supplied link lists; see [Incremental link updates](#incremental-link-updates).

Update media metadata, or replace the stored file with `filePath` from an
upload folder. The new file is checked like in `CreateMedia`; a file identical
to the stored one uploads nothing, and a file another media record already
holds is rejected. The date and file are checked before anything is saved.
`attachTo` shows the media on more records, whole or as a region, like in
`CreateMedia`; it works alone, without saving the media record. To take the
media off a record, use that record's update tool with `mediaHandles` and
`linkMode: "remove"`.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `handle` | `string` | yes | — | Media handle |
| `description` | `string?` | no | — | Description |
| `date` | `string?` | no | — | Date text; omit to keep the current date, an empty string removes it |
| `noteHandles`, `tagHandles`, `citationHandles` | `FlexibleHandleList?` | no | — | Linked handles |
| `attributes` | `FlexibleAttributeList?` | no | — | Attributes, each with optional citations, notes and private flag |
| `isPrivate` | `bool?` | no | — | Private flag |
| `filePath` | `string?` | no | — | Replacement file inside an upload folder; omit to keep the file |
| `attachTo` | `FlexibleMediaLinkList?` | no | — | Records to show the media on, as in `CreateMedia`; not affected by `linkMode` |

---

## Repository (`RepositoryTools.cs`) — 2 tools

### C — `CreateRepository`
Create a repository: an archive, library, church, website, or other holder of
sources.  **Prerequisites:** `gramps://types`.  Call `list_objects('repositories')`
first to avoid entering one holder twice, and change an existing repository with
`UpdateRepository`. Sources link to the repository afterwards through
`repositoryHandles` on `CreateSource` or `UpdateSource`, which also carry the
call number. `noteHandles` and `tagHandles` only link existing notes and tags.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `name` | `string` | yes | — | Name |
| `repoType` | `string?` | no | — | Repository type, such as `Archive`, `Library`, `Church`, or `Web site`; Gramps stores `Library` when omitted |
| `address` | `string?` | no | — | Postal address as one line, stored as the street of the repository's address |
| `url` | `string?` | no | — | Website, stored as the repository's web home link |
| `noteHandles`, `tagHandles` | `FlexibleHandleList?` | no | — | Existing notes and tags to link |
| `isPrivate` | `bool` | no | `false` | Mark private |

### U — `UpdateRepository`
`linkMode: "replace" | "add" | "remove"` applies to supplied link lists; see [Incremental link updates](#incremental-link-updates).

Update a repository (same field set, all optional).

---

## Tag (`TagTools.cs`) — 2 tools

Tags have no Gramps ID. Their color is stored as `#rrggbb`, the form Gramps
Web and Gramps desktop read; both tools accept six hex digits with or without
`#` and reject anything else before saving.

### C — `CreateTag`
Create a tag.  Call `list_objects('tags')` first to avoid duplicates.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `name` | `string` | yes | — | Display name |
| `color` | `string` | no | `"000000"` | RRGGBB, with or without `#`; stored as `#rrggbb` |
| `priority` | `int` | no | `0` | Sort priority |

### U — `UpdateTag`
Rename a tag or change its color or priority; every record carrying the tag
shows the change. Which records carry it is stored on the records and changes
through their `tagHandles`. A name another tag already uses, ignoring case, is
rejected, because Gramps filters such as `HasTag` find tags by name. The
header reports the stored `name`, `color` and `priority`.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `handle` | `string` | yes | — | Tag handle or exact name (a name that differs only in case is accepted when one tag matches) |
| `name` | `string?` | no | — | New name; empty is rejected |
| `color` | `string?` | no | — | New color, RRGGBB with or without `#` |
| `priority` | `int?` | no | — | New sort priority |

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
| `grampsId` | `string?` | no | — | Filter by Gramps ID (I0001-style); not together with `rules` |
| `sourceHandle` | `string?` | no | — | For citations only: filter by source |
| `gql` | `string?` | no | — | Gramps QL expression |
| `rules` | `object \| array \| string?` | no | — | Gramps filter rules, applied on the server before `gql`; not for tags |
| `sort` | `string?` | no | — | Sort field (prefix `-` for descending) |

`rules` selects records with the filter rules of Gramps itself, which can
follow kinship where Gramps QL cannot: ancestors or descendants of a person,
everyone related to someone, people with no family links. A filter is
`{"function": "and", "invert": false, "rules": [...]}`, where `function` is
`and` (the default), `or` or `one`, `invert: true` keeps the records that do
not match, and each rule is `{"name": "IsAncestorOf", "values": ["I0001", true]}`
with one value per label of the rule, in order, and an optional
`"regex": true`. The array of rules alone, a single rule, and the same JSON as a
string are read too. An item of `rules` can be a nested filter; with
`"namespace"` it matches the records each listed one links to: from people
`Event` or `Family`, from families `Event` or `Person`, from events `Place`,
from citations `Source`, from sources `Repository`. Filters nest at most five
levels deep. A saved filter of the tree is the rule `MatchesFilter` with its
name.

```text
list_objects(objectType: "people", rules: {"rules": [{"name": "Disconnected"}]})
list_objects(objectType: "people", rules: {"invert": true, "rules": [{"name": "IsRelatedWith", "values": ["I0001"]}]})
list_objects(objectType: "people", rules: [{"namespace": "Event", "rules": [{"name": "HasType", "values": ["Residence"]}]}])
```

The shape, keys and namespaces are checked before the request, with the path
of the faulty item (`Invalid rules at rules[0]: …`). A rule name Gramps Web
does not know is named with the closest known one
(`Unknown filter rule: IsRelatedwith for people (did you mean IsRelatedWith?)`),
and a rule Gramps cannot evaluate keeps the server's reason
(`Gramps Web could not apply the rules: …`). Values are not checked: a Gramps
ID, tag or saved filter name the tree does not have matches nothing, so an
empty result with rules says to check those values. Gramps Web reads `true`
and `false` as `1` and `0`. `MatchesQuery` needs Gramps
Web API 3.23 or later. `get_reference(topic: "filter-rules", section: "people")`
lists the rules of a type with their values, and the tree's saved filters.

---

## System (`SystemTools.cs`) — 2 tools

### R — `GetRecentChanges`
Recent transaction history, newest first, under the header
`RECENT CHANGES (20 of 512, newest first)`. Each transaction row shows its UTC
commit time, description, user, an `(undo)` marker for undo transactions, and
`[transaction: id]`, the number `undo_change` takes. Below it, each changed object appears as `Added`,
`Updated`, `Deleted`, or `Changed` with its class and handle, up to 10 objects
per transaction followed by `(+N more changes)`. A link that an edit adds or
removes, which Gramps records as a reference change, appears as
`Added Reference from [handle: …] to [handle: …]`.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `limit` | `int` | no | `20` | Number of transactions (clamped 1–100) |

### R — `GetBookmarks`
The records bookmarked in the tree, under the header `BOOKMARKS (5)`, with
one section per type that has any (`People (2):`, `Families (1):`, …) and each
record as its one-line summary with `[handle: …]`. Bookmarks belong to the
tree and are shared by all its users; they are set in Gramps Web or Gramps
desktop, not through this server. A tree without bookmarks gets `No bookmarks
in this tree.`

---

## Undo (`UndoTools.cs`) — 1 tool

### U — `UndoChange`
Undo one transaction of the edit history through Gramps Web's undo endpoint,
named by the number `get_recent_changes` shows as `[transaction: N]`. Records
the transaction added are deleted, records it updated get their earlier version
back, and records it deleted are restored with the same handles and Gramps IDs.
Gramps Web applies the undo as one new transaction, all or nothing, described
as `Undo transaction N: <description>`; `get_recent_changes` lists it, and
`undo_change` can revert it in turn.

Before writing, the tool looks for conflicts, and any conflict refuses the undo
with nothing changed:

- Gramps Web's own check: a record changed or deleted again after the
  transaction, or a deleted record that exists again;
- a record the undo would delete that records outside the transaction now link
  to;
- an earlier version to restore that links a record which no longer exists.

The two link checks run for transactions of up to 200 records; larger ones
rely on Gramps Web's check alone. The refusal names each conflicting record
and advises undoing the later transactions first, newest first, or passing
`force=true` after the user separately confirms. A forced undo overwrites the
later changes to those records and may leave links dangling.

Gramps Web before API 3.23 (images before 26.10) undoes a deletion only with
`force=true`: its check reports the deleted record as changed. The tool reads
the version from `/api/metadata/`; when nothing else conflicts, the refusal
says that `force=true` would apply exactly the listed changes, so the user can
confirm it or update Gramps Web.

With a task queue, Gramps Web runs the undo in the background. The tool waits
up to 45 seconds for the task; a task still running returns `action: pending`
with its `task` id and the advice not to call `undo_change` again for it.

The reply header carries `type: Transaction`, `action` (`undone`, `checked`
for `dryRun`, `refused`, `pending`, or `unchanged` for a transaction that
changed only links), `transaction`, the undo's own `undo_transaction` once
found, and `conflicts`. The body names the transaction and each change, up to
10 records followed by `(+N more records)`; deleted records are named from
their stored version with their Gramps ID.

```text
undo_change(transactionId: 412, dryRun: true)
undo_change(transactionId: 412)
```

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `transactionId` | `int` | yes | — | The transaction to undo, from `get_recent_changes` |
| `dryRun` | `bool` | no | `false` | List the changes and conflicts without writing |
| `force` | `bool` | no | `false` | Undo despite conflicts, only after a separate confirmation |

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
| Media | 1 | 1 | 1 | 0 | 3 |
| Repository | 0 | 1 | 1 | 0 | 2 |
| Tag | 0 | 1 | 1 | 0 | 2 |
| Object | 1 | 0 | 0 | 1 | 2 |
| Search | 2 | 0 | 0 | 0 | 2 |
| System | 2 | 0 | 0 | 0 | 2 |
| Reference | 1 | 0 | 0 | 0 | 1 |
| Undo | 0 | 0 | 1 | 0 | 1 |
| **Total with media access and upload enabled** | **10** | **10** | **11** | **1** | **32** |

With the default `GRAMPS_MEDIA_RESOURCES_ENABLED=false`, `read_media` and
`create_media` are hidden: the default catalog contains 30 tools in read/write
mode. With media access enabled, `create_media` also needs
`GRAMPS_MEDIA_UPLOAD_DIRS`. Read-only mode publishes 10 tools
with media access enabled, or 9 with it disabled.

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
A deletion can be reverted with `undo_change`, which restores the record with
its handle, Gramps ID and its own links.
