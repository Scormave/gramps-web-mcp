using System.Text.Json;
using System.Text.Json.Nodes;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Tools;

namespace GrampsWeb.Mcp.Tests.UnitTests;

/// <summary>
/// Calls create_records with its arguments written as JSON, the way a client sends them, and answers its
/// POST <c>/api/objects/</c> as Gramps Web does.
/// </summary>
internal static class CreateRecordsCall
{
    private static readonly Dictionary<string, string> Prefixes = new()
    {
        ["Person"] = "I", ["Family"] = "F", ["Event"] = "E", ["Place"] = "P", ["Source"] = "S",
        ["Citation"] = "C", ["Repository"] = "R", ["Note"] = "N"
    };

    /// <summary>Runs create_records with the record lists of <paramref name="json"/>, such as <c>{ "people": [...] }</c>.</summary>
    public static Task<string> RunAsync(string json, GrampsApiClient client)
    {
        var arguments = JsonNode.Parse(json)!.AsObject();
        T[]? Records<T>(string name) => arguments[name]?.Deserialize<T[]>();
        return RecordBatchTools.CreateRecords(
            Records<TagRecord>("tags"), Records<RepositoryRecord>("repositories"), Records<PlaceRecord>("places"),
            Records<SourceRecord>("sources"), Records<NoteRecord>("notes"), Records<CitationRecord>("citations"),
            Records<EventRecord>("events"), Records<PersonRecord>("people"), Records<FamilyRecord>("families"), client);
    }

    /// <summary>
    /// The answer of Gramps Web to a POST <c>/api/objects/</c> body: an "add" change per object, with the next
    /// Gramps ID of its type.
    /// </summary>
    public static JsonArray Changes(JsonNode body) => new(body.AsArray().Select((o, i) =>
    {
        var grampsClass = o!["_class"]!.GetValue<string>();
        var handle = o["handle"]!.GetValue<string>();
        var stored = new JsonObject { ["_class"] = grampsClass, ["handle"] = handle };
        if (grampsClass != "Tag") // tags have no Gramps ID
            stored["gramps_id"] = $"{Prefixes[grampsClass]}{i + 1:D4}";
        return (JsonNode)new JsonObject
        {
            ["_class"] = grampsClass, ["handle"] = handle, ["type"] = "add", ["old"] = null, ["new"] = stored
        };
    }).ToArray());

    /// <summary>The first object of <paramref name="grampsClass"/> in a POST <c>/api/objects/</c> body.</summary>
    public static JsonNode Saved(JsonNode? body, string grampsClass) =>
        body!.AsArray().First(o => o!["_class"]!.GetValue<string>() == grampsClass)!;
}
