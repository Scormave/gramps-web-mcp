using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;
using GrampsWeb.Mcp.Tools.Parsing;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// MCP tools for reading Note objects—textual annotations attached to genealogical objects.
/// </summary>
[McpServerToolType]
public static class NoteTools
{
    [Description(
        "Read-only: one note (text, type, Plain vs Html format).")]
    internal static async Task<string> ReadNoteAsync(
        [Description("Note handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        GrampsApiClient client)
    {
        try
        {
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "notes");
            var note = await client.GetOrNullIfNotFoundAsync<GrampsNote>(
                $"/api/notes/{Uri.EscapeDataString(resolvedHandle)}");
            if (note == null)
                return NotFoundHelper.NotFoundMessage("Note", handle);
            var backlinks = await BacklinkCollector.CollectAsync(client, "notes", resolvedHandle);
            return await NoteFormatter.FormatNoteFullAsync(note, client, backlinks);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool(Title = "Create Note", ReadOnly = false, Destructive = false)]
    [Description(
        "Create a free-text note (research notes, transcriptions, comments) of a given type. " +
        "Returns the new handle, Gramps ID and type, with next steps. The note is attached to nothing yet: add its handle " +
        "to the noteHandles of each person, family, event, place, source or citation it belongs to (their update tool, linkMode add). " +
        "Change an existing note with update_note. An unknown type is rejected before anything is saved.")]
    public static async Task<string> CreateNote(
        [Description("The note text (required).")]
        string text,
        [Description("Note type, default General; e.g. Research, Transcript, Person Note, Event Note, Citation. " + ToolDescriptionFragments.KnownType)]
        string noteType = "General",
        [Description("Text format: Plain (default) or Html.")]
        string format = "Plain",
        [Description("Tags. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Mark the note private (default false).")]
        bool isPrivate = false,
        GrampsApiClient client = null!)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text))
                throw McpToolErrors.ValidationError("Error: text is required");

            noteType = await KnownTypes.ResolveAsync(noteType, "note_types", client);

            var formatCode = NoteTextFormatParser.ParseRequired(format);

            var request = new CreateNoteRequest
            {
                Text = new StyledTextRequest { Text = text, Tags = [] },
                Type = noteType,
                Format = formatCode,
                TagList = tagHandles,
                Private = isPrivate
            };

            var (handle, grampsId) = await client.PostMutationAsync("/api/notes/", request, "Note");
            var typeLabel = string.IsNullOrWhiteSpace(noteType)
                ? "General"
                : await GrampsDefaultTypeLabels.FormatNoteTypeAsync(client, noteType);
            return ResponseEnvelope.CreateSuccess(
                "Note", handle, grampsId,
                typeLabel, ResponseEnvelope.NoteCreateNextSteps(handle!));
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool(Title = "Update Note", ReadOnly = false, Destructive = false)]
    [Description(
        "Change an existing note: its text, type, format, tags or private flag. text replaces the whole body; appendText adds to " +
        "the end of the body as stored now, keeping the rest of it, styling and links included, so it suits adding a finding to a long note. " +
        ToolDescriptionFragments.UpdateSemantics + " Returns the handle and Gramps ID; a missing note returns a not-found message. " +
        "Which records the note is attached to is stored on them (their noteHandles), not here.")]
    public static async Task<string> UpdateNote(
        [Description("The note to change. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("New text, replacing the whole body; changed text loses the styling and links of the old one, the same text keeps them. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? text = null,
        [Description("Text to add at the end of the body, on a new line: a line break is put in between unless the body already ends with one, " +
                     "so start with a line break to leave an empty line. The rest of the body, styling and links included, is kept. Cannot be combined with text.")]
        string? appendText = null,
        [Description("New note type. " + ToolDescriptionFragments.OmitToKeepScalar + " " + ToolDescriptionFragments.KnownType)]
        string? noteType = null,
        [Description("Plain or Html. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? format = null,
        [Description("Tags. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("true makes the record private, false public. " + ToolDescriptionFragments.OmitToKeepScalar)]
        bool? isPrivate = null,
        GrampsApiClient client = null!,
        [Description(LinkUpdates.Description)]
        string linkMode = "replace")
    {
        try
        {
            LinkUpdates.Validate(linkMode);
            if (appendText != null)
            {
                if (text != null)
                    throw McpToolErrors.ValidationError(
                        "Pass text to replace the whole body or appendText to add to its end, not both.");
                if (string.IsNullOrWhiteSpace(appendText))
                    throw McpToolErrors.ValidationError("appendText is empty: pass the text to add.");
            }

            using var updateLease = await client.BeginUpdateAsync();
            if (noteType != null)
                noteType = await KnownTypes.ResolveAsync(noteType, "note_types", client);

            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "notes");
            var note = await GrampsObjectPatch.LoadAsync(client, $"/api/notes/{Uri.EscapeDataString(resolvedHandle)}");
            if (note == null)
                return NotFoundHelper.NotFoundMessage("Note", handle);

            // The stored text carries styling and links (StyledText tags); keep it unless the text really changes.
            var storedText = note.Root["text"] is JsonObject styled
                ? GrampsObjectPatch.StringValue(styled["string"])
                : GrampsObjectPatch.StringValue(note.Root["text"]);
            if (text != null && !string.Equals(text, storedText, StringComparison.Ordinal))
                note.Set("text", new StyledTextRequest { Text = text, Tags = [] });
            if (appendText != null)
            {
                // Tag ranges index the old text, which stays at the start, so styling and links stay where they were.
                var body = string.IsNullOrEmpty(storedText) || storedText.EndsWith('\n')
                    ? storedText + appendText
                    : storedText + "\n" + appendText;
                if (note.Root["text"] is JsonObject storedStyled)
                    storedStyled["string"] = body;
                else
                    note.Set("text", new StyledTextRequest { Text = body, Tags = [] });
            }
            note.Set("type", noteType);
            note.Set("format", NoteTextFormatParser.ParseOptional(format));
            note.ApplyHandles("tag_list", tagHandles, linkMode);
            note.Set("private", isPrivate);

            await note.SaveAsync(client);
            return ResponseEnvelope.UpdateSuccess("Note", note.Handle, note.GrampsId);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

}
