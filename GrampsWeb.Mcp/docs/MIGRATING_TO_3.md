# Migrating from 2.x to 3.0

This guide compares the 2.10 tool catalog with the 3.0 catalog. 3.0 needs a
newer Gramps Web, renames the tools that read, list, merge and delete
records, replaces the nine single-record `create_*` tools with
`create_records`, and makes `linkMode: "add"` the default of the `update_*`
tools. The catalog shrinks from up to 35 tools to up to 26.

Existing Gramps Web trees and records need no migration. Update Gramps Web if
needed, update the MCP server, reconnect the MCP client so it refreshes
`tools/list`, and then update saved tool calls, agent instructions, tool
allowlists and scripts that use the old names. Per-tool permissions that a
client stored for a 2.x tool name do not carry over to the new name.

## Check the Gramps Web version

3.0 requires Gramps Web API 3.16 or newer, which ships with Gramps Web 26.6.2
and later. Before its first request the server reads the API version. With an
older Gramps Web every tool call returns an error naming both versions,
`GET /health` reports unhealthy, and the startup log says so once. The startup
log also shows the API version of a supported server. Keep Gramps Web MCP 2.x
until Gramps Web can be updated.

## Rename tool calls

| 2.x call | 3.0 call |
|----------|----------|
| `get_object(objectType, identifier, extended)` | `get_records(identifiers: [identifier], objectType, extended)` |
| `list_objects(objectType, ...)` | `list_records(objectType, ...)` |
| `merge_objects(objectType, keepHandle, duplicateHandle, ...)` | `merge_records(objectType, keepHandle, duplicateHandle, ...)` |
| `read_media(handle, mode, size, region)` | `get_media_image(handle, mode, size, region)` |
| `delete_object(objectType, handle, force)` | `delete_records(identifiers: [handle], objectType, force)` |
| `create_media(filePath, ...)` | `upload_media(filePath, ...)` |

`list_records`, `merge_records`, `get_media_image` and `upload_media` take the
same arguments as the tools they replace. `get_media_image` and
`upload_media` are published under the same media settings as `read_media`
and `create_media` were.

`get_records` reads up to 20 records and `delete_records` deletes up to 50, of
any types, so several 2.x calls can become one:

```text
get_records(identifiers: ["I0001", "F0002", "E0003"])
delete_records(identifiers: ["E0010", "C0011"])
```

A Gramps ID gives the record type by its prefix. `objectType` is needed only
for handles, and then every handle in the call is of that type, so read or
delete handles of different types in separate calls, or pass Gramps IDs.

`get_records` returns the records in the order given, separated by a line
`---`. A record the tree lacks gets its not-found message in its place while
the others are still read. `extended` applies to the people and families
among them and is refused when there are none.

`delete_records` checks all the records first and deletes nothing when one
is missing or, without `force`, when records outside the call link to them.
With `force`, Gramps Web now removes those links from the other records
instead of leaving them pointing at a missing record. It also deletes the
citations of a deleted source and a family left without father, mother and
children. A place that encloses places outside the call is refused even with
`force`. Each record type is deleted in its own transaction, and
`undo_change` reverts each one.

## Create records with create_records

`create_person`, `create_family`, `create_event`, `create_place`,
`create_source`, `create_citation`, `create_note`, `create_repository` and
`create_tag` are removed. `create_records` takes the same fields, one list
per record type, and creates one or many records in one transaction:

| 2.x call | 3.0 call |
|----------|----------|
| `create_person(primaryName, gender, ...)` | `create_records(people: [{primaryName, gender, ...}])` |
| `create_family(fatherHandle, motherHandle, childRefs, ...)` | `create_records(families: [{fatherHandle, motherHandle, childRefs, ...}])` |
| `create_event(eventType, date, placeHandle, ...)` | `create_records(events: [{eventType, date, placeHandle, ...}])` |
| `create_place(name, placeType, lat, lon, enclosedBy, ...)` | `create_records(places: [{name, placeType, lat, lon, enclosedBy, ...}])` |
| `create_source(title, repositoryHandles, ...)` | `create_records(sources: [{title, repositoryHandles, ...}])` |
| `create_citation(sourceHandle, page, ...)` | `create_records(citations: [{sourceHandle, page, ...}])` |
| `create_note(text, noteType, format, ...)` | `create_records(notes: [{text, noteType, format, ...}])` |
| `create_repository(name, repoType, ...)` | `create_records(repositories: [{name, repoType, ...}])` |
| `create_tag(name, color, priority)` | `create_records(tags: [{name, color, priority}])` |

Records of one call link each other by `"#key"`, so a chain of 2.x calls that
passed each new handle to the next becomes one call:

```text
create_records(
  citations: [{key: "cit", sourceHandle: "S0001", page: "f. 12, No. 14"}],
  events: [{key: "birth", eventType: "Birth", date: "1881-03-02", citationHandles: ["#cit"]}],
  people: [{key: "child", primaryName: {given: "Ivan", surname: "Petrov"}, gender: "male", eventRefs: ["#birth"]}],
  families: [{fatherHandle: "I0012", childRefs: ["#child"]}])
```

Either all the records are saved or none, and one `undo_change` removes them
all. New people get the same duplicate check as `create_person` did, with
`action: refused` and `notDuplicateOf`. A tag name the tree already has stops
the call with an error that gives the existing tag's handle.

A new person's `familyHandles` and `parentFamilyHandles` are no longer taken.
Pass the person in a family's `fatherHandle`, `motherHandle` or `childRefs`
instead, in the same call or with `update_family` for an existing family.

The reply has `type: Transaction` and one line per record with its handle,
Gramps ID and key, instead of the single-record header of the `create_*`
tools.

## Review update calls that omit linkMode

`linkMode` on the `update_*` tools now defaults to `add` instead of
`replace`. **A saved 2.x call that passed a link list without `linkMode`
replaced the stored list; in 3.0 the same call adds to it.** Calls that
already passed `linkMode` behave as before, apart from the changes to `add`
and `replace` below.

| To | 2.x call | 3.0 call |
|----|----------|----------|
| Add one link | `update_event(handle, citationHandles: ["C0001"], linkMode: "add")` | `update_event(handle, citationHandles: ["C0001"])` |
| Set a whole list | `update_person(handle, noteHandles: ["N0001", "N0002"])` | `update_person(handle, noteHandles: ["N0001", "N0002"], linkMode: "replace")` |
| Remove every link of a kind | `update_person(handle, noteHandles: [])` | `update_person(handle, noteHandles: [], linkMode: "replace")` |
| Change one event role | Read `eventRefs`, then pass the whole edited list | `update_person(handle, eventRefs: ["E0001::Witness"])` |
| Change a child's relations | Read `childRefs`, then pass the whole edited list | `update_family(handle, childRefs: [{ref: "I0045", frel: "Adopted"}])` |
| Change a call number | Read `repositoryHandles`, then pass the whole edited list | `update_source(handle, repositoryHandles: [{ref: "R0001", callNumber: "F. 1, op. 2, d. 3"}])` |

`add` now updates a link that is already stored with the fields the call sends
for it: an event's role, a child's `frel` and `mrel`, a repository's call
number and media type, an enclosing place's dates. It adds the citations,
notes and attributes the link lacks and keeps the rest. When a record links
the same handle more than once, a link with a role updates the stored link
with that role first. A bare handle, without a role or relation, changes
nothing on a stored link.

`[]` changes nothing in `add` or `remove` mode, so an update whose only
arguments are empty link lists is refused unless `linkMode` is `replace`.

In `replace` mode a bare event or child handle now keeps the role or
relations of its stored link. 2.x reset it to `Primary` (`Family` for a
family's events) or `Birth`.

Names, attributes, addresses and URLs still replace their whole lists,
whatever `linkMode` says.

## Use Gramps IDs in link fields

Link fields in `create_records`, the `update_*` tools and `upload_media`
take Gramps IDs as well as handles: the `…Handles` lists, `eventRefs`,
`childRefs`, `personAssociations`, `enclosedBy`, `fatherHandle`,
`motherHandle`, `placeHandle`, `sourceHandle`, and the citations and notes of
names, attributes, addresses and links. 2.x saved a Gramps ID in a link list
as if it were a handle; a 2.x call that worked needs no change.
An ID that no record of the field's type has now stops the call before
anything is saved. Tags have no Gramps ID, so a tag field takes handles only.

## Migration checklist

1. Check that Gramps Web runs API 3.16 or newer (Gramps Web 26.6.2 or newer).
2. Upgrade the server and reconnect the MCP client so it refreshes its tools.
3. Replace removed and renamed names in saved prompts, scripts and tool
   allowlists using the tables above, and approve the renamed tools again
   where a client asks per tool.
4. Find every saved `update_*` call that passes a link list without
   `linkMode`. Add `linkMode: "replace"` where it must set the whole list.
5. Test a read (`get_records`), a `create_records` call and an
   `update_*` call on a test record before resuming automated edits, and
   remove the test records with `undo_change`.
6. If an integration parses tool responses, review the replies of
   `get_records`, `create_records` and `delete_records` as well as the tool
   names.

For current parameters and examples, see the [tool catalog](TOOL_CATALOG.md).
Upgrading from 1.x? Follow the [2.0 migration guide](MIGRATING_TO_2.md) first
for the tools 2.0 replaced, then this guide.
