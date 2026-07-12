using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Requests;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Input;

/// <summary>
/// MCP parameter for <c>placeref_list</c> (parent place enclosure refs).
/// Accepts handle strings, objects with optional dates, or shorthand <c>HANDLE::date</c>.
/// </summary>
[JsonConverter(typeof(FlexiblePlaceRefListJsonConverter))]
public sealed class FlexiblePlaceRefList
{
    public const string DescriptionHint =
        "Parent place refs via enclosedBy (not enclosedByHandles): handle string, array of handles, " +
        "JSON array of {ref, date?}, or shorthand \"HANDLE::1920-1950\" (date optional). " +
        "Examples: [{ref:\"…\", date:\"1708-1927\"}], \"HANDLE::1991-\", date \"1914-08-31-1924-01-26\". " +
        "Smaller region → larger region order. Dates use ISO/year syntax (see get_input_guide).";

    public required PlaceRefRequest[] Items { get; init; }

    public static implicit operator PlaceRefRequest[]?(FlexiblePlaceRefList? value) => value?.Items;
}
