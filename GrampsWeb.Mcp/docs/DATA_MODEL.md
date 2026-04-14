# Data Model & Serialization

## Gramps entity graph

Gramps Web organises genealogical data as a graph of interconnected entities.
All entities use opaque **handles** as primary identifiers (not the human-visible
Gramps ID like `I0001`).

```
Person ─┬─ Name (primary + alternates)
        │    └── Surname[]
        ├─ EventRef[] ──► Event ──► Place
        ├─ FamilyRef[] ──► Family ──┬─► Person (father)
        │                           ├─► Person (mother)
        │                           └─► ChildRef[] ──► Person
        ├─ Citation[] ──► Source ──► RepositoryRef[] ──► Repository
        ├─ Note[]
        ├─ Media[]
        ├─ Tag[]
        ├─ Address[]
        ├─ Attribute[]
        ├─ URL[]
        └─ PersonRef[] (associations)
```

## Model classes (`Models/`)

All models use `[JsonPropertyName("snake_case")]` to match the Gramps Web API.
Many array properties use custom `JsonConverter` attributes to handle
polymorphic wire shapes.

### Core entities

| Model | File | Key properties |
|-------|------|----------------|
| `GrampsPerson` | `GrampsPerson.cs` | `Handle`, `GrampsId`, `Gender` (int), `PrimaryName` → `GrampsName`, `AlternateNames` → `GrampsName[]`, `EventRefList` → `GrampsEventRef[]`, `FamilyList`, `ParentFamilyList`, `MediaList`, `NoteList`, `CitationList`, `TagList`, `AddressList`, `AttributeList`, `UrlList`, `PersonRefList`, `BirthRefIndex`, `DeathRefIndex`, `Change`, `Private` |
| `GrampsFamily` | `GrampsFamily.cs` | `Handle`, `GrampsId`, `FatherHandle`, `MotherHandle`, `ChildRefList` → `GrampsChildRef[]`, `EventRefList`, `MediaList`, `NoteList`, `CitationList`, `TagList`, `AttributeList`, `Relationship` (type), `Change`, `Private` |
| `GrampsEvent` | `GrampsEvent.cs` | `Handle`, `GrampsId`, `Type`, `Date` → `GrampsDate`, `Place` (handle), `Description`, `MediaList`, `NoteList`, `CitationList`, `TagList`, `Change`, `Private` |
| `GrampsPlace` | `GrampsPlace.cs` | `Handle`, `GrampsId`, `Name`, `Type` (`place_type`), `Code`, `Longitude`, `Latitude`, `PlaceRefList`, `AlternateLocations`, `MediaList`, `NoteList`, `CitationList`, `TagList`, `Change`, `Private` |
| `GrampsSource` | `GrampsSource.cs` | `Handle`, `GrampsId`, `Title`, `Author`, `PubInfo`, `Abbrev`, `RepositoryRefList`, `MediaList`, `NoteList`, `AttributeList`, `TagList`, `Change`, `Private` |
| `GrampsCitation` | `GrampsCitation.cs` | `Handle`, `GrampsId`, `Source` (`source_handle`), `Page`, `Confidence`, `Date`, `Text`, `MediaList`, `NoteList`, `AttributeList`, `TagList`, `Change`, `Private` |
| `GrampsNote` | `GrampsNote.cs` | `Handle`, `GrampsId`, `Text`, `Type`, `Format`, `TagList`, `Change`, `Private` |
| `GrampsMedia` | `GrampsMedia.cs` | `Handle`, `GrampsId`, `Path`, `Mime`, `Description`, `Date`, `AttributeList`, `CitationList`, `NoteList`, `TagList`, `Change`, `Private` |
| `GrampsRepository` | `GrampsRepository.cs` | `Handle`, `GrampsId`, `Name`, `Type`, `EmailList`, `AddressList`, `UrlList`, `NoteList`, `TagList`, `Change`, `Private` |
| `GrampsTag` | `GrampsTag.cs` | `Handle`, `GrampsId`, `Name`, `Color`, `Priority`, `Change` |

### Sub-entities (embedded)

| Model | File | Notes |
|-------|------|-------|
| `GrampsName` | `GrampsName.cs` | Person names; `SurnameList` → `GrampsSurname[]`, `Type` via wire-type converter, `Date` via date converter |
| `GrampsSurname` | `GrampsSurname.cs` | `Surname`, `Prefix`, `Connector`, `OriginType`, `Primary` |
| `GrampsEventRef` | in `GrampsPerson.cs` | `Ref` (handle), `Role` (wire-type), `NoteList`, `AttributeList` |
| `GrampsChildRef` | in `GrampsFamily.cs` | `Ref`, `FatherRelType` (frel), `MotherRelType` (mrel); whole-type converter |
| `GrampsFamilyRef` | in `GrampsPerson.cs` | `Ref`, `Relationship`, `FatherRelationship`, `MotherRelationship`; whole-type converter |
| `GrampsRepositoryRef` | `GrampsRepositoryRef.cs` | `Ref`, `CallNumber`, `MediaType`, `NoteList`; whole-type converter |
| `GrampsPersonRef` | `GrampsPersonRef.cs` | `Ref`, `Relationship`, `CitationList`, `NoteList`, `Private` |
| `GrampsAttribute` | in `GrampsPerson.cs` | `Type`, `Value`, `CitationList`, `NoteList`, `Private` |
| `GrampsAddress` | `GrampsAddress.cs` | `Street`, `Locality`, `City`, `County`, `State`, `Country`, `Postal`, `Phone`, `Date`, `CitationList`, `NoteList`, `Private` |
| `GrampsUrl` | `GrampsUrl.cs` | `Type`, `Path`, `Description`, `Private` |
| `GrampsDate` | `GrampsDate.cs` | Complex date (type-level `GrampsDateJsonConverter`); fields for calendar, modifier, quality, text, day/month/year, end segment, sortval |
| `GrampsWireTypeObject` | `GrampsWireTypeObject.cs` | `Class`, `String`, `Value` — represents `{ _class, string }` API shapes |

### Extended models (with resolved sub-entities)

| Model | File | Description |
|-------|------|-------------|
| `GrampsPersonExtended` | `GrampsPersonExtended.cs` | Person + `Extended` → events, families, parent_families, notes, tags, media, citations as resolved objects |
| `GrampsFamilyExtended` | in `GrampsFamily.cs` | Family + `Extended` → events, father, mother, children, notes, tags, media, citations |
| `GrampsEventExtended` | `GrampsEventExtended.cs` | Event + `Extended` → place, citations, media, notes, tags |
| `GrampsCitationExtended` | `GrampsCitationExtended.cs` | Citation + `Extended` → source, media, notes, tags |

### Search & timeline

| Model | File | Description |
|-------|------|-------------|
| `GrampsSearchHit` | `GrampsSearchHit.cs` | `Handle`, `ObjectType`, `GrampsId`, `Rank`, `Score`, `Object` (raw `JsonElement?`) |
| `GrampsPagedResult<T>` | `GrampsPagedResult.cs` | `Objects`, `Total`, `Page` — generic wrapper for paged lists |
| `GrampsTimelineEntry` | `GrampsTimeline.cs` | `Handle`, `GrampsId`, `Label`, `Type`, `Date`, `Place`, `Description`, `Role`, `Name`, `Category`, `Rating` |
| `PersonTreeRow` | `PersonTreeRow.cs` | `Person`, `Generation`, `AncestorPathFromRoot` — app-side model for tree traversal |

---

## Serialization layer (`Serialization/`)

### Shared options: `GrampsJson`

`GrampsJson.Options` is the single `JsonSerializerOptions` instance used for all
API communication:

- `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`
- `PropertyNameCaseInsensitive = true`
- `UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip`
- `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull`
- Empty collection omission modifier (non-string `IEnumerable`)

### Wire-format converters (polymorphic field handling)

The Gramps Web API has many fields that can be either a simple value or a
structured object.  Custom converters normalize these:

| Converter | Problem solved | Normalization |
|-----------|---------------|---------------|
| `GrampsWireTypeStringConverter` | Type fields: `"Birth"` **or** `{ "_class": "EventType", "string": "Birth" }` | → `string?` |
| `GrampsPlaceNameStringConverter` | Place name: `"Paris"` **or** `{ "value": "Paris", "lang": "en" }` | → `string?` |
| `GrampsNoteTextStringConverter` | Note text: `"Hello"` **or** `{ "_class": "StyledText", "string": "Hello", "tags": [...] }` | → `string?` |
| `GrampsHandleStringArrayConverter` | Handle lists: `["h1","h2"]` **or** `[{"ref":"h1"}, "h2"]` **or** `[{"handle":"h1"}]` | → `string[]` |
| `HandleElementReader` | Single handle element: `"h1"` **or** `{"ref":"h1"}` **or** `{"handle":"h1"}` | → `string?` |
| `GrampsChildRefJsonConverter` | Child ref: full object **or** plain handle string | → `GrampsChildRef` |
| `GrampsFamilyRefJsonConverter` | Family ref: full object **or** plain handle string | → `GrampsFamilyRef` |
| `GrampsRepositoryRefJsonConverter` | Repo ref: full object **or** plain handle string | → `GrampsRepositoryRef` |
| `GrampsDateJsonConverter` | Delegates to `GrampsDateWireCodec` | → `GrampsDate` |

### Date wire codec: `GrampsDateWireCodec`

Gramps dates on the wire use `dateval` / `datespan` / `daterange` arrays
with calendar, modifier, quality as numeric codes.  `GrampsDateWireCodec`
reads/writes this format and maps to the flat `GrampsDate` model.

### Response shape adapters

| Class | Problem solved |
|-------|---------------|
| `GrampsPagedResultParser` | List endpoints return either a bare `[]` array or `{ "objects": [...], "total": N, "page": N }` |
| `GrampsMutationParser` | POST/PUT responses return either a bare entity or a change array `[{ "_class": "...", "type": "...", "old": {...}, "new": {...} }]` |
| `TypesPayloadParser` | `/api/types/` returns per-category data as string arrays, string→string maps, or single strings |

### Date request serialization: `DateRequestJsonConverter`

Converts `DateRequest` DTOs into the Gramps wire date format for POST/PUT
requests (`dateval` / `datespan` / `daterange`).

---

## Flexible input types (`Input/`)

These wrapper types allow AI agents to pass arguments in multiple convenient
formats.  Each has a `[JsonConverter]` and an `implicit operator` to the
canonical model type.

| Type | Target type | Agent-friendly formats |
|------|-------------|----------------------|
| `FlexibleHandleList` | `string[]` | JSON array; single string; comma/semicolon/pipe/newline-separated; `{ref}`/`{handle}` objects |
| `FlexibleGrampsName` | `GrampsName` | Full JSON; shorthand `"Given Surname"` or `"Given :: Surname"` with `\|`-separated fields |
| `FlexibleAlternateNameList` | `GrampsName[]` | Array of names (objects or strings); newline-separated strings |
| `FlexibleAttributeList` | `GrampsAttribute[]` | Object array; `"Type: Value"` strings; multiline/pipe-separated |
| `FlexibleAddressList` | `GrampsAddress[]` | Object array; keyed lines (`street:`, `city:`, …); blocks separated by blank line/`---`; single line → street |
| `FlexibleUrlList` | `GrampsUrl[]` | Object array; `"Type: URL"` strings with optional description |
| `FlexiblePersonRefList` | `GrampsPersonRef[]` | Object array; `"HANDLE:: rel"` strings; multiline/pipe-separated |

### Parsing logic

Converter implementations live in `Serialization/Flexible*JsonConverter.cs`.
Name parsing has additional logic in `FlexibleGrampsNameParsing.cs`.

---

## Request DTOs (`Requests/`)

Create/update request DTOs sent to the Gramps Web API via POST/PUT.

| DTO | Target API endpoint |
|-----|-------------------|
| `CreatePersonRequest` | `POST /api/people/` / `PUT /api/people/{handle}` |
| `CreateFamilyRequest` | `POST /api/families/` / `PUT /api/families/{handle}` |
| `CreateEventRequest` | `POST /api/events/` / `PUT /api/events/{handle}` |
| `CreatePlaceRequest` | `POST /api/places/` / `PUT /api/places/{handle}` |
| `CreateSourceRequest` | `POST /api/sources/` / `PUT /api/sources/{handle}` |
| `CreateCitationRequest` | `POST /api/citations/` / `PUT /api/citations/{handle}` |
| `CreateNoteRequest` | `POST /api/notes/` / `PUT /api/notes/{handle}` |
| `CreateMediaRequest` | `PUT /api/media/{handle}` (no create via MCP) |
| `CreateRepositoryRequest` | `POST /api/repositories/` / `PUT /api/repositories/{handle}` |
| `CreateTagRequest` | `POST /api/tags/` |

Common pattern:
- `_class` field matches Gramps class name
- `handle`, `gramps_id`, `change` — optional (API generates on create)
- `[JsonPropertyName("snake_case")]` for all fields
- Nested request types: `GrampsNameRequest`, `SurnameRequest`,
  `EventRefRequest`, `FamilyRefRequest`, `AttributeRequest`,
  `DateRequest`, `PlaceNameRequest`, `StyledTextRequest`

### Mapping: `GrampsRequestMapping`

`GrampsRequestMapping` converts GET-response models into request DTOs for
updates (read-modify-write pattern):
- `GrampsDate` → `DateRequest` (via `ToDateRequestOrNull`)
- `GrampsAttribute[]` → `AttributeRequest[]`
- Event ref lists, family ref lists → request equivalents
- Parallel handle/role arrays → `EventRefRequest[]` (`BuildEventRefList`)

---

## High-risk polymorphic fields

These API fields have inconsistent shapes between different Gramps Web versions
or endpoints.  The serialization layer handles them, but be aware when adding
new features:

| Field | Possible shapes |
|-------|----------------|
| `parent_family_list`, `family_list` | Handle strings **or** `{ref}` objects |
| `media_list` | Handle strings **or** `{ref}` / `{handle}` objects |
| `child_ref_list` | Full child-ref objects **or** plain handle strings |
| `reporef_list` | Full repo-ref objects **or** plain handle strings |
| Type/role fields | Plain strings **or** `{ _class, string, value }` |
| Note `text` | String **or** `{ _class: "StyledText", string, tags }` |
| Place `name` | String **or** `{ value, lang }` |
| Search root | Bare array **or** wrapped object |
| List endpoints | Bare array **or** `{ objects, total, page }` |
| POST/PUT response | Bare entity **or** change array `[{ old, new }]` |
