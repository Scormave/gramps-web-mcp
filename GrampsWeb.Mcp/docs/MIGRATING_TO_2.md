# Migrating from 1.x to 2.0

This guide compares the 1.0.8 tool catalog with the 2.0 catalog.
The MCP tool catalog shrinks from 57 tools to at most 31. Existing Gramps Web
trees and records need no migration: update your MCP server and then update
saved tool calls, agent instructions, and client configurations that use the
old tool names. Clients should refresh `tools/list` after the upgrade.

## Replace removed tool calls

`objectType` is **singular** in `get_object`, `delete_object`, and
`get_timeline`: `person`, `family`, `event`, `place`, `source`, `citation`,
`note`, `media`, `repository`, or `tag`. `get_timeline` accepts only `person`,
`family`, and `place`. A Gramps ID such as `I0001` lets `get_object` infer
the object type; an opaque handle requires `objectType`.

| 1.x tool | 2.0 call |
|------------|----------|
| `get_person(handle)` | `get_object(objectType: "person", identifier: handle)` |
| `get_family(handle)` | `get_object(objectType: "family", identifier: handle)` |
| `get_event(handle)` | `get_object(objectType: "event", identifier: handle)` |
| `get_place(handle)` | `get_object(objectType: "place", identifier: handle)` |
| `get_source(handle)` | `get_object(objectType: "source", identifier: handle)` |
| `get_citation(handle)` | `get_object(objectType: "citation", identifier: handle)` |
| `get_note(handle)` | `get_object(objectType: "note", identifier: handle)` |
| `get_media(handle)` | `get_object(objectType: "media", identifier: handle)`; this reads metadata, not file bytes |
| `get_repository(handle)` | `get_object(objectType: "repository", identifier: handle)` |
| `get_tag(handle)` | `get_object(objectType: "tag", identifier: handle)` |
| `find_by_gramps_id(grampsId)` | `get_object(identifier: grampsId)` |

For the old `get_person(handle, extended: true)` or
`get_family(handle, extended: true)`, pass `extended: true` to `get_object`.
Other object types do not accept `extended: true`.

| 1.x tool | 2.0 call |
|------------|----------|
| `delete_person(handle, force?)` | `delete_object(objectType: "person", handle: handle, force: force)` |
| `delete_family(handle, force?)` | `delete_object(objectType: "family", handle: handle, force: force)` |
| `delete_event(handle, force?)` | `delete_object(objectType: "event", handle: handle, force: force)` |
| `delete_place(handle, force?)` | `delete_object(objectType: "place", handle: handle, force: force)` |
| `delete_source(handle, force?)` | `delete_object(objectType: "source", handle: handle, force: force)` |
| `delete_citation(handle, force?)` | `delete_object(objectType: "citation", handle: handle, force: force)` |
| `delete_note(handle, force?)` | `delete_object(objectType: "note", handle: handle, force: force)` |
| `delete_media(handle, force?)` | `delete_object(objectType: "media", handle: handle, force: force)` |
| `delete_repository(handle, force?)` | `delete_object(objectType: "repository", handle: handle, force: force)` |
| `delete_tag(handle, force?)` | `delete_object(objectType: "tag", handle: handle, force: force)` |

Backlink checks and the meaning of `force` remain the same. Deletion is still
blocked by default when another record references the target.

| 1.x tool | 2.0 call |
|------------|----------|
| `get_input_guide()` | `get_reference(topic: "input-guide")` |
| `get_types()` | `get_reference(topic: "types")` |
| `get_metadata()` | `get_reference(topic: "metadata")` |
| `get_name_settings()` | `get_reference(topic: "name-settings")` |
| `get_ancestors(handle, generations?, kinshipLabels?)` | `get_person_tree(person: handle, direction: "ancestors", generations: ..., kinshipLabels: ...)` |
| `get_descendants(handle, generations?, kinshipLabels?)` | `get_person_tree(person: handle, direction: "descendants", generations: ..., kinshipLabels: ...)` |
| `get_person_timeline(handle, events?, relatives?, relativeEvents?, dates?)` | `get_timeline(objectType: "person", identifier: handle, events: ..., relatives: ..., relativeEvents: ..., dates: ...)` |
| `get_family_timeline(handle, events?, dates?)` | `get_timeline(objectType: "family", identifier: handle, events: ..., dates: ...)` |
| `get_place_timeline(handle, events?, dates?)` | `get_timeline(objectType: "place", identifier: handle, events: ..., dates: ...)` |
| `get_media_thumbnail(handle, size?)` | `read_media(handle: handle, mode: "thumbnail", size: size)` |
| `get_media_file(handle)` | `read_media(handle: handle, mode: "file")` |

Omit optional arguments shown as `...` when you did not pass them before.
`get_person_tree` still defaults to three generations. `read_media` defaults
to thumbnail mode, so `mode: "thumbnail"` may be omitted. In 2.0 the default
thumbnail was 256 pixels; later releases render a 1568-pixel JPEG or PNG
preview from the original, so pass `size: 256` to keep the smaller one.
**Do not pass `size` in file mode**; it is now rejected. Timeline
relative filters are valid only for `objectType: "person"`.

All nine `create_*` tools, nine `update_*` tools, `quick_add_person`,
`add_event_to_person`, `search`, `list_objects`, `get_relations`,
`get_bookmarks`, and `get_recent_changes` keep their names. Their saved
calls do not need renaming.

## Use smaller reference responses

The four reference resources retain their URIs: `gramps://input-guide`,
`gramps://types`, `gramps://metadata`, and `gramps://name-settings`.
`get_reference` can return just the part needed for a task:

```text
get_reference(topic: "input-guide", section: "dates")
get_reference(topic: "input-guide", section: "link_updates")
get_reference(topic: "types", section: "event_types")
get_reference(topic: "name-settings", section: "formats")
```

Omitting `section` returns the full topic, like the old reference tools.
Use [the tool catalog](TOOL_CATALOG.md)
for all accepted sections. The input-guide now includes `link_updates`.

## Update links without copying a whole list

The nine `update_*` tools have an optional `linkMode` for link-list parameters:
`replace` (default), `add`, or `remove`. Old calls that omit `linkMode` keep
replacement behavior. To add one citation to an event without replacing its
other citations:

```text
update_event(handle: "EVENT_HANDLE", citationHandles: ["CITATION_HANDLE"], linkMode: "add")
```

To remove that citation, use the same call with `linkMode: "remove"`.
Omitted lists stay unchanged. `[]` clears a list only in `replace` mode;
it does nothing in `add` or `remove` mode. Existing link metadata is kept
when adding or removing. To change an existing event role or child
relationship, read the complete list and replace it with the edited version.
The mode applies to **every supplied link list** in the call. Names,
attributes, addresses, and URLs still use replacement semantics.

## Check tool availability after reconnecting

With `GRAMPS_READ_ONLY=true`, 2.0 publishes only read tools in `tools/list`.
Write requests sent by name are still blocked server-side. If you previously
saved a write tool name in a read-only client, refresh the catalog after
changing the setting. The Claude Desktop MCPB setup defaults to read-only;
the server's `GRAMPS_READ_ONLY` default is `false`.

`read_media` appears in `tools/list` only when
`GRAMPS_MEDIA_RESOURCES_ENABLED=true`. Media metadata through `get_object`
is available when file access is disabled. Media resource URIs remain
`gramps://media/{handle}/thumbnail/{size}` and
`gramps://media/{handle}/file`; size and private-record safeguards continue
to apply. Since 2.1.0 the server ignores `GRAMPS_MEDIA_ALLOWED_MIME_TYPES` and
logs a startup warning while it is set; remove it from your configuration.

The stdio and HTTP transport setup is unchanged. Existing username/password
configuration still works; `GRAMPS_REFRESH_TOKEN` is now an alternative for
instances without local password login. `GRAMPS_MUTATION_SERIALIZE` retains
its default of `true` and also serializes update read/modify/write sequences
within this server process. It does not lock other MCP replicas or Gramps
Web clients.

## Migration checklist

1. Upgrade the server and reconnect the MCP client so it refreshes its tools.
2. Replace removed names in saved prompts, scripts, and tool allowlists using
   the tables above. Add `objectType` for opaque handles, or use a Gramps ID
   with `get_object`.
3. Review read-only and media settings if expected tools are absent from
   `tools/list`.
4. Test a read (`get_object`), a focused reference (`get_reference`), and a
   representative write on a test record before resuming automated edits.
5. If an integration parses tool responses, review the updated creation hints,
   pagination labels, and `read_media` content type as well as tool names.

For current parameters and examples, see the [tool catalog](TOOL_CATALOG.md).
