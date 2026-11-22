namespace GrampsWeb.Mcp.Config;

/// <summary>
/// When delete_object, merge_objects and undo_change ask the user to confirm through MCP elicitation
/// before changing the tree. Set with <c>GRAMPS_CONFIRMATION</c>.
/// </summary>
public enum ConfirmationMode
{
    /// <summary>
    /// Ask when the client supports elicitation; other clients, and clients that decline the question at once
    /// without showing it, work as before.
    /// </summary>
    WhenSupported,

    /// <summary>
    /// Always ask; refuse the change when the client cannot show the question or declines it at once without
    /// showing it.
    /// </summary>
    Required,

    /// <summary>Never ask; the assistant's own confirmation in chat is the only check.</summary>
    Off
}
