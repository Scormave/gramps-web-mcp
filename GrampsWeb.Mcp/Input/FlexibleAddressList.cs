using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Input;

/// <summary>
/// MCP parameter for person address list. Accepts JSON array of <see cref="GrampsAddress"/> objects,
/// array of strings (each string is one address block), a multiline string with blocks separated by blank lines or <c>---</c>,
/// keyed lines (<c>street:</c>, <c>city:</c>, …), or a single line without <c>key:</c> as street-only.
/// </summary>
[JsonConverter(typeof(FlexibleAddressListJsonConverter))]
public sealed class FlexibleAddressList
{
    public const string DescriptionHint =
        "Addresses: JSON array of Gramps address objects; or text blocks with lines like street:, city:, … (blank line or --- between addresses); " +
        "or one plain line = street only. Grammar: get_structured_field_input_guide().";

    public required GrampsAddress[] Items { get; init; }

    public static implicit operator GrampsAddress[]?(FlexibleAddressList? value) => value?.Items;
}
