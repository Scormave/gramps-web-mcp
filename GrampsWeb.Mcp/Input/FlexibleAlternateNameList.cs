using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Input;

/// <summary>
/// MCP parameter for <c>alternate_names</c>. Accepts JSON array of <see cref="GrampsName"/> objects,
/// array of simple strings (same grammar as <see cref="FlexibleGrampsName"/> per entry),
/// one multiline JSON string (one name per line — do not use | between names), or embedded JSON array string.
/// </summary>
[JsonConverter(typeof(FlexibleAlternateNameListJsonConverter))]
public sealed class FlexibleAlternateNameList
{
    public const string DescriptionHint =
        "Alternate names: JSON array of full name objects, or strings per entry (see FlexibleGrampsName / get_structured_field_input_guide). " +
        "Multiple names in one string: separate with newlines only (not | — | is given|surname within one name).";

    public required GrampsName[] Items { get; init; }

    public static implicit operator GrampsName[]?(FlexibleAlternateNameList? value) => value?.Items;
}
