using System.Text.Json;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// List endpoints may return a bare JSON array in the documented schema, while Gramps Web sometimes returns
/// a paged object with <c>objects</c>, <c>total</c>, and <c>page</c>. Accept both shapes.
/// </summary>
public static class GrampsPagedResultParser
{
    public static GrampsPagedResult<T>? Parse<T>(string body, JsonSerializerOptions options) where T : class
    {
        using var doc = JsonDocument.Parse(body);
        return Parse<T>(doc.RootElement, options);
    }

    public static GrampsPagedResult<T>? Parse<T>(JsonElement root, JsonSerializerOptions options) where T : class
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            var objects = JsonSerializer.Deserialize<T[]>(root.GetRawText(), options);
            var n = objects?.Length ?? 0;
            return new GrampsPagedResult<T>
            {
                Objects = objects,
                Total = n,
                Page = 1
            };
        }

        if (root.ValueKind == JsonValueKind.Object)
            return JsonSerializer.Deserialize<GrampsPagedResult<T>>(root.GetRawText(), options);

        return null;
    }
}
