using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Input;

/// <summary>
/// MCP parameter for source repository refs (<c>reporef_list</c>).
/// Accepts JSON array of GrampsRepositoryRef objects, array of strings, one multiline / |-separated string,
/// or a JSON-array string.
/// String item grammar: <c>Ref : CallNumber : MediaType</c> (CallNumber/MediaType optional).
/// </summary>
[JsonConverter(typeof(FlexibleRepositoryRefListJsonConverter))]
public sealed class FlexibleRepositoryRefList
{
    public const string DescriptionHint =
        "Repository refs: JSON array of GrampsRepositoryRef objects, or strings \"Ref : CallNumber : MediaType\". " +
        "Parts after Ref are optional: \"Ref : CallNumber\" and \"Ref :: MediaType\" are valid. " +
        "Multiple refs: JSON array or one multiline / |-separated string.";

    public required GrampsRepositoryRef[] Items { get; init; }

    public static implicit operator GrampsRepositoryRef[]?(FlexibleRepositoryRefList? value) => value?.Items;
}
