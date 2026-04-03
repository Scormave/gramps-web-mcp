using System.Text.Json;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Serializes dynamic API payloads to indented JSON for agent-readable output.
/// </summary>
public static class JsonResponseFormatter
{
    public static string FormatDynamic(object? obj)
    {
        if (obj == null) return "(no data)";
        return JsonSerializer.Serialize(
            obj,
            new JsonSerializerOptions { WriteIndented = true });
    }
}
