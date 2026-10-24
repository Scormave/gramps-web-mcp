# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- `undo_change`: undo one transaction listed by `get_recent_changes`, by
  its `[transaction: N]` number. Records it added are deleted, records it
  updated get their earlier version back, and records it deleted are
  restored with the same handles and Gramps IDs, as one new all-or-nothing
  transaction that `undo_change` can revert in turn. `dryRun` lists the
  changes without writing. Any conflict refuses the undo with nothing
  changed and names the records: one changed again after the transaction
  (Gramps Web's own check), one to delete that other records now link to,
  or an earlier version that links a record which no longer exists. Undo
  the later transactions first, newest first, or pass `force=true` after
  a separate confirmation. Gramps Web before API 3.23 restores deleted
  records only with `force=true`; the refusal says so when nothing else
  conflicts. A background undo (Gramps Web with a task queue) is followed
  for up to 45 seconds and otherwise reported as `pending`
- Confirmation dialogs: in clients that support MCP elicitation,
  `delete_object` and `undo_change` ask the user, not the model, before
  they change the tree. The dialog names the record to delete and, with
  `force=true`, the records that will keep a link to it, or lists what
  the undo will delete and restore and the conflicts it will override.
  Only a ticked box goes ahead; declining or closing the dialog returns
  `action: declined` or `action: cancelled` with nothing changed. The
  record, or the transaction's conflicts, are checked again after the
  answer, and a change made while the user was deciding refuses the call.
  Dry runs and refusals do not ask
- `GRAMPS_CONFIRMATION`: `when-supported` (default) asks clients that
  support elicitation and runs as before in the others, `required`
  refuses deletes and undos in clients that cannot ask
  (`action: not_confirmed`), `off` never asks. Stateless Streamable HTTP
  cannot ask; `MCP_STATELESS=false` enables the dialog over HTTP. The
  server logs once per client whether it asks
- A test keeps the documentation in step with the registered tools,
  resources and prompts: the tool catalog's sections, R/C/U/D letters,
  summary table and per-profile counts, its resource and prompt tables,
  and the counts in the READMEs, the MCPB manifest and the site. Docs,
  tool descriptions, prompts and the server instructions may name only
  registered tools, and the instructions of each profile only the tools
  it publishes
- `delete_object` and `undo_change` carry the tool metadata
  `anthropic/requiresUserInteraction`, so clients that honour it ask
  their own permission before the call

### Changed

- `delete_object` no longer describes a deletion as permanent:
  `undo_change` with the deletion's transaction restores the record.
  The server instructions and the `get_recent_changes` description say
  when to use `undo_change` and where its transaction number comes from

## [2.6.0] - 2026-10-16

### Added

- `list_objects` takes `rules`: Gramps filter rules, applied on the server
  before `gql`, which follow kinship where Gramps QL cannot, such as the
  ancestors or descendants of a person, everyone related to someone
  (inverted: everyone who is not) or people with no family links. A filter
  is `{"function", "invert", "rules": [{"name", "values", "regex"}]}`;
  the array of rules alone, one rule and a JSON string are read too. Nested
  filters with a `namespace` match the records each listed one links to,
  such as people by their events, and `MatchesFilter` uses a filter saved
  in the tree. The shape is checked before the request with the path of
  the faulty item, an unknown rule is named with the closest known one,
  a rule Gramps cannot evaluate keeps the server's reason, and an empty
  result says that a missing ID, tag or filter name matches nothing
- `get_reference` topic `filter-rules` and the resources
  `gramps://filter-rules` and `gramps://filter-rules/{objectType}`: how to
  write `rules`, then the rules of one type by category with the values
  each takes (their kinds come from Gramps Web API 3.23; on older versions
  the labels alone) and the tree's saved filters
- `update_tag`: rename a tag or change its color or priority; every record
  carrying the tag shows the change. The tag is named by its handle or its
  exact name, since tags have no Gramps ID. A name another tag already
  uses, ignoring case, is rejected, because Gramps filters such as `HasTag`
  find tags by name
- `--health-check [url]` command-line option: instead of starting a server,
  request `GET /health` of a running one and exit with 0 on HTTP 200 and 1
  otherwise. Without a URL it takes the address from `ASPNETCORE_URLS` (or
  `DOTNET_URLS`, `ASPNETCORE_HTTP_PORTS`), with `0.0.0.0`, `[::]`, `*` and
  `+` replaced by `localhost`; with `MCP_TRANSPORT=stdio` it reports
  `skipped` and exits with 0, since there is no HTTP endpoint
- Integration tests run the server against a real Gramps Web (26.10.1) in
  Docker and call it through the MCP protocol: people and families with
  their links and child order, dates in the formats the tools accept,
  places, sources and citations, notes, tags, media uploads and previews,
  deletion with backlinks, sign-in by password and by refresh token,
  read-only mode and the health check. CI runs them on every push and pull
  request, and Dependabot proposes each new Gramps Web release, so its pull
  request shows whether the server still works with it. Locally:
  `GRAMPS_INTEGRATION_TESTS=true dotnet test GrampsWeb.Mcp.IntegrationTests`
  (needs Docker)

### Changed

- The Docker image runs on `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`
  instead of `10.0-noble`: no shell, package manager or curl, about half the
  size (54 MB to download instead of 97 MB) and a smaller set of system
  packages to patch. Its `HEALTHCHECK` runs
  `dotnet /app/GrampsWeb.Mcp.dll --health-check` in place of curl. A health
  check defined outside the image with `curl`, such as in an older copy of
  `docker-compose.example.yml`, fails on the new image and must switch to
  `["CMD", "dotnet", "/app/GrampsWeb.Mcp.dll", "--health-check"]`. The
  image has no ICU, so .NET runs in invariant globalization mode; the server
  already parses and formats dates and numbers with the invariant culture,
  and its tests pass in that mode
- The server starts about 70 ms faster (stdio, until the answer to
  `initialize`: 264 ms instead of 332 ms). The prompts return plain text
  instead of a `ChatMessage`, so the MCP SDK no longer builds a JSON schema
  for the `ChatMessage` type at startup. `prompts/list` and `prompts/get`
  answer exactly as before
- Tool descriptions say when to use each tool and which one to use
  instead: every `update_*` tool names its `create_*` tool and
  `delete_object`, and where a link is kept on the other record, the tool
  to change it there. Write tools say that writes run one at a time, need
  a Gramps Web account allowed to edit the tree, and that a locked or
  rate-limited write fails with the delay to wait. They also explain how
  arguments work together, such as `rules` before `gql` in `list_objects`,
  lists that `linkMode` does not affect, and `sort` reordering links
  without changing them, and what a call returns or rejects. In replace
  mode, `[]` removes the links, not the linked records
- The `update_*` tools declare `destructiveHint: true`, like
  `delete_object`: they overwrite the fields passed, and replace mode
  removes links. Clients that ask before a destructive call now ask for
  them too. The `create_*` tools stay non-destructive, since they only add
- Every tool declares `openWorldHint: false`, since it works only on the
  configured tree, and every write tool declares `idempotentHint`. It is
  true for the `update_*` tools except `update_note`, for `create_media`
  (an identical file returns the stored media) and for `delete_object`,
  where repeating a call changes nothing more; false for the other
  `create_*` tools, which add another record on each call, and for
  `update_note`, whose `appendText` appends again

### Fixed

- When Gramps Web rejects the access token, the server now gets a new one and
  sends the request once more, for tool calls and for `GET /health`, as
  2.1.0 meant it to. The server looked for the error text under
  flask-jwt-extended's default key `msg`, but Gramps Web names it `message`,
  so it never recognized a rejected token: after Gramps Web restarted with a
  new `SECRET_KEY`, every tool call failed with HTTP 422 "Signature
  verification failed" and `/health` answered 503 until the cached token was
  due for renewal, up to 15 minutes later. The integration tests found it
- `create_tag` stored the color as given, so the documented form `FF5733`
  and the default `000000` were saved without `#`, which is not a valid CSS
  or GTK color for Gramps Web or Gramps desktop to show. The color is now
  stored as `#rrggbb`, as Gramps Web stores it: six hex digits are
  accepted with or without `#`, anything else is rejected before saving.
  Tags created earlier keep their color; correct it with `update_tag`

## [2.5.2] - 2026-10-15

### Changed

- Update the MCP C# SDK (`ModelContextProtocol` and
  `ModelContextProtocol.AspNetCore`) from 1.3.0 to 2.2.0. Clients on protocol
  versions 2024-11-05 through 2025-11-25 get the same tools, resources,
  prompts and replies as before, over stdio, Streamable HTTP (stateless and
  stateful) and legacy SSE. Tools no longer advertise
  `execution.taskSupport`: the SDK moved its experimental tasks to a separate
  package, and the server never used them. A client that asks for protocol
  version 2026-07-28 in `initialize` now gets error -32022 with the list of
  supported versions; 1.3.0 answered with 2025-11-25

### Security

- Over HTTP and SSE the server now refuses with 403 any request that carries
  an `Origin` header not listed in the new `MCP_ALLOWED_ORIGINS`
  (comma-separated, empty by default), as the MCP specification requires.
  Without the check, a web page on another site could call tools with the
  server's Gramps credentials through DNS rebinding: a name the page
  controls is pointed at the machine running the server, so the browser
  treats the page and the server as one site. That worked on a server bound
  to `127.0.0.1` without `MCP_API_KEY`. MCP clients outside a browser send
  no `Origin` and work as before. An entry that is not an `http` or `https`
  origin stops the server at startup

## [2.5.1] - 2026-10-14

### Changed

- Uploading needs media file access too: `create_media` and `filePath` on
  `update_media` work only while `GRAMPS_MEDIA_RESOURCES_ENABLED=true`, so
  one setting, "Enable media file access" in the Claude Desktop extension,
  covers every file the server reads or uploads. Before, the extension's
  default upload folder let `create_media` read `~/Downloads` while media
  access was off. A server with `GRAMPS_MEDIA_UPLOAD_DIRS` set and media
  access off now hides `create_media` and logs why at startup

### Fixed

- The Claude Desktop extension did not start while its "Media upload folder"
  setting stayed at the default. Claude Desktop passes that default,
  `${HOME}/Downloads`, to the server without expanding it, and the server
  refused it as not an absolute path. A leading `${HOME}` in
  `GRAMPS_MEDIA_UPLOAD_DIRS` now expands to the home folder, as `~` does

## [2.5.0] - 2026-10-14

### Added

- `sort` on `update_person` and `update_family` puts the named lists in date
  order after the other changes: `Events` as `sortEvents` did, and now also
  `Children` (`update_family`), `Families` (`update_person`), `Citations` and
  `Media`. A child or family added with `linkMode: "add"` used to land at the
  end, and putting it in place meant sending the whole list again in `replace`
  mode. Children go by birth date, else baptism or christening date; a
  person's families by the marriage date, else another marriage event such as
  banns, else the birth of the first child; citations and media by their own
  date. Undated entries go last, equal dates keep their order, and the
  references keep their relationships, crops and other metadata. `sort`
  takes a JSON array or one string such as `"Events | Children"`, and rejects
  a list the tool does not have, naming the ones it does
- `create_media` uploads a photo, document scan or PDF from an upload folder
  as a new media record and shows it on the people, families, events, places,
  sources or citations named in `attachTo`; `update_media` takes `filePath` to
  replace a record's file. Set `GRAMPS_MEDIA_UPLOAD_DIRS` to the folders files
  may come from (`create_media` stays hidden until then) and optionally
  `GRAMPS_MEDIA_UPLOAD_MAX_BYTES` (default 50 MiB); the MCPB extension offers
  an upload folder setting that defaults to `~/Downloads`. Symbolic links are
  resolved before the folder check, and the type is judged by content: JPEG,
  PNG, GIF, WebP, TIFF, BMP or PDF. A file the tree already stores, by MD5
  checksum, is reused instead of uploaded twice, so repeating a call is safe.
  Unknown `attachTo` records and unreadable dates are rejected before upload
- `appendText` on `update_note` adds text to the end of a note on a new line,
  keeping the rest of the body with its styling and links. A long research
  note no longer has to be sent back in full to add one finding, and text
  saved meanwhile by someone else is kept, since the tool appends to the body
  as stored at the time of the call
- `replaceText` with `withText` on `update_note` changes one fragment of a
  note, so a typo or a single item in a long research note is fixed without
  sending the whole text back. The fragment must occur exactly once, else the
  call is rejected and nothing is saved; an empty `withText` deletes it.
  Styling and links after the fragment move with the text, and those around
  it stretch or shrink with it
- A media link can show only part of an image, such as one record on a page
  scan: `mediaHandles` and `attachTo` take `{"ref": …, "region": [left, top,
  right, bottom]}` in place of a handle, with edges in percent of the image as
  Gramps stores a crop. A plain handle keeps the stored region; in add mode,
  and always with `attachTo`, a record already showing the media keeps its
  link with its citations and privacy and takes the region, and listing the
  media twice shows two regions. Repeating a call changes nothing, and an
  empty or out-of-range region is rejected before anything is saved.
  `read_media` takes `region` to show what a region covers, read from the
  full-resolution original when it is small, and records list each media
  link with its region. `update_media` takes `attachTo` too, and applies it
  without saving the media record when nothing else changes

### Changed

- Building `GrampsWeb.Mcp` in Release now needs a Six Labors license key, which
  ImageSharp 4 checks before it compiles the project. The repository commits a
  free community key for the project's assemblies as
  `GrampsWeb.Mcp/sixlabors.lic`, so builds, the Docker image and MCPB bundles
  need no setup. The key expires on 2028-01-12; a weekly workflow opens an issue
  when it is due for renewal, and a test fails 30 days before it expires.
  Built binaries do not check the key

### Removed

- `sortEvents` on `update_person` and `update_family`. Use `sort: "Events"`;
  a call that still passes `sortEvents` fails with an unknown-argument error
  that suggests `sort`

### Fixed

- The citation card that `get_object` returns labelled the citation's date
  "Access Date". Gramps keeps there the date of the cited entry, which is how
  `create_citation` and `update_citation` describe it, so a date such as the
  year of a register entry read as the day the source was consulted. The card
  now shows it as `Date:`

### Security

- Update SixLabors.ImageSharp from 3.1.12 to 4.1.3. Two advisories fixed in
  4.1.2 affect the thumbnails that `read_media` and the media thumbnail
  resource render from originals: an ICC profile with forged lookup-table
  dimensions could make the decoder allocate far more memory than the image
  needs
  ([GHSA-gwg2-r3hj-4w44](https://github.com/advisories/GHSA-gwg2-r3hj-4w44)),
  and a BigTIFF file with a forged directory count could keep a decoder thread
  looping ([GHSA-wmxv-xphr-5c9g](https://github.com/advisories/GHSA-wmxv-xphr-5c9g)).
  The other three advisories of that release concern the TIFF CCITT encoders
  and histogram equalization, which the server does not use

## [2.4.0] - 2026-10-08

### Added

- `scripts/find-update-losses.py` finds data that update tools before 2.3.1
  dropped ([#5](https://github.com/Scormave/gramps-web-mcp/issues/5)). It
  reads the Gramps Web transaction history, lists every value an update
  emptied, with the value it had, and with `--removed` every list entry an
  update removed, and says whether the record still lacks it. It only
  reads; `--user`, `--since` and `--until` narrow the scan to the server's
  account and the time before the upgrade
- `sortEvents` on `update_person` and `update_family` sorts the event list by
  event date after the other changes. An event added with `linkMode: "add"`
  used to land at the end, and putting it in place meant sending the whole
  list again in `replace` mode. The references are only moved, so their roles,
  notes, citations and privacy stay. Undated Birth, Baptism and Christening go
  first, other undated events last with Death and Burial at the end. On the
  same date: "before" dates, then Residence and Occupation, Birth, Baptism,
  other events, Death and Burial, then "after" dates

### Fixed

- `create_person` sets `birth_ref_index` and `death_ref_index` to the first
  Birth and the first Death the person has the Primary role in, as Gramps
  does. Gramps Web sets them only when a person is saved with PUT, so a person
  created with a Birth or Death had no birth or death of record (in Gramps
  Web lists, charts and sorting by birth date) until their next update
- `create_person`, `update_person`, `create_family` and `update_family` check
  the role of each event link. A misspelt role such as `Witnes` used to be
  saved, and Gramps stored it as a new custom role that the person's events
  then showed instead of Witness. A role that is neither a standard Gramps
  role nor a custom one the tree already uses is now rejected with
  suggestions before anything is saved, and a role sent in another case, such
  as `witness`, is stored in its known spelling. `linkMode: "remove"` matches
  links by handle only, so its roles are not checked
- Event, place, note, repository and family relationship types are stored in
  their known spelling. The check accepted a type in any case, but Gramps
  matches type names case-sensitively, so `birth` or `village` was saved as a
  new custom type next to Birth or Village
- Name types, surname origins, attribute types, URL types, the relations of
  child links (`frel`, `mrel`) and the media types of repository links are
  checked the same way. A misspelt value or one sent in another case, such as
  `married name`, `father age` or `adopted`, used to be saved as a new custom
  type next to the standard one; now an unknown value is rejected with
  suggestions before anything is saved, and a known one is stored in its
  known spelling, including the unknown surname origin, which Gramps spells
  with a trailing space. Person, family, event and media attributes accept an
  attribute type the tree uses on any of them; source and citation
  attributes have types of their own. The surname origin `Custom` is no
  longer offered, since Gramps stored it as a custom origin named "Custom".
  `linkMode: "remove"` does not check child or repository links
- `create_place` and `update_place` show the place's type, coordinates and
  enclosing places in their result, and `update_place` its name. Clients such
  as Claude Code drop an argument a tool does not have before calling it, so
  `type`, `latitude` or `longitude` in place of `placeType`, `lat` and `lon`
  created a place of type Unknown without coordinates, or left an updated
  place's type and coordinates as they were, and the result only said that
  the place was saved. It now shows `placeType: "Unknown"` and `lat: none`
- Create results show the new record's name in its own script. A name in
  Cyrillic or with Polish letters was printed with `\u` escapes

## [2.3.1] - 2026-10-07

### Removed

- The `text` parameter of `create_citation` and `update_citation`. Gramps
  citations have no text field, so the transcript was never stored, and
  Gramps Web 3.23 and later refuse a citation that carries one. Keep a
  transcript in a note (`create_note` with type Transcript) linked through
  `noteHandles`

### Fixed

- Update tools no longer drop data they were not asked to change
  ([#5](https://github.com/Scormave/gramps-web-mcp/issues/5), reported by
  @hcirlu). Gramps Web replaces the whole object on save, and the tools
  rebuilt it from the fields they knew, so any update, even one that only
  added a note, lost the rest: citations, notes and privacy of attributes,
  the styling and links of note text, LDS ordinances, privacy and citations
  of event and child references, place URLs and alternate locations, media
  checksums, and fields of newer Gramps versions. The tools now change the
  stored object in place and send everything else back as it was
- In `replace` mode a reference that stays keeps its stored privacy,
  citations, notes and crop, matched in order when the same handle is linked
  more than once, and an attribute with the same type and value as a stored
  one keeps its citations, notes and privacy. `update_note` keeps the styling
  and links of the text when `text` is omitted or unchanged
- Create tools keep the private flag given with an attribute, and
  `create_person`, `create_family` and `create_event` also keep its
  citations and notes; they kept only the type and value
- Source and citation attributes have no citations or notes in Gramps.
  `create_source`, `update_source`, `create_citation` and `update_citation`
  now refuse such an attribute before saving anything instead of dropping
  them

## [2.3.0] - 2026-10-05

### Added

- `create_place` and `update_place` take `nameDate`, the date of the primary
  place name (`from 1937-02-10`, `1918-1937`); on update an empty string
  removes it. Gramps shows the first name, primary first, whose date is empty
  or matches the event date, so dated alternate names never showed while the
  primary name had no date, and the tools had no way to give it one. The
  place card shows the primary name's language and date
- The server sends MCP instructions at initialization: start with `search`
  or `list_objects`, which tool answers which kinship question, the order to
  create linked records in, that links are stored on one side, how `linkMode`
  works, and to delete only after confirmation. Read-only mode leaves out
  the writing part, and `read_media` is named only when media access is
  enabled

### Changed

- Every tool and parameter description is rewritten for agents: what the
  tool does and returns, which sibling tool to use instead, prerequisites,
  and what happens on duplicates, partial failure, or bad input. The
  "Read-only:" and "(write)" prefixes are gone, since the tool annotations
  already say so, and repeated pointers to the input guide are shortened to
  shared fragments. `get_reference` no longer calls itself "compatibility
  access", and `delete_object`, `create_tag` and `update_repository` say
  what they return and what they do not check
- Describe in `create_repository` what a repository is, to check
  `list_objects('repositories')` first, that sources link to it through
  `repositoryHandles`, that `noteHandles` and `tagHandles` only link existing
  notes and tags, and the type Gramps stores when `repoType` is omitted;
  describe in `get_bookmarks` what it lists and when to use `search`,
  `list_objects`, or `get_recent_changes` instead
- Update `Microsoft.Extensions.Hosting` and `Microsoft.Extensions.Http` to
  10.0.12

### Removed

- `quick_add_person` and `add_event_to_person`. With `linkMode: "add"` they
  only saved a call or two, and they did what no other write tool does:
  they created a place from a bare name, without type or enclosing place,
  whenever no place had exactly that name; they took no citations;
  `add_event_to_person` saved an unknown event type as a new custom type; and
  a failure halfway left the records already created. Use instead:
  - `add_event_to_person(personHandle, eventType, date, place, role)` →
    `create_event(eventType, date, placeHandle)`, then
    `update_person(handle, eventRefs: [{ref: <event handle>, role}],
    linkMode: "add")`
  - `quick_add_person(name, gender, birthDate, birthPlace, deathDate,
    deathPlace)` → `create_event` for the Birth and Death, then
    `create_person(primaryName, gender, eventRefs)`
  - a place given by name → find it with `search`, and create it with
    `create_place` only if it is not in the tree

  The `add-person` and `import-from-text` prompts and the next-step hints
  after `create_person` and `create_place` now walk through these calls

### Fixed

- `create_place` and `update_place` accept `lat` and `lon` as JSON numbers;
  `52.19` was rejected with "Argument lat must be a string". Coordinates are
  trimmed, and one sent with its own double quotes (`"\"52.19\""`) is stored
  without them
- Every text parameter accepts a JSON number as its text, so a year-only date
  sent as `1877` (`date`, `nameDate`, `dates`, …) works; it was rejected
  with "Argument date must be a string". A number given as the `date` of an
  `alternateNames` or `enclosedBy` entry was silently dropped and is now
  read as a year
- `create_place` and `create_repository` report the new record's name; the
  result showed its type (`name: "Hamlet"`, `name: "Archive"`) though the
  stored name was right
- An `update_*` call with no field to change is refused with the tool's
  parameter list. Clients such as Claude Code drop arguments a tool does not
  have, so `update_event(place: …)` reached the server as an update of nothing,
  answered "updated" and saved the event unchanged
- `get_bookmarks` lists the bookmarked records by type, each with its summary
  and handle; it printed the server's reply as raw JSON, because it expected a
  list of bookmark objects that Gramps Web does not send. It no longer calls
  them user bookmarks: bookmarks belong to the tree and are shared by all its
  users

## [2.2.0] - 2026-10-03

### Added

- Enter dates in every Gramps calendar: date parameters take the calendar at
  the end, as Gramps writes it, and for a year that started on 1 March, 25
  March, or 1 September the new year too (`1856-07-20 (Julian)`, `between
  1850 and 1860 (Julian)`, `1735-03-10 (Julian, Mar25)`, `1735-03-10
  (Mar25)`). Gregorian, Julian, Hebrew, French Republican, Persian, Islamic,
  and Swedish dates keep the day, month, and year as written and get the sort
  value Gramps computes for them, so an Old Style date from a church book
  sorts on the right day. Hebrew, French Republican, Persian, and Islamic
  dates take numeric months; `get_reference(topic: "input-guide", section:
  "dates")` lists the months of each calendar
- Accept the date abbreviations Gramps and GEDCOM write, in any case:
  `bef.`, `aft.`, `abt.`, `c.`, `ca.`, `around`, `est.`, `calc.`, `cal.`, and
  `bet. 1850 and 1860`; they were rejected as unrecognized dates

### Changed

- Reject dates that don't exist in their calendar, as Gramps does, instead of
  storing them: `1900-02-29` and `1856-02-30` in the Gregorian calendar (the
  error suggests `(Julian)` when the date exists there), Adar II in a common
  Hebrew year, and a range or span with a new year that starts on or after
  the new year day, which Gramps can't store
- Reject a range or span that ends before it starts (`between 1860 and 1850`,
  `1860-1850`); it was stored and sorted by the later date. The ends are
  compared to the precision both have, so `between 1850-05 and 1850` still
  works

### Fixed

- Send a sort value for a stored Julian or other non-Gregorian date that an
  update tool writes back unchanged; it was left out. A stored dual-dated year
  (`1735/6`) or a date with a new year such as Mar25 got a sort value computed
  as a Gregorian date, and now gets the one Gramps computes
- Before, after, or about in front of a range, span, or open-ended date was
  dropped silently: `about between 1850 and 1860` was stored as the bare
  range, and `about 1850-` on an event as `after 1850`. It's now a validation
  error that suggests `estimated between 1850 and 1860`
- `get_timeline` `dates` took only the Gramps Web form `1850/1/1-1860/12/31`:
  an ISO date, a year range, a single day, or a Julian date got a 422 from
  Gramps Web for person and family timelines. It now takes dates as the other
  date parameters do (`1850-1860`, `between 1850-03 and 1851`, `from 1850`,
  `before 1900-05-01`, `1856-07-20 (Julian)`): a year or month covers all its
  days, from and to include the date, before and after leave it out, and
  other calendars are converted to Gregorian. Approximate dates are rejected,
  since a filter needs exact bounds
- Say in the `update_event`, `update_media`, and `update_citation` date
  descriptions that an empty string removes the date; `update_event` said it
  "may clear per parser rules", the others didn't say

## [2.1.1] - 2026-10-02

### Fixed

- Show Gramps IDs in `search` results, as the tool description says; Gramps
  Web search hits carry the ID only inside the object, so rows showed only the
  handle
- Show links that an edit adds or removes as `Reference from [handle: …] to
  [handle: …]` in `get_recent_changes` instead of `Added 7 [handle: …]`;
  Gramps Web sends Gramps' reference key as the class name
- Name the root person in the `get_person_tree` header (`Root: Petrov, Ivan,
  b. 1880 [I0012] [handle: …]`) instead of only its handle, and stop
  repeating each row's Gramps ID as `(gramps_id: …)` next to the handle
- Name the others in an event a person takes part in under another role, such
  as the child of a `Birth [Father]` row or the couple of a `Marriage
  [Witness]` row, on a `Participants:` line in `get_timeline` for people and
  in extended `get_object` for people; the rows did not say whose event it was
- Stop offering a thumbnail resource on media cards that cannot have one, such
  as PDFs, and say that `read_media` needs mode `file` for them instead; the
  thumbnail was always rejected
- Leave out empty dates on media and citation cards instead of printing
  `Date:  Unknown date` and `Access Date: Unknown date`
- Name the citations, notes, media, tags, and repositories that event, place,
  source, citation, repository, note, and media cards link to, and the objects
  in their `Referenced by` sections, as in `search` results (`• [General]
  Born at home [handle: …]`) instead of listing bare handles; keep a
  multi-line note on one line in `search` and `list_objects` rows
- Name the families, events, media, notes, citations, tags, and associated
  people on person cards, and the media, notes, citations, and tags on family
  cards, in the same way (`• Birth — 1856-08-01 — Tver [handle: …] role:
  Primary`)
- Head person cards in `get_object` with the name in the tree's display
  format, as lists and `search` show it (`Petrova, Anna Ivanovna`), instead of
  given names followed by surnames
- Show dates on cards as Gramps Web shows them in timelines, `search`, and
  trees (`1856-08-01`, `about 1930-08`) instead of `1 Aug 1856`, with the
  quality and the non-Gregorian calendar that cards left out (`estimated
  1850`, `1856-07-20 (Julian)`), and a dual-dated year as Gramps writes it
  (`1735/6`) instead of one year later; date parameters take the shown dates
  back, including three-digit years, and a date with a calendar suffix fails
  validation instead of being stored as Gregorian
- Keep relatives' events in person timelines without `dates` when the person's
  first event is imprecise: Gramps Web cut the timeline to the person's first
  and last event with Gramps' fuzzy date matching, so an `about 1876` birth
  (read as 1826 to 1926) dropped every relative's event before 1926, the
  person's own marriage too. `get_timeline` now asks Gramps Web not to cut
  person timelines unless `dates` are given, so relatives' events from before
  the person's birth and after their death are listed as well

## [2.1.0] - 2026-10-02

### Added

- Add SixLabors.ImageSharp 3.1.12 to render media thumbnails; it is fully
  managed, so the Docker image and MCP Bundle need no native libraries
- Add `THIRD-PARTY-NOTICES.txt` with the licenses of the bundled NuGet packages
  and the .NET runtime. MCP Bundles now include it, the Docker image includes
  it and `LICENSE`, and a test fails when the restored packages and the list
  differ

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
  originals, using the first page of multi-page files; reject non-image media
  such as PDF and audio, and AVIF, HEIC, HEIF, JPEG XL, and SVG images, before
  downloading the original
- Download originals for thumbnails up to 50 MiB, or `GRAMPS_MEDIA_MAX_BYTES`
  if larger, and up to 100 megapixels; `GRAMPS_MEDIA_MAX_BYTES` still limits
  the preview returned to the client
- Return media files of any MIME type in `read_media` file mode and the file
  resource; images other than JPEG, PNG, GIF, and WebP are returned as an
  embedded blob with a hint, which points to thumbnail mode when the server can
  render the format
- Update the README, security and privacy policies, architecture, developer,
  migration, and tool documentation, the system prompt, Docker Compose, MCPB
  metadata, and GitHub Pages for the new media behavior
- Show the Gramps ID and age of relatives in `get_timeline` rows, and the
  person's own age; show the role only when it is not Primary instead of on
  every row; and list up to 5 people and families of each event on place
  timelines with their roles. A zero age, such as "0 days" on birth rows, is
  left out
- Name the place once above a place timeline, with its Gramps ID, instead of
  on every row
- Show a family's parents and children in `get_object` without `extended` by
  name, Gramps ID, birth, and death, with the marriage, the divorce, and each
  event's type, date, and place, from one request instead of handles
- Show the parents, spouse, and children in each family of an extended person
  `get_object` by name, Gramps ID, birth, and death, with the family's Gramps
  ID, marriage, and divorce, from the person request instead of handles
- Show a place's enclosing places and hierarchy in `get_object` by name, type,
  and Gramps ID from the same request instead of one request per parent
- Show the page count and total in `search` results, and the total when the
  page is past the last one
- Show `get_recent_changes` as one entry per transaction with its ID, UTC time,
  description, user, undo flag, and each changed object's class, change kind,
  and handle, up to 10 objects per transaction, instead of raw JSON
- Show `get_relations` with both people by name, the closest relationship as a
  sentence, generations to the common ancestor, and every relationship with up
  to 10 common ancestors by name and the rest by handle, instead of raw JSON;
  the people and relationships load in parallel and the common ancestors in one
  request
- Load place timeline events, each `get_person_tree` generation, and
  `get_relations` common ancestors in batches of up to 50 objects with the list
  endpoint's `handles` filter (Gramps Web API 3.14+), instead of one request
  per object. Servers that reject or ignore the filter get one request per
  object, up to 4 at a time, and the server answer is remembered per API URL
  and tree
- Read a place once per place timeline instead of twice
- Refetch the events, citations, and media of an extended person or family
  `get_object` in one batch per type, the three in parallel, instead of one
  request per object. With the type cache below, a person with six events at
  places and one citation takes 3 requests after the first call instead of 16
- Build `get_person_tree` rows from the person profile in the batch instead of
  reading each person's birth and death events and places; an ancestor tree
  takes about two requests per generation instead of several per person. Rows
  show the name in the tree's display format, "Surname, Given, b. 1815 in
  London, d. 1852 [I0001]", instead of "Given Surname"
- Label baptism, christening, burial, and cremation dates by event type in
  `get_person_tree` rows, family members, `get_relations`, and people in
  `search` and `list_objects`, e.g. "burial 1950", instead of showing them as
  birth and death
- Build `search` and `list_objects` lines for people, families, events, and
  citations from the `profile=self` summary in the same response instead of
  reading each person's birth event and place, each family's parents, each
  event's place, and each citation's source; a search page takes one request
  plus type labels instead of up to three per hit. Hits without a usable
  embedded object are read in one batch per type
- Show people in `search` and `list_objects` as "Surname, Given, b. 1815 in
  London, d. 1852" in the tree's name display format, with death and baptism
  or burial fallbacks, instead of "Given Surname (b. 1815 in London)"; family
  partners use the display format too. Display names leave out the separator
  of an empty name part, so a person without a surname shows as "Anna"
- Keep type vocabularies between tool calls instead of reading them in every
  call: default types for the life of the process and custom types for 10
  minutes; failed reads are not kept. After the first call, an extended person
  `get_object` makes 8 fewer requests and an event `list_objects` page takes 1
  instead of 2
- Reject a tool call with an argument the tool does not have, such as `extend`
  for `extended` or `object_type` for `objectType`, and suggest the likely
  parameter; such arguments were ignored, so a misspelled option had no effect
- Describe the Gramps ID prefixes in `get_object`, the place lookup by Gramps
  ID, handle, or name in `add_event_to_person`, and the undo marker and
  10-object limit in `get_recent_changes` in the tool descriptions
- Document the `GRAMPS_MEDIA_*` variables in MCP Registry metadata and the
  Dockerfile; list read-only mode, the prompts, and the Linux MCP Bundles on
  GitHub Pages; and correct the tool catalog, system prompt, architecture,
  data model, developer guide, API inventory, and release checklist

### Removed

- Remove `GRAMPS_MEDIA_ALLOWED_MIME_TYPES` and the MCPB "Allowed MIME types"
  setting; access is controlled by `GRAMPS_MEDIA_RESOURCES_ENABLED`,
  `GRAMPS_MEDIA_ALLOW_PRIVATE`, and `GRAMPS_MEDIA_MAX_BYTES`. The server
  ignores the variable and logs a startup warning while it is set

### Fixed

- Say in the `update_source` description that `[]` for `repositoryHandles`
  clears the repository links in replace mode, as it does; the description
  said an empty list was ignored
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
- Leave a missing type, date, or place out of `search` and `list_objects`
  event lines instead of showing dashes
- Sort `get_timeline` events dated before the year 1000 by year; they were
  listed after the undated events. Place timeline events of the same year
  follow their dates instead of the order of the place's backlinks
- Name the `objectType` parameter in the `list_objects` error for an unknown
  type instead of `object_type`
- List the default types in `gramps://types` and `get_reference(topic:
  "types")` when the custom types endpoint fails, instead of failing
- Explain a Gramps ID that was not found instead of suggesting `get_object`
  with the same ID: an ID with another type's prefix names that type and how
  to read it, an unknown prefix gets the right one, and a missing ID points to
  `search` and `list_objects`
- Log in to Gramps Web once instead of twice at startup: the connectivity
  check and `GET /health` take their token from the token cache the tools use.
  The first tool call no longer logs in again and hits the rate limit on
  `/api/token/` (HTTP 429), and health probes no longer log in each time. A
  check whose cached token is rejected replaces it once, as tool calls do
- Accept a custom type added in Gramps after the server read the types: write
  tools read custom types again before rejecting a type, instead of rejecting
  it for up to 10 minutes
- Name a missing required argument, or an argument whose value has the wrong
  type, and list the tool's parameters, instead of the bare "An error occurred
  invoking" error
- Replace an access token that Gramps Web rejects and retry the request once.
  After Gramps Web restarted with a new `SECRET_KEY`, every tool call failed
  with HTTP 422 "Signature verification failed" until the cached token expired
  up to 15 minutes later. The server now refreshes the token, or logs in again
  when the refresh token is rejected too. Requests rejected together replace
  the token once, and Gramps Web validation errors with HTTP 422 are not
  retried

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

[Unreleased]: https://github.com/Scormave/gramps-web-mcp/compare/v2.6.0...HEAD
[2.6.0]: https://github.com/Scormave/gramps-web-mcp/compare/v2.5.2...v2.6.0
[2.5.2]: https://github.com/Scormave/gramps-web-mcp/compare/v2.5.1...v2.5.2
[2.5.1]: https://github.com/Scormave/gramps-web-mcp/compare/v2.5.0...v2.5.1
[2.5.0]: https://github.com/Scormave/gramps-web-mcp/compare/v2.4.0...v2.5.0
[2.4.0]: https://github.com/Scormave/gramps-web-mcp/compare/v2.3.1...v2.4.0
[2.3.1]: https://github.com/Scormave/gramps-web-mcp/compare/v2.3.0...v2.3.1
[2.3.0]: https://github.com/Scormave/gramps-web-mcp/compare/v2.2.0...v2.3.0
[2.2.0]: https://github.com/Scormave/gramps-web-mcp/compare/v2.1.1...v2.2.0
[2.1.1]: https://github.com/Scormave/gramps-web-mcp/compare/v2.1.0...v2.1.1
[2.1.0]: https://github.com/Scormave/gramps-web-mcp/compare/v2.0.1...v2.1.0
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
