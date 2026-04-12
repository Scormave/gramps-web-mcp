using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Input;

/// <summary>
/// MCP parameter for one Gramps name (primary or single alternate). Accepts a full <see cref="GrampsName"/> JSON object
/// or a string: optional <c>NameType:: given|surname</c> or <c>NameType:: Jane Doe</c>; without <c>::</c>, same rules on the whole string.
/// </summary>
[JsonConverter(typeof(FlexibleGrampsNameJsonConverter))]
public sealed class FlexibleGrampsName
{
    public const string DescriptionHint =
        "Name: full Gramps name JSON (get_name_schema), or a string: optional \"Married Name:: Jane|Smith\" or \"Jane Doe\" (last space splits given/surname; use | to force). " +
        "Grammar: get_structured_field_input_guide().";

    public required GrampsName Name { get; init; }

    public static implicit operator GrampsName?(FlexibleGrampsName? value) => value?.Name;
}
