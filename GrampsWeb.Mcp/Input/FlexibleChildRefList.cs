using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Input;

[JsonConverter(typeof(FlexibleChildRefListJsonConverter))]
public sealed class FlexibleChildRefList
{
    public const string DescriptionHint =
        "Child references (handles or Gramps IDs): JSON array of {ref, frel, mrel, citation_list, note_list}, strings " +
        "\"HANDLE::RelType\" (sets both frel and mrel; default: Birth), comma/pipe/newline-separated, or a single handle or ID. " +
        "A relation must be a standard Gramps one (Birth, Adopted, Stepchild, Sponsored, Foster, Unknown, None) " +
        "or a custom one the tree already uses. citation_list and note_list (handles or Gramps IDs) cite and explain " +
        "the child's link to this family, such as an adoption record. " +
        "JSON objects accept snake_case (frel, mrel, citation_list, note_list, tag_list) or camelCase (fatherRel, " +
        "citationList, tagList, etc.).";

    public required GrampsChildRef[] Items { get; init; }

    public static implicit operator GrampsChildRef[]?(FlexibleChildRefList? value) => value?.Items;
}
