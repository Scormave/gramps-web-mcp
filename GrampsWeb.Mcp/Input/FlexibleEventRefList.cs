using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Requests;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Input;

[JsonConverter(typeof(FlexibleEventRefListJsonConverter))]
public sealed class FlexibleEventRefList
{
    public const string DescriptionHint =
        "Event references (handles, not Gramps IDs): JSON array of {ref, role}, strings \"HANDLE::Role\" (default role: Primary on a person, Family on a family), " +
        "comma/pipe/newline-separated, or a single handle. " +
        "A role must be a standard Gramps role or a custom one the tree already uses; anything else is rejected with suggestions. " +
        "An object may carry \"attributes\" of this one participant's part, such as the age a register gives: " +
        "{\"ref\":\"…\",\"role\":\"Primary\",\"attributes\":[\"Age: 65\"]}, in the forms of the attributes argument; " +
        "their types are checked like attributes. " +
        "JSON objects accept snake_case or camelCase list fields (note_list / noteList).";

    /// <summary>How linkMode treats link attributes, for update tools.</summary>
    public const string UpdateHint =
        "A link given without attributes keeps the stored ones. In replace mode a link's attributes replace its stored ones " +
        "([] removes them; one with the same type and value keeps its citations and notes); in add mode an event already " +
        "linked keeps its link and gains the attributes it lacks.";

    public required EventRefRequest[] Items { get; init; }

    public static implicit operator EventRefRequest[]?(FlexibleEventRefList? value) => value?.Items;
}
