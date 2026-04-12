using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>
/// Wire shape for Gramps typed enumerations: <c>EventType</c>, <c>RepositoryType</c>, <c>FamilyRelType</c>,
/// <c>EventRoleType</c>, <c>PlaceType</c>, <c>NameType</c>, <c>NoteType</c>, etc.
/// </summary>
/// <remarks>
/// GET responses often send a plain JSON string instead of this object; pair model properties with <see cref="GrampsWeb.Mcp.Serialization.GrampsWireTypeStringConverter"/>.
/// </remarks>
public sealed class GrampsWireTypeObject
{
    [JsonPropertyName("_class")]
    public string? Class { get; set; }

    [JsonPropertyName("string")]
    public string? String { get; set; }

    /// <summary>Gramps enum index when <see cref="String"/> is empty (typical in mutation <c>new</c> payloads).</summary>
    [JsonPropertyName("value")]
    public int? Value { get; set; }

    /// <summary>Returns non-empty <see cref="String"/> if set; otherwise <see cref="Value"/> as invariant text.</summary>
    public string? ToPreferredString()
    {
        if (!string.IsNullOrWhiteSpace(String))
            return String.Trim();
        if (Value.HasValue)
            return Value.Value.ToString(CultureInfo.InvariantCulture);
        return null;
    }

    /// <summary>Parses the same rules as <see cref="ToPreferredString"/> from a JSON object (without full deserialization).</summary>
    public static string? TryReadPreferredString(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        if (root.TryGetProperty("string", out var str) && str.ValueKind == JsonValueKind.String)
        {
            var s = str.GetString();
            if (!string.IsNullOrWhiteSpace(s))
                return s.Trim();
        }

        if (root.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.Number)
            return val.GetRawText();

        return null;
    }
}
