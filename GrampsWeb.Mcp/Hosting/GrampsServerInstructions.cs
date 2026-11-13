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
        - With a name or other text, start with search; to browse or filter one type, use list_records. Read records with get_records, all the ones you need in one call.
        - Gramps IDs show the type by prefix: I person, F family, E event, P place, S source, C citation, R repository, N note, O media; tags have none. Arguments that take one record and link lists (…Handles, …Refs) accept a Gramps ID or a handle; tags are given by handle.
        - Kinship: get_person_tree for ancestors or descendants, get_relations for how two people are related, get_timeline for the dated events around a person, family or place; list_records with rules selects by kinship across the tree, such as everyone related to a person or people with no family links.
        - find_duplicates lists people who may be entered twice, across spellings, Cyrillic and Latin letters, and the male and female forms of their names.
        - Report what the tree records, and say so when something is not recorded instead of guessing.
        """;

    private const string Media =
        """

        - get_media_image shows a photo or document scan; the default thumbnail keeps handwriting legible.
        """;

    private const string Writing =
        """


        Changing the tree:
        - Change data only when the user asked. Search for an existing record before creating one: create_records does not check for duplicates except for the people it creates, refusing a probable duplicate with action "refused" (pass notDuplicateOf when it is someone else) and listing namesakes after creating.
        - create_records creates new records, one or many: a record links to records already in the tree by handle and to records of the same call by "#key", and Gramps Web saves them all or none in one transaction, so one undo_change removes them. Enter one register entry, with its people, family, events, citation and transcript, in one call.
        - Each link is stored on one side only. Attach events, citations, notes and media through the owning person, family, event or place, and children through the family's childRefs; backlinks shown on a record are read-only.
        - update_* tools change only the arguments passed. Link lists follow linkMode: add (the default) adds links and changes only the fields given on a link already stored, such as a role; remove drops links; replace overwrites every link of that kind.
        - Argument formats (dates, names, structured fields) and valid types: get_reference.
        - Use delete_records only after the user confirmed those exact records, and force past links from other records only after a separate confirmation: Gramps Web then removes those links.
        - When two records describe the same person, family, event, place or other record, merge_records joins the duplicate into the record to keep (find_duplicates finds such people), only after the user confirmed both: links move to the kept record, the duplicate is deleted, and undo_change reverts the merge.
        - undo_change reverts one transaction from get_recent_changes, deletions included: only after the user confirmed it, newest first, and force past conflicts only after a separate confirmation.
        """;

    private const string MediaUpload =
        """

        - upload_media uploads a photo, scan or PDF from the server's upload folder and shows it on records through attachTo; a file the tree already stores is reused, not uploaded twice.
        """;

    private const string ReadOnlyNote =
        """


        This server is read-only: no tool changes the tree.
        """;

    /// <summary>Instructions for the tools <paramref name="config"/> publishes.</summary>
    public static string For(GrampsConfig config) =>
        Reading
        + (config.MediaResourcesEnabled ? Media : "")
        + (config.ReadOnly ? ReadOnlyNote : Writing + (config.MediaUploadEnabled ? MediaUpload : ""));
}
