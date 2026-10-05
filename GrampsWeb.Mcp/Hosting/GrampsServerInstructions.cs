using GrampsWeb.Mcp.Config;

namespace GrampsWeb.Mcp.Hosting;

/// <summary>
/// Server instructions sent to MCP clients at initialization: how the tools fit together.
/// Sections for tools the configuration hides are left out.
/// </summary>
internal static class GrampsServerInstructions
{
    private const string Reading =
        """
        Gramps Web family tree: people, families, events, places, sources, citations, repositories, notes, media and tags.

        Finding and reading:
        - With a name or other text, start with search; to browse or filter one type, use list_objects. Read one record with get_object.
        - Gramps IDs show the type by prefix: I person, F family, E event, P place, S source, C citation, R repository, N note, O media, T tag. Arguments that take one record accept a Gramps ID or a handle; link lists (…Handles, …Refs) take handles.
        - Kinship: get_person_tree for ancestors or descendants, get_relations for how two people are related, get_timeline for the dated events around a person, family or place.
        - Report what the tree records, and say so when something is not recorded instead of guessing.
        """;

    private const string Media =
        """

        - read_media shows a photo or document scan; the default thumbnail keeps handwriting legible.
        """;

    private const string Writing =
        """


        Changing the tree:
        - Change data only when the user asked. Create tools do not check for duplicates: search for an existing record first.
        - Create what a record links to before the record: repositories before sources, sources before citations, places before events, people and events before the families that link them.
        - Each link is stored on one side only. Attach events, citations, notes and media through the owning person, family, event or place, and children through the family's childRefs; backlinks shown on a record are read-only.
        - update_* tools change only the arguments passed. Link lists follow linkMode, and replace (the default) overwrites every link of that kind, so use add or remove to change one link.
        - Argument formats (dates, names, structured fields) and valid types: get_reference.
        - Use delete_object only after the user confirmed that exact record, and force past backlinks only after a separate confirmation.
        """;

    private const string ReadOnlyNote =
        """


        This server is read-only: no tool changes the tree.
        """;

    /// <summary>Instructions for the tools <paramref name="config"/> publishes.</summary>
    public static string For(GrampsConfig config) =>
        Reading
        + (config.MediaResourcesEnabled ? Media : "")
        + (config.ReadOnly ? ReadOnlyNote : Writing);
}
