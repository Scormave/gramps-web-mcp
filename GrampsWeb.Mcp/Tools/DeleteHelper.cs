using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Formatters;

namespace GrampsWeb.Mcp.Tools;

internal static class DeleteHelper
{
    /// <summary>
    /// Shared delete-with-backlinks logic for all entity tools.
    /// </summary>
    public static async Task<string> DeleteWithBacklinksAsync(
        GrampsApiClient client,
        string objectType,
        string apiPath,
        string handle,
        bool force,
        string? originalIdentifier = null)
    {
        originalIdentifier ??= handle;
        var payload = await client.GetJsonOrNullIfNotFoundAsync(
            $"/api/{apiPath}/{Uri.EscapeDataString(handle)}?backlinks=true");
        if (payload is null || payload.Value.ValueKind == JsonValueKind.Null)
            return NotFoundHelper.NotFoundMessage(objectType, originalIdentifier);
        var response = payload.Value;

        if (!force && response.TryGetProperty("backlinks", out var backlinks)
            && backlinks.ValueKind == JsonValueKind.Object)
        {
            var refs = new StringBuilder();
            foreach (var prop in backlinks.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Array && prop.Value.GetArrayLength() > 0)
                    refs.AppendLine($"  {prop.Name}: {prop.Value.GetArrayLength()} reference(s)");
            }
            if (refs.Length > 0)
            {
                return $"Cannot delete {objectType} [{handle}]: other objects reference it.\n" +
                       $"Backlinks:\n{refs}" +
                       "Pass force=true to delete anyway (may leave dangling references).";
            }
        }

        await client.DeleteAsync($"/api/{apiPath}/{Uri.EscapeDataString(handle)}");
        return ResponseEnvelope.DeleteSuccess(objectType, handle);
    }
}
