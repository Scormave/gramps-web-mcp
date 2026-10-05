namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// Shared prose for MCP <see cref="System.ComponentModel.DescriptionAttribute"/> to keep tool docs consistent.
/// </summary>
public static class ToolDescriptionFragments
{
    /// <summary>Suffix for parameters that take one Gramps object handle.</summary>
    public const string HandleDiscovery =
        "Handle or Gramps ID (e.g. I0001), resolved automatically; find it with search or list_objects.";

    /// <summary>Tool-level sentence for update tools: what omitting a value and linkMode do.</summary>
    public const string UpdateSemantics =
        "Pass only what changes; omitted arguments stay as they are. " +
        "Link lists (…Handles, …Refs) follow linkMode: replace (default) sets the whole list, add appends, " +
        "remove drops the given handles; in replace mode [] REMOVES every link of that kind.";

    /// <summary>For link-list parameters on update tools.</summary>
    public const string LinkListOnUpdate =
        "Omit to keep; applied per linkMode.";

    /// <summary>For lists on update tools that linkMode does not apply to (names, attributes, addresses, URLs).</summary>
    public const string ReplacedListOnUpdate =
        "Omit to keep; a value replaces the whole list and [] clears it, whatever linkMode says.";

    /// <summary>For optional scalar/string fields on update.</summary>
    public const string OmitToKeepScalar =
        "Omit to leave unchanged.";

    /// <summary>For type parameters checked by <see cref="Client.TypeCache.ValidateTypeAsync"/>.</summary>
    public const string KnownType =
        "Must be a standard Gramps type or a custom one the tree already uses; " +
        "anything else is rejected with suggestions. List: get_reference(topic: \"types\").";

    /// <summary>For date parameters parsed by <see cref="Dates.AgentDateParser"/>.</summary>
    public const string DateText =
        "Date text: 1856-07-20, 1920-05, 1920, 1 Jul 1919, about/before/after 1920, 1800-1850, between … and …; " +
        "append the calendar for other calendars, e.g. 1856-07-20 (Julian). Unreadable dates are rejected.";

    /// <summary>One pointer to the input guide for tools with structured arguments.</summary>
    public const string InputGuide =
        "Formats of every argument: get_reference(topic: \"input-guide\").";
}
