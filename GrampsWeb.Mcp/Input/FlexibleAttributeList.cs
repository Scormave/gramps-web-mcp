using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Input;

/// <summary>
/// MCP parameter for attribute lists. Accepts JSON array of <see cref="GrampsAttribute"/> objects,
/// array of strings <c>Type: Value</c> (first colon separates type and value), a multiline or <c>|</c>-separated string,
/// or a string containing a JSON array.
/// </summary>
[JsonConverter(typeof(FlexibleAttributeListJsonConverter))]
public sealed class FlexibleAttributeList
{
    public const string DescriptionHint =
        "Attributes: JSON array of objects {type,value,...}, or strings \"Type: Value\" (first colon), " +
        "or one string with lines or | between entries. Full grammar: get_structured_field_input_guide().";

    public required GrampsAttribute[] Items { get; init; }

    public static implicit operator GrampsAttribute[]?(FlexibleAttributeList? value) => value?.Items;
}
