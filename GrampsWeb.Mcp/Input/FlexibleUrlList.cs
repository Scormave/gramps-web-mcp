using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Input;

/// <summary>
/// MCP parameter for person URL list. Accepts JSON array of <see cref="GrampsUrl"/> objects,
/// array of strings <c>Type: URL</c> with optional <c> — description</c> or <c> - description</c> after the URL,
/// multiline or <c>|</c>-separated string, or embedded JSON array string.
/// </summary>
[JsonConverter(typeof(FlexibleUrlListJsonConverter))]
public sealed class FlexibleUrlList
{
    public const string DescriptionHint =
        "URLs: JSON array of {type,path,desc}, or strings \"Web Home: https://…\" with optional \" — note\" after URL, " +
        "or lines/| separated. Grammar: get_structured_field_input_guide().";

    public required GrampsUrl[] Items { get; init; }

    public static implicit operator GrampsUrl[]?(FlexibleUrlList? value) => value?.Items;
}
