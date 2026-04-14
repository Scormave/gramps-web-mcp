using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Requests;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Input;

[JsonConverter(typeof(FlexibleEventRefListJsonConverter))]
public sealed class FlexibleEventRefList
{
    public const string DescriptionHint =
        "Event references: JSON array of {ref, role}, strings \"HANDLE::Role\" (default role: Primary), " +
        "comma/pipe/newline-separated, or a single handle.";

    public required EventRefRequest[] Items { get; init; }

    public static implicit operator EventRefRequest[]?(FlexibleEventRefList? value) => value?.Items;
}
