using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Input;

[JsonConverter(typeof(FlexibleChildRefListJsonConverter))]
public sealed class FlexibleChildRefList
{
    public const string DescriptionHint =
        "Child references: JSON array of {ref, frel, mrel}, strings \"HANDLE::RelType\" " +
        "(sets both frel and mrel; default: Birth), comma/pipe/newline-separated, or a single handle.";

    public required GrampsChildRef[] Items { get; init; }

    public static implicit operator GrampsChildRef[]?(FlexibleChildRefList? value) => value?.Items;
}
