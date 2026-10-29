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

    [McpServerTool(Title = "Create Note", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(
        "Create a free-text note (research notes, transcriptions, comments) of a given type. " +
        "Returns the new handle, Gramps ID and type, with next steps. The note is attached to nothing yet: add its handle " +
        "to the noteHandles of each person, family, event, place, source or citation it belongs to (their update tool, linkMode add). " +
        "For the transcript of a cited record use type Transcript and attach the note to that citation. " +
        "Change an existing note with update_note. An unknown type is rejected before anything is saved. " +
        ToolDescriptionFragments.WriteAccess)]
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
            var request = await BuildNoteRequestAsync(text, noteType, format, tagHandles, isPrivate, client);
            var (handle, grampsId) = await client.PostMutationAsync("/api/notes/", request, "Note");
            var typeLabel = await FormatNoteTypeAsync(request.Type, client);
            return ResponseEnvelope.CreateSuccess(
                "Note", handle, grampsId,
                typeLabel, ResponseEnvelope.NoteCreateNextSteps(handle!));
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    /// <summary>The checked request body of create_note; create_records sends it too.</summary>
    internal static async Task<CreateNoteRequest> BuildNoteRequestAsync(
        string text, string noteType, string format, FlexibleHandleList? tagHandles, bool isPrivate,
        GrampsApiClient client)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw McpToolErrors.ValidationError("Error: text is required");

        noteType = await KnownTypes.ResolveAsync(noteType, "note_types", client);

        var formatCode = NoteTextFormatParser.ParseRequired(format);

        return new CreateNoteRequest
        {
            Text = new StyledTextRequest { Text = text, Tags = [] },
            Type = noteType,
            Format = formatCode,
            TagList = tagHandles,
            Private = isPrivate
        };
    }

    /// <summary>The label of a note type: General when none is set.</summary>
    internal static async Task<string> FormatNoteTypeAsync(string? noteType, GrampsApiClient client) =>
        string.IsNullOrWhiteSpace(noteType)
            ? "General"
            : await GrampsDefaultTypeLabels.FormatNoteTypeAsync(client, noteType);

    [McpServerTool(Title = "Update Note", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description(
        "Change an existing note: its text, type, format, tags or private flag. text replaces the whole body. For a long note, " +
        "replaceText with withText changes one fragment and appendText adds to the end: both work on the body as stored now " +
        "and keep the rest of it, styling and links included. " +
        ToolDescriptionFragments.UpdateSemantics + " Returns the handle and Gramps ID; a missing note returns a not-found message. " +
        "Which records the note is attached to is stored on them (their noteHandles), not here. " +
        "For a new note use create_note; to remove one, delete_object. " + ToolDescriptionFragments.WriteAccess)]
    public static async Task<string> UpdateNote(
        [Description("The note to change. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("New text, replacing the whole body; changed text loses the styling and links of the old one, the same text keeps them. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? text = null,
        [Description("Text to add at the end of the body, on a new line: a line break is put in between unless the body already ends with one, " +
                     "so start with a line break to leave an empty line. The rest of the body, styling and links included, is kept. Cannot be combined with text.")]
        string? appendText = null,
        [Description("A fragment of the body to replace with withText, copied exactly as stored, spaces, line breaks and letter case included. " +
                     "It must occur once: include enough of the surrounding text to make it unique. The rest of the body, styling and links included, " +
                     "is kept. Applied before appendText; cannot be combined with text.")]
        string? replaceText = null,
        [Description("The text to put in place of replaceText; an empty string deletes the fragment.")]
        string? withText = null,
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
            if (text != null && (appendText != null || replaceText != null))
                throw McpToolErrors.ValidationError(
                    "text replaces the whole body, so it cannot be combined with replaceText or appendText, which change part of it.");
            if (appendText != null && string.IsNullOrWhiteSpace(appendText))
                throw McpToolErrors.ValidationError("appendText is empty: pass the text to add.");
            if (replaceText == null && withText != null)
                throw McpToolErrors.ValidationError("withText needs replaceText: pass the fragment of the body to replace.");
            if (replaceText != null)
            {
                if (replaceText.Length == 0)
                    throw McpToolErrors.ValidationError("replaceText is empty: pass the fragment of the body to replace.");
                if (withText == null)
                    throw McpToolErrors.ValidationError(
                        "withText is missing: pass the text to put in place of replaceText, or an empty string to delete it.");
            }

            using var updateLease = await client.BeginUpdateAsync();
            if (noteType != null)
                noteType = await KnownTypes.ResolveAsync(noteType, "note_types", client);

            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "notes");
            var note = await GrampsObjectPatch.LoadAsync(client, $"/api/notes/{Uri.EscapeDataString(resolvedHandle)}");
            if (note == null)
                return NotFoundHelper.NotFoundMessage("Note", handle);

            // The stored text carries styling and links (StyledText tags); keep it unless the text really changes.
            var styled = note.Root["text"] as JsonObject;
            var storedText = GrampsObjectPatch.StringValue(styled != null ? styled["string"] : note.Root["text"]) ?? "";
            if (text != null && !string.Equals(text, storedText, StringComparison.Ordinal))
                note.Set("text", new StyledTextRequest { Text = text, Tags = [] });
            if (replaceText != null || appendText != null)
            {
                var body = replaceText != null
                    ? ReplaceFragment(storedText, replaceText, withText!, styled?["tags"] as JsonArray)
                    : storedText;
                // Appended text goes after every tag range, so styling and links stay where they were.
                if (appendText != null)
                    body = body.Length == 0 || body.EndsWith('\n') ? body + appendText : body + "\n" + appendText;
                if (styled != null)
                    styled["string"] = body;
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

    /// <summary>
    /// Replaces the one occurrence of <paramref name="fragment"/> in <paramref name="body"/> and fits the tag ranges
    /// (styling and links) to the new text.
    /// </summary>
    private static string ReplaceFragment(string body, string fragment, string replacement, JsonArray? tags)
    {
        var start = body.IndexOf(fragment, StringComparison.Ordinal);
        if (start < 0)
            throw McpToolErrors.ValidationError(
                "replaceText was not found in the note: copy the fragment exactly as stored, spaces, line breaks and letter case included.");
        var count = 1;
        for (var next = body.IndexOf(fragment, start + 1, StringComparison.Ordinal); next >= 0;
             next = body.IndexOf(fragment, next + 1, StringComparison.Ordinal))
            count++;
        if (count > 1)
            throw McpToolErrors.ValidationError(
                $"replaceText occurs {count} times in the note: include more of the surrounding text so that it occurs once.");

        if (tags != null)
        {
            // Gramps counts tag ranges in Unicode code points, .NET strings in UTF-16 units.
            var from = CodePointCount(body.AsSpan(0, start));
            MoveTagRanges(tags, from, from + CodePointCount(fragment), CodePointCount(replacement));
        }
        return string.Concat(body.AsSpan(0, start), replacement, body.AsSpan(start + fragment.Length));
    }

    /// <summary>
    /// Moves the ranges of each tag for the text [start, end) replaced by text of newLength: ranges after it shift,
    /// a range around it stretches or shrinks with it, a range partly in it keeps its part outside, and a range
    /// wholly inside it goes, as does a tag left without ranges.
    /// </summary>
    private static void MoveTagRanges(JsonArray tags, int start, int end, int newLength)
    {
        var shift = newLength - (end - start);
        for (var t = tags.Count - 1; t >= 0; t--)
        {
            if (tags[t]?["ranges"] is not JsonArray ranges)
                continue;
            for (var r = ranges.Count - 1; r >= 0; r--)
            {
                if (ranges[r] is not JsonArray { Count: 2 } range || !TryGetInt(range[0], out var from) || !TryGetInt(range[1], out var to))
                    continue;
                (int From, int To) moved =
                    to <= start ? (from, to)
                    : from >= end ? (from + shift, to + shift)
                    : from <= start && to >= end ? (from, to + shift)
                    : from < start ? (from, start)
                    : to > end ? (end + shift, to + shift)
                    : (0, 0);
                if (moved == (from, to))
                    continue;
                if (moved.To <= moved.From)
                    ranges.RemoveAt(r);
                else
                {
                    range[0] = moved.From;
                    range[1] = moved.To;
                }
            }
            if (ranges.Count == 0)
                tags.RemoveAt(t);
        }
    }

    private static bool TryGetInt(JsonNode? node, out int value)
    {
        value = 0;
        return node is JsonValue v && v.TryGetValue(out value);
    }

    private static int CodePointCount(ReadOnlySpan<char> text)
    {
        var count = 0;
        foreach (var c in text)
        {
            if (!char.IsLowSurrogate(c))
                count++;
        }
        return count;
    }
}
