using System.Text.Json;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Names records from their stored data, such as the old record of a transaction's deletion, which has no
/// record left to read.
/// </summary>
internal static class StoredRecordLabels
{
    /// <summary>
    /// The search summary line of the stored <paramref name="data"/> of a record of
    /// <paramref name="collection"/>; null when it has none, such as for a family.
    /// </summary>
    public static string? Label(string collection, JsonElement data, GrampsTypeLabelTables tables) =>
        ReadModel(collection, data) is { } model && SearchFormatter.FormatSummary(model, tables)?.Trim() is
            { Length: > 0 } summary
            ? summary
            : null;

    /// <summary>
    /// The stored record as the model its summary line needs. A family is left out: its summary names the
    /// partners from data the stored record does not carry.
    /// </summary>
    public static object? ReadModel(string collection, JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
            return null;
        try
        {
            return collection switch
            {
                "people" => data.Deserialize<GrampsPerson>(GrampsJson.Options),
                "events" => data.Deserialize<GrampsEvent>(GrampsJson.Options),
                "places" => data.Deserialize<GrampsPlace>(GrampsJson.Options),
                "sources" => data.Deserialize<GrampsSource>(GrampsJson.Options),
                "citations" => data.Deserialize<GrampsCitation>(GrampsJson.Options),
                "notes" => data.Deserialize<GrampsNote>(GrampsJson.Options),
                "media" => data.Deserialize<GrampsMedia>(GrampsJson.Options),
                "repositories" => data.Deserialize<GrampsRepository>(GrampsJson.Options),
                "tags" => data.Deserialize<GrampsTag>(GrampsJson.Options),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null; // The record keeps its bare handle.
        }
    }
}
