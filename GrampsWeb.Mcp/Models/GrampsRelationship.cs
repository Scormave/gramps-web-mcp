using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>
/// Closest relationship from <c>GET /api/relations/{handle1}/{handle2}</c> (OpenAPI <c>Relationship</c>).
/// Read as "person 2 is the {relationship_string} of person 1".
/// </summary>
public class GrampsRelationship
{
    /// <summary>Localized, e.g. "third cousin twice removed" or "husband"; empty when unrelated.</summary>
    [JsonPropertyName("relationship_string")]
    public string? RelationshipString { get; set; }

    /// <summary>Generations from person 1 up to the common ancestor; -1 when there is none, also for spouses.</summary>
    [JsonPropertyName("distance_common_origin")]
    public int? DistanceCommonOrigin { get; set; }

    /// <summary>Generations from person 2 up to the common ancestor; -1 when there is none, also for spouses.</summary>
    [JsonPropertyName("distance_common_other")]
    public int? DistanceCommonOther { get; set; }
}

/// <summary>
/// One entry of <c>GET /api/relations/{handle1}/{handle2}/all</c> (OpenAPI <c>RelationshipItem</c>),
/// closest first. Unrelated people come back as a single empty object.
/// </summary>
public class GrampsRelationshipItem
{
    [JsonPropertyName("relationship_string")]
    public string? RelationshipString { get; set; }

    /// <summary>Person handles; empty for spouses or when a parent is unknown.</summary>
    [JsonPropertyName("common_ancestors")]
    public string[]? CommonAncestors { get; set; }
}
