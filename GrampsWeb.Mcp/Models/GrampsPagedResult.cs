using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>
/// Represents a paginated result set from the Gramps API.
/// </summary>
/// <typeparam name="T">The type of objects in the result set.</typeparam>
public class GrampsPagedResult<T>
{
    [JsonPropertyName("objects")]
    public T[]? Objects { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }
}
