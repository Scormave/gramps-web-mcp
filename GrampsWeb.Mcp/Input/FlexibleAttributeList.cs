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
        "or one string with lines or | between entries. " +
        "A type must be a standard Gramps attribute type or a custom one the tree already uses; anything else is rejected with suggestions. " +
        "Full grammar: gramps://input-guide.";

    /// <summary>Gramps <c>SrcAttribute</c>: source and citation attributes have no citations or notes.</summary>
    public const string SourceAttributesHint = "Each attribute takes only type, value and private.";

    public required GrampsAttribute[] Items { get; init; }

    public static implicit operator GrampsAttribute[]?(FlexibleAttributeList? value) => value?.Items;
}
