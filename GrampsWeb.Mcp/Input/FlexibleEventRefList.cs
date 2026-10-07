using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Requests;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Input;

[JsonConverter(typeof(FlexibleEventRefListJsonConverter))]
public sealed class FlexibleEventRefList
{
    public const string DescriptionHint =
        "Event references (handles, not Gramps IDs): JSON array of {ref, role}, strings \"HANDLE::Role\" (default role: Primary), " +
        "comma/pipe/newline-separated, or a single handle. " +
        "A role must be a standard Gramps role or a custom one the tree already uses; anything else is rejected with suggestions. " +
        "JSON objects accept snake_case or camelCase list fields (note_list / noteList).";

    public required EventRefRequest[] Items { get; init; }

    public static implicit operator EventRefRequest[]?(FlexibleEventRefList? value) => value?.Items;
}
