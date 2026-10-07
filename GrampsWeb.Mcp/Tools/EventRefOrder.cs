using System.Text.Json.Nodes;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Dates;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// Puts the event references of a person or family in date order (the <c>sortEvents</c> argument of the update tools).
/// The stored reference entries are only moved, so their roles, notes, citations and privacy stay.
/// </summary>
internal static class EventRefOrder
{
    public const string Description =
        "true sorts all event links by event date after the other changes, keeping each link's role and metadata; " +
        "alone it sorts the stored list. Undated Birth, Baptism and Christening go first and other undated events last, " +
        "ending with Death, then Burial and Cremation. On the same date: \"before\" dates, then Residence and Occupation, " +
        "Birth, Baptism, other events, Death, Burial, then \"after\" dates. Default false keeps the order " +
        "(linkMode add appends at the end).";

    private const int ModBefore = 1;
    private const int ModAfter = 2;
    private const int BirthRank = 1;
    private const int BaptismRank = 2;

    /// <summary>Sorts <c>event_ref_list</c> of <paramref name="root"/> in place; one batch read of its events.</summary>
    public static async Task SortAsync(GrampsApiClient client, JsonObject root)
    {
        if (root["event_ref_list"] is not JsonArray list || list.Count < 2)
            return;

        var refs = list.ToArray();
        var handles = refs.Select(RefHandle).ToArray();
        var events = await client.GetByHandlesAsync<GrampsEvent>("events", handles, e => e.Handle);
        var keys = handles
            .Select(h => SortKey(h is not null && events.TryGetValue(h, out var evt) ? evt : null))
            .ToArray();

        // OrderBy is stable: links with equal keys keep their stored order.
        var order = Enumerable.Range(0, refs.Length).OrderBy(i => keys[i]).ToArray();
        root["event_ref_list"] = new JsonArray(order.Select(i => refs[i]?.DeepClone()).ToArray());
    }

    /// <summary>
    /// (position, sort value, modifier rank, type rank): position 0 for undated birth events, 1 for dated events and
    /// 2 for other undated ones; a missing event counts as an undated other event.
    /// </summary>
    internal static (int Position, int SortVal, int Modifier, int Type) SortKey(GrampsEvent? evt)
    {
        var type = TypeRank(evt?.Type);
        if (GrampsDateSortVal.TryGetDatedSortKey(evt?.Date) is not { } sortVal)
            return (type is BirthRank or BaptismRank ? 0 : 2, 0, 0, type);

        var modifier = evt!.Date!.Modifier switch
        {
            ModBefore => 0,
            ModAfter => 2,
            _ => 1
        };
        return (1, sortVal, modifier, type);
    }

    // A residence or occupation stated in a record describes the time of the record's event, so it goes first.
    private static int TypeRank(string? type) => type?.Trim().ToLowerInvariant() switch
    {
        "residence" or "occupation" => 0,
        "birth" => BirthRank,
        "baptism" or "christening" => BaptismRank,
        "death" => 4,
        "burial" or "cremation" => 5,
        _ => 3
    };

    private static string? RefHandle(JsonNode? node) =>
        GrampsObjectPatch.StringValue(node is JsonObject o ? o["ref"] : node)?.Trim();
}
