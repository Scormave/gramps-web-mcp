using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Input;

/// <summary>
/// MCP parameter for person association list (<c>person_ref_list</c>). Accepts JSON array of objects
/// with <c>ref</c> and <c>rel</c>, array of strings <c>handle:: relationship text</c> (double colon after handle),
/// multiline or <c>|</c>-separated string, or embedded JSON array string.
/// </summary>
[JsonConverter(typeof(FlexiblePersonRefListJsonConverter))]
public sealed class FlexiblePersonRefList
{
    public const string DescriptionHint =
        "Person associations: JSON array of {ref, rel, ...}, or strings \"PERSON_HANDLE:: Godfather\" (double colon after handle). " +
        "Grammar: gramps://input-guide.";

    public required GrampsPersonRef[] Items { get; init; }

    public static implicit operator GrampsPersonRef[]?(FlexiblePersonRefList? value) => value?.Items;
}
