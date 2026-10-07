using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Requests;

namespace GrampsWeb.Mcp.Tools;

/// <summary>Checks the roles of the event links that the person and family tools send.</summary>
internal static class EventRoles
{
    private const string Category = "event_role_types";

    /// <summary>
    /// Rejects a role that is neither a standard Gramps role nor a custom one the tree already uses, since Gramps would
    /// store a misspelt role as a new custom role, and writes each accepted role in its known spelling. An empty role
    /// stays empty (Gramps reads it as Primary), and linkMode remove matches links by handle only, so its roles are
    /// not checked.
    /// </summary>
    public static async Task CheckAsync(EventRefRequest[]? refs, GrampsApiClient client, string linkMode = "replace")
    {
        if (refs is null || linkMode == "remove")
            return;

        foreach (var eventRef in refs)
        {
            if (string.IsNullOrWhiteSpace(eventRef.Role))
                continue;

            var (label, error) = await TypeCache.ResolveTypeAsync(eventRef.Role.Trim(), Category, client);
            if (error != null)
                throw McpToolErrors.ValidationError($"eventRefs: {error}");
            eventRef.Role = label;
        }
    }
}
