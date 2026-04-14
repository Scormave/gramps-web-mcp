using GrampsWeb.Mcp.Client;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// Builds helpful not-found messages with hints when agents pass wrong identifiers.
/// </summary>
internal static class NotFoundHelper
{
    /// <summary>
    /// Builds a not-found message. If the identifier looks like a Gramps ID, adds a hint
    /// about using find_by_gramps_id or passing the opaque handle.
    /// </summary>
    public static string NotFoundMessage(string objectType, string identifier)
    {
        var msg = $"{objectType} not found: {identifier}";

        if (HandleResolver.LooksLikeGrampsId(identifier))
        {
            msg += $"\n\nHint: '{identifier}' looks like a Gramps ID. Try find_by_gramps_id(grampsId: \"{identifier}\") " +
                   $"to look it up, or use search(\"{identifier}\") to find it. " +
                   $"Tool parameters accept both handles and Gramps IDs — auto-resolution should work, " +
                   $"so this ID may genuinely not exist in the database.";
        }
        else if (identifier.Length < 5)
        {
            msg += $"\n\nHint: This identifier is very short. Handles are typically 20+ character strings. " +
                   $"If you meant a Gramps ID (like I0001), include the type prefix letter.";
        }

        return msg;
    }
}
