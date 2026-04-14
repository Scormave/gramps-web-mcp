# Developer Guide

Practical guide for extending the gramps-web-mcp server with new features.

## Quick start

```bash
# Prerequisites: .NET 8 SDK

# Run locally against the Gramps demo server
./run-local-server.sh

# Run tests
dotnet test
```

The server starts on `http://127.0.0.1:8080` (HTTP transport) and connects
to `demo.grampsweb.org`.

## Project conventions

### Naming

- **Tool methods**: PascalCase static methods (e.g. `GetPerson`, `CreateEvent`).
  The MCP SDK derives the wire name from the method name (→ `get_person`,
  `create_event`).
- **Models**: `Gramps` prefix (e.g. `GrampsPerson`, `GrampsEvent`).
- **Request DTOs**: `Create*Request` (used for both create and update).
- **Formatters**: `*Formatter` static classes.
- **Converters**: `*JsonConverter` or `*Codec`.

### Tool method signature

```csharp
[McpServerTool]
[Description("Read-only: ... OR Create/Update/Delete ...")]
public static async Task<string> ToolName(
    [Description("Handle or Gramps ID. " + ToolDescriptionFragments.HandleDiscovery)]
    string handle,
    [Description("...")] int param2 = defaultValue,
    GrampsApiClient client = null!)  // injected by host
{
    try
    {
        // 1. Resolve Gramps ID → handle (if applicable)
        var resolved = await HandleResolver.ResolveToHandleAsync(handle, client);
        // 2. Validate input (use TypeCache.ValidateTypeAsync for type strings)
        // 3. Call client
        // 4. Format and return (use NotFoundHelper for 404s)
    }
    catch (Exception ex)
    {
        throw McpToolErrors.ToMcpException(ex);
    }
}
```

Key rules:
- Tools return `Task<string>` — always formatted text, never raw JSON.
- `GrampsApiClient client` is the **last** parameter, injected by the MCP host.
- Every tool wraps its body in `try/catch` and rethrows via `McpToolErrors`.
- `[Description]` must clearly state read-only vs write and prerequisites.
- Use constants from `ToolDescriptionFragments` for consistent documentation.
- Handle parameters should accept both handles and Gramps IDs — use
  `HandleResolver.ResolveToHandleAsync` to normalize.
- For not-found responses, use `NotFoundHelper.NotFoundMessage` to provide
  contextual hints.

### Error handling

- API errors → `GrampsApiException` → `McpToolErrors.ToMcpException()` → `McpException`
- Input validation → `McpToolErrors.ValidationError(message)` → `McpException`
- Never let raw exceptions escape a tool method.

### Formatting

- Tools return **human-readable text**, not JSON.
- Use dedicated `*Formatter` static classes.
- `GrampsDefaultTypeLabels` resolves wire type keys to display labels.
- `GrampsValueFormatter` handles atomic values (names, dates).
- For dynamic/unknown payloads, use `JsonResponseFormatter.FormatJson()`.

---

## How to add a new tool

### Step 1: Identify the API endpoint

Check the Gramps Web API spec (`apispec.yaml`) and update
`docs/API_INVENTORY.md` with the new endpoint.

### Step 2: Create/update model

If the endpoint returns a new entity type:
1. Add a model class in `Models/` with `[JsonPropertyName]` attributes.
2. If any fields have polymorphic wire shapes, add a custom `JsonConverter`
   in `Serialization/`.
3. Update contract test mapping in `Tests/Contract/swagger-dto-map.json`.

### Step 3: Add API client method (if needed)

Most tools use the generic `client.GetAsync<T>()`, `client.PostMutationAsync<T>()`
etc.  Only add new client methods if you need special request/response handling.

### Step 4: Add formatter

Create a `*Formatter.cs` in `Formatters/` with static methods that take model
objects and return formatted strings.

### Step 5: Implement the tool

1. Add a new `[McpServerTool]` method in the appropriate `*Tools.cs` file,
   or create a new file if it's a new domain (add `[McpServerToolType]` to
   the class).
2. Follow the standard method signature pattern.
3. For write tools, include appropriate `ToolDescriptionFragments` constants
   in the `[Description]`.
4. For write tools, create a request DTO in `Requests/` if needed.
5. For write tools with type parameters, add server-side validation via
   `TypeCache.ValidateTypeAsync(value, "category_name", client)`.
6. For handle parameters, use `HandleResolver.ResolveToHandleAsync` so
   agents can pass either handles or Gramps IDs.
7. For not-found cases, return `NotFoundHelper.NotFoundMessage(type, id)`.

### Step 6: Add tests

- **Unit test** for the formatter in `Tests/UnitTests/`.
- **Fixture file** in `Tests/Fixtures/` with realistic API response JSON.
- **Contract test mapping** if new models were added.

---

## How to add a new Gramps entity type

Full checklist for adding support for a completely new entity type
(e.g., if Gramps Web adds a new object type in the future):

1. **Model** → `Models/Gramps{Entity}.cs`
2. **Extended model** (if `?extend=all` is supported) →
   `Models/Gramps{Entity}Extended.cs`
3. **Request DTO** → `Requests/Create{Entity}Request.cs`
4. **Formatter** → `Formatters/{Entity}Formatter.cs`
5. **Tools** → `Tools/{Entity}Tools.cs` with:
   - `Get{Entity}` (read)
   - `Create{Entity}` (write)
   - `Update{Entity}` (write)
   - `Delete{Entity}` (write)
6. **Search support** → update `SearchFormatter.cs` and
   `SearchTools.ListObjects` if the new type should appear in listings.
7. **Contract mapping** → update `swagger-dto-map.json`
8. **API inventory** → update `docs/API_INVENTORY.md`
9. **Tests** → fixture JSON + unit tests + contract mapping

---

## How to add a Flexible* input type

When you want agents to pass data in free-form text as well as structured JSON:

1. Create `Input/Flexible{Thing}.cs`:
   ```csharp
   [JsonConverter(typeof(Flexible{Thing}JsonConverter))]
   public class Flexible{Thing}
   {
       public const string DescriptionHint = "...";
       internal {TargetType} Value { get; }
       public static implicit operator {TargetType}(Flexible{Thing} f) => f.Value;
   }
   ```
2. Create `Serialization/Flexible{Thing}JsonConverter.cs` that parses
   string, array, or object input into the target type.
3. Use `Flexible{Thing}` as the parameter type in tool methods.
4. Add unit tests in `Tests/UnitTests/Flexible{Thing}Tests.cs`.
5. Update the `get_structured_field_input_guide` output in `TypeTools.cs`.

---

## How to add a composite tool

Composite tools (`CompositeTools.cs`) combine multiple API calls into a single
tool invocation, reducing the number of sequential tool calls an agent must
make.  Examples: `FindByGrampsId`, `QuickAddPerson`, `AddEventToPerson`.

Pattern:
1. Add a new `[McpServerTool]` method in `CompositeTools.cs`.
2. Use `HandleResolver.ResolveToHandleAsync` for any handle parameter.
3. Call the API client directly (not other tool methods) to create/fetch
   sub-objects.
4. Aggregate results into a single formatted response.
5. Track created objects and list them in the output for transparency.

Keep private helpers (place resolution, name conversion) in the same class
to avoid coupling with entity-specific tool files.

---

## How to handle new API wire quirks

The Gramps Web API sometimes changes field shapes between versions.
The approach is:

1. **Never fail on unexpected shapes** — use `UnmappedMemberHandling.Skip`.
2. **Create a custom converter** that handles all known shapes
   (string, object, array) and normalizes to one CLR type.
3. **Add fixture JSON** for each known shape variant.
4. **Add deserialization tests** covering all variants.

Example pattern (string-or-object field):
```csharp
public class MyFieldConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, ...)
    {
        if (reader.TokenType == JsonTokenType.String)
            return reader.GetString();
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            return doc.RootElement.GetProperty("string").GetString();
        }
        reader.Skip();
        return null;
    }
}
```

---

## Testing

### Run all tests

```bash
dotnet test
```

### Run contract tests only

```bash
dotnet test --filter "Category=Contract"
```

### Contract tests explained

Contract tests ensure C# DTOs stay in sync with the OpenAPI spec:

1. `apispec.yaml` (repo root) is the Gramps Web OpenAPI spec.
2. `swagger-dto-map.json` maps API schema definitions → C# model types
   and their `[JsonPropertyName]` fields.
3. `DtoSwaggerSyncTests` loads both and verifies every mapped property
   exists with the correct JSON name.

**When to update `swagger-dto-map.json`:**
- Adding a new model class
- Adding new `[JsonPropertyName]` properties to an existing model
- Changing JSON property names

### Fixture files

JSON files in `Tests/Fixtures/` represent real API responses. They serve as
regression tests for deserialization. When the API changes shape, update or
add fixtures to document the new format.

---

## Update semantics for write tools

### Omit vs empty

This is a critical distinction for update tools:

| What agent does | Effect |
|----------------|--------|
| **Omits parameter** | Field is unchanged (keeps current value) |
| **Passes `[]` (empty array)** | **Clears** all items of that kind |
| **Passes `null`** | Same as omit (ignored) |

This is documented in tool descriptions via `ToolDescriptionFragments.OmitToKeepEmptyClears`
and `UpdateEmptyListRemovesLinks`.

### Read-modify-write pattern

Update tools follow this pattern:
1. `GET` the current entity
2. Build a request DTO merging current values with provided changes
3. `PUT` the merged request

This means the tool must preserve all fields the agent didn't explicitly change.

---

## Date handling

### Agent → Gramps

`AgentDateParser` converts free-text dates into `DateRequest` objects:
- ISO dates: `2024-03-15`
- Slash dates: `15/03/2024` (order controlled by `DateComponentOrder`)
- Modifiers: `about 1950`, `before 1900/01/01`, `after 2000`
- Ranges: `between 1900 and 1950`, `from 1900 to 1950`
- Spans: `from 1900/01/01 to 1950/12/31`
- Text dates: anything unparseable → stored as text-only date

`DateComponentOrder` enum controls ambiguous date parsing:
- `Iso` — YYYY/MM/DD (default)
- `DayMonthYear` — DD/MM/YYYY
- `MonthDayYear` — MM/DD/YYYY

### Gramps → Wire

`DateRequestJsonConverter` serializes `DateRequest` into Gramps wire format
(`dateval`, `datespan`, `daterange` arrays).

`GrampsDateJsonConverter` + `GrampsDateWireCodec` deserialize wire dates
into `GrampsDate` model.

### Sort values

`GrampsDateSortVal` computes integer sort keys from date components,
used for chronological ordering in timelines.

---

## Extended entity enrichment

When `?extend=all` is used, the API fills first-level linked objects but
does **not** deeply resolve nested references (e.g., citation → source,
event → place).

`ExtendedEntityEnrichment` refetches these:
- Citations: refetch with `?extend=all` to get source info
- Events: refetch with `?extend=place` to get place details
- Media: fetch by handle when `extended.media` is empty but `media_list` has handles

This is applied automatically in `GetPersonExtended` and `GetFamilyExtended`.

---

## Deployment notes

### Docker

The `Dockerfile` uses multi-stage build:
1. **Build stage**: SDK 8.0, restore + publish
2. **Runtime stage**: ASP.NET 8.0, runs as non-root user

Default environment in the image:
- `MCP_TRANSPORT=http`
- `ASPNETCORE_URLS=http://0.0.0.0:8080`
- `MCP_PATH=/mcp`

### CI

Gitea Actions workflow in `.gitea/workflows/docker.yml` builds and publishes
the Docker image on push.

### Local development

`run-local-server.sh` sets up environment variables for the Gramps demo
server and runs the project via `dotnet run`.

For stdio transport (e.g., Claude Desktop):
```bash
export MCP_TRANSPORT=stdio
dotnet run --project GrampsWeb.Mcp/GrampsWeb.Mcp.csproj
```

---

## File map: where to find things

| I need to... | Look in... |
|--------------|-----------|
| Add/modify a tool | `Tools/{Entity}Tools.cs` |
| Add a multi-step convenience tool | `Tools/CompositeTools.cs` |
| Resolve Gramps ID → handle | `Client/HandleResolver.cs` |
| Validate type strings server-side | `Client/TypeCache.cs` |
| Return helpful not-found messages | `Tools/NotFoundHelper.cs` |
| Add structured metadata to responses | `Formatters/ResponseEnvelope.cs` |
| Add a model for an API response | `Models/Gramps{Entity}.cs` |
| Handle a new wire format quirk | `Serialization/` (new converter) |
| Format tool output | `Formatters/{Entity}Formatter.cs` |
| Add agent-friendly input parsing | `Input/Flexible{Thing}.cs` + `Serialization/Flexible{Thing}JsonConverter.cs` |
| Build request DTOs for POST/PUT | `Requests/Create{Entity}Request.cs` |
| Add shared tool description text | `Tools/ToolDescriptionFragments.cs` |
| Map models to request DTOs | `Requests/GrampsRequestMapping.cs` |
| Configure API connection | `Config/GrampsConfig.cs` (env vars) |
| Configure transport | `Config/McpTransportConfig.cs` (env vars) |
| Add a test fixture | `Tests/Fixtures/{name}.json` |
| Update contract mapping | `Tests/Contract/swagger-dto-map.json` |
| Parse dates from agent input | `Dates/AgentDateParser.cs` |
| Parse gender/confidence enums | `Tools/Parsing/` |
| Look up default type labels | `Formatters/GrampsDefaultTypeLabels.cs` |
| Handle extended entity enrichment | `Client/ExtendedEntityEnrichment.cs` |
