namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// Shared prose for MCP <see cref="System.ComponentModel.DescriptionAttribute"/> to keep tool docs consistent.
/// </summary>
public static class ToolDescriptionFragments
{
    /// <summary>Suffix for parameters that take a Gramps object handle (API opaque string).</summary>
    public const string HandleDiscovery =
        "Handle is an opaque string returned by the API (not the numeric Gramps ID unless the parameter is named grampsId). " +
        "Find handles with search() or list_objects with the right object type (e.g. list_objects('people')), then pass the handle here.";

    /// <summary>Tool-level warning for update tools that replace linked-object lists.</summary>
    public const string UpdateEmptyListRemovesLinks =
        "WARNING: On update, passing an empty list [] for a replaceable list REMOVES all links of that kind (e.g. empty tagHandles removes every tag). " +
        "To leave a list unchanged, omit that parameter entirely—do not pass [] to mean 'no change'.";

    /// <summary>Short clause for optional update list parameters.</summary>
    public const string OmitToKeepEmptyClears =
        "Omit the parameter to leave unchanged; pass [] only to clear the list.";

    /// <summary>For optional scalar/string fields on update.</summary>
    public const string OmitToKeepScalar =
        "Omit to leave unchanged.";

    public const string CallGetTypes =
        "Call get_types first for valid custom type strings where applicable.";

    public const string CallGetDateInputGuide =
        "Call get_date_input_guide for Gramps date string formats.";

    public const string CallGetNameSchema =
        "Call get_name_schema for name field structure and examples.";

    public const string CallGetStructuredFieldInputGuide =
        "Call get_structured_field_input_guide for attributes, URLs, addresses, person refs, and flexible handle/name list syntax.";
}
