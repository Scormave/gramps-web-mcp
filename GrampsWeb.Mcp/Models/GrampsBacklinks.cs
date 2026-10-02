using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>Handles of objects referring to an object, from <c>?backlinks=true</c> (OpenAPI <c>Backlinks</c>).</summary>
public class GrampsBacklinks
{
    [JsonPropertyName("family")]
    public string[]? Family { get; set; }
}
