namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// Shared prose for MCP <see cref="System.ComponentModel.DescriptionAttribute"/> to keep tool docs consistent.
/// </summary>
public static class ToolDescriptionFragments
{
    /// <summary>Suffix for parameters that take a Gramps object handle (API opaque string).</summary>
    public const string HandleDiscovery =
        "Handle or Gramps ID (e.g. I0001). Handles are opaque strings from the API. " +
        "Gramps IDs (like I0001, F0023) are auto-resolved to handles. " +
        "Find handles with search() or list_objects().";

    /// <summary>Tool-level warning for update tools that replace linked-object lists.</summary>
    public const string UpdateEmptyListRemovesLinks =
        "Link lists support linkMode: replace (default), add, remove. WARNING: In replace mode, an empty list [] REMOVES all links of that kind (e.g. empty tagHandles removes every tag). " +
        "To leave a list unchanged, omit that parameter entirely—do not pass [] to mean 'no change'.";

    /// <summary>Short clause for optional update list parameters.</summary>
    public const string OmitToKeepEmptyClears =
        "Omit to leave unchanged. For link lists, linkMode controls replace/add/remove; [] clears in replace mode and does nothing in add/remove. Other lists always use replacement.";

    /// <summary>For optional scalar/string fields on update.</summary>
    public const string OmitToKeepScalar =
        "Omit to leave unchanged.";

    public const string CallGetTypes =
        "Type values are validated by the server; see gramps://types resource or call get_reference(topic: \"types\") for available values.";

    public const string CallGetDateInputGuide =
        "Date format: use ISO dates (1990-03-15), year-only (1920), or modifiers (before 1920, about 1950). " +
        "Add the calendar at the end for Old Style and other calendars: 1856-07-20 (Julian). " +
        "See gramps://input-guide resource or call get_reference(topic: \"input-guide\", section: \"dates\") for full syntax.";

    public const string CallGetNameSchema =
        "Use shorthand 'Given Surname' or full JSON; see gramps://input-guide resource or call get_reference(topic: \"input-guide\", section: \"name_schema\") for the Name schema.";

    public const string CallGetStructuredFieldInputGuide =
        "Accepts JSON arrays or shorthand strings (e.g. 'Type: Value' for attributes). " +
        "See gramps://input-guide resource or call get_reference(topic: \"input-guide\", section: \"structured_fields\") for all formats.";
}
