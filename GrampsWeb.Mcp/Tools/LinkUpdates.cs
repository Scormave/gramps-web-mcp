namespace GrampsWeb.Mcp.Tools;

/// <summary>The linkMode argument of the update tools, applied by <see cref="GrampsObjectPatch.ApplyLinks"/>.</summary>
internal static class LinkUpdates
{
    public const string Description = "How to apply all supplied link lists: add (default) adds the links not stored yet and updates a stored link with the fields given for it (role, relation, call number, date; attributes, citations and notes are added), keeping the rest, so a bare handle changes nothing on a stored link; remove removes all links matching supplied handles (reference metadata is ignored); replace sets each full list, and a link it keeps keeps its stored privacy, citations, notes and crop unless the call sends new values. Omitted lists stay unchanged; empty lists clear only in replace mode. Does not affect attributes, names, addresses or URLs. Use separate calls to add and remove.";

    public static void Validate(string mode)
    {
        if (mode is not ("replace" or "add" or "remove"))
            throw McpToolErrors.ValidationError("linkMode must be replace, add, or remove.");
    }
}
