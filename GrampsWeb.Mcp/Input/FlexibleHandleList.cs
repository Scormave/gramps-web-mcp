using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Input;

/// <summary>
/// MCP tool parameter for lists of links: handles or Gramps IDs. Accepts a JSON array of strings,
/// objects with <c>ref</c>/<c>handle</c>, a comma/semicolon/newline-separated string,
/// a single handle as a plain string (same as a one-element list),
/// or a string containing a JSON array (e.g. <c>["h1","h2"]</c>).
/// </summary>
[JsonConverter(typeof(FlexibleHandleListJsonConverter))]
public sealed class FlexibleHandleList
{
    /// <summary>Human-readable hint for MCP tool parameter descriptions.</summary>
    public const string DescriptionHint =
        "Handles or Gramps IDs of the records this field links: JSON array [\"h1\",\"h2\"], one value, or a " +
        "comma/semicolon/newline-separated string; entries may also be objects {\"ref\":\"…\"}.";

    /// <summary>Description hint for a list of tags, which have no Gramps ID.</summary>
    public const string TagHint =
        "Tag handles; tags have no Gramps ID, and list_objects(objectType: \"tags\") lists them: JSON array " +
        "[\"h1\",\"h2\"], one handle, or a comma/semicolon/newline-separated string.";

    public required string[] Handles { get; init; }

    public static implicit operator string[]?(FlexibleHandleList? value) => value?.Handles;
}
