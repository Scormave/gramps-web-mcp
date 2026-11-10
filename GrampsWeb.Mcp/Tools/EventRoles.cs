using System.Text.Json.Nodes;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Requests;

namespace GrampsWeb.Mcp.Tools;

/// <summary>Checks the roles of the event links that the person and family tools send.</summary>
internal static class EventRoles
{
    /// <summary>The default role of a person's event link.</summary>
    public const string Primary = "Primary";

    /// <summary>
    /// The default role of a family's event link, as in Gramps: Gramps and Gramps Web name the family among an
    /// event's participants and in its summary only through a Family link.
    /// </summary>
    public const string Family = "Family";

    private const string Category = "event_role_types";

    /// <summary>
    /// Rejects a role that is neither a standard Gramps role nor a custom one the tree already uses, since Gramps would
    /// store a misspelt role as a new custom role, and writes each accepted role in its known spelling. A bare handle
    /// (no <c>::Role</c>) takes <paramref name="defaultRole"/>. A link object without a role stays without one, so in
    /// replace mode it keeps the stored role, and linkMode remove matches links by handle only, so its roles are
    /// not checked. The attribute types of each link, such as Age, are checked as well.
    /// </summary>
    public static async Task CheckAsync(EventRefRequest[]? refs, GrampsApiClient client, string linkMode = "replace",
        string defaultRole = Primary)
    {
        if (refs is null || linkMode == "remove")
            return;

        foreach (var eventRef in refs)
        {
            await KnownTypes.CheckAttributesAsync(eventRef.AttributeList, client, "eventRefs");
            if (string.IsNullOrWhiteSpace(eventRef.Role))
            {
                if (eventRef.BareHandle)
                    eventRef.Role = defaultRole;
                continue;
            }

            var (label, error) = await TypeCache.ResolveTypeAsync(eventRef.Role.Trim(), Category, client);
            if (error != null)
                throw McpToolErrors.ValidationError($"eventRefs: {error}");
            eventRef.Role = label;
        }
    }

    /// <summary>
    /// Gives the Family role to each link in <c>event_ref_list</c> of a family that has no role, that is a link the
    /// call added without one; Gramps would read it as Primary. Stored links keep their roles.
    /// </summary>
    public static void SetMissingFamilyRoles(JsonObject family)
    {
        if (family["event_ref_list"] is not JsonArray list)
            return;

        foreach (var link in list.OfType<JsonObject>().Where(link => link["role"] is null))
            link["role"] = Family;
    }
}
