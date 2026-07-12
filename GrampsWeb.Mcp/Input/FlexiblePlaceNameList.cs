using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Requests;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Input;

/// <summary>
/// MCP parameter for <c>alt_names</c> on places (<see cref="PlaceNameRequest"/> items).
/// </summary>
[JsonConverter(typeof(FlexiblePlaceNameListJsonConverter))]
public sealed class FlexiblePlaceNameList
{
    public const string DescriptionHint =
        "Alternate place names: array of strings (\"Old Name\" or \"Old Name::de\"), " +
        "simple objects {value, lang?, date?}, or JSON array. " +
        "Dated example: [{value:\"New York\", date:\"1914-08-31-1924-01-26\"}]. " +
        "Place dashes are spans (from…to); open \"1991-\" / \"from 1991\" are From (see get_input_guide).";

    public required PlaceNameRequest[] Items { get; init; }

    public static implicit operator PlaceNameRequest[]?(FlexiblePlaceNameList? value) => value?.Items;
}
