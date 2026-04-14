using System.ComponentModel;
using System.Text;
using System.Text.Json;
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
    [McpServerTool]
    [Description(
        "Read-only: one note (text, type, Plain vs Html format).")]
    public static async Task<string> GetNote(
        [Description("Note handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        GrampsApiClient client)
    {
        try
        {
            var note = await client.GetOrNullIfNotFoundAsync<GrampsNote>($"/api/notes/{handle}");
            return note == null
                ? NotFoundHelper.NotFoundMessage("Note", handle)
                : await NoteFormatter.FormatNoteFullAsync(note, client);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Create a note (write). Returns handle and Gramps ID. " +
        ToolDescriptionFragments.CallGetTypes + " " +
        "Link the note to people/events/etc. by passing its handle in that object's noteHandles on create/update.")]
    public static async Task<string> CreateNote(
        [Description("Note body text (required).")]
        string text,
        [Description("Note type key. " + ToolDescriptionFragments.CallGetTypes + " Default General.")]
        string noteType = "General",
        [Description("Text format: Plain or Html (default: Plain)")]
        string format = "Plain",
        [Description("Tag handles (optional). " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Mark as private (optional)")]
        bool isPrivate = false,
        GrampsApiClient client = null!)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text))
                throw McpToolErrors.ValidationError("Error: text is required");

            var typeError = await TypeCache.ValidateTypeAsync(noteType, "note_types", client);
            if (typeError != null) throw McpToolErrors.ValidationError(typeError);

            var formatCode = NoteTextFormatParser.ParseRequired(format);

            var request = new CreateNoteRequest
            {
                Text = new StyledTextRequest { Text = text, Tags = [] },
                Type = noteType,
                Format = formatCode,
                TagList = tagHandles,
                Private = isPrivate
            };

            var response = await client.PostMutationAsync<GrampsNote>("/api/notes/", request, "Note");
            var typeLabel = string.IsNullOrWhiteSpace(response.Type)
                ? "General"
                : await GrampsDefaultTypeLabels.FormatNoteTypeAsync(client, response.Type);
            return ResponseEnvelope.CreateSuccess(
                "Note", response.Handle, response.GrampsId,
                typeLabel, ResponseEnvelope.NoteCreateNextSteps(response.Handle!));
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Update a note (write). Only pass fields to change. " +
        ToolDescriptionFragments.UpdateEmptyListRemovesLinks + " " +
        ToolDescriptionFragments.CallGetTypes)]
    public static async Task<string> UpdateNote(
        [Description("Note handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("Body text. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? text = null,
        [Description("Note type. " + ToolDescriptionFragments.OmitToKeepScalar + " " + ToolDescriptionFragments.CallGetTypes)]
        string? noteType = null,
        [Description("Plain or Html. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? format = null,
        [Description("Replace tags. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Private flag. " + ToolDescriptionFragments.OmitToKeepScalar)]
        bool? isPrivate = null,
        GrampsApiClient client = null!)
    {
        try
        {
            if (noteType != null)
            {
                var typeError = await TypeCache.ValidateTypeAsync(noteType, "note_types", client);
                if (typeError != null) throw McpToolErrors.ValidationError(typeError);
            }

            var note = await client.GetOrNullIfNotFoundAsync<GrampsNote>($"/api/notes/{handle}");
            if (note == null)
                return NotFoundHelper.NotFoundMessage("Note", handle);

            var updateRequest = new CreateNoteRequest
            {
                Class = "Note",
                Handle = note.Handle,
                GrampsId = note.GrampsId,
                Change = note.Change,
                Text = new StyledTextRequest
                {
                    Text = text ?? note.Text ?? "",
                    Tags = []
                },
                Type = noteType ?? note.Type,
                Format = NoteTextFormatParser.ParseOptional(format) ?? note.Format,
                TagList = (string[]?)tagHandles ?? note.TagList,
                Private = isPrivate ?? note.Private
            };

            var response = await client.PutMutationAsync<GrampsNote>($"/api/notes/{handle}", updateRequest, "Note");
            return ResponseEnvelope.UpdateSuccess("Note", response.Handle, response.GrampsId);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Delete a note (destructive). Blocked when still attached elsewhere unless force=true.")]
    public static async Task<string> DeleteNote(
        [Description("Note handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("If true, delete despite backlinks (default false).")]
        bool force = false,
        GrampsApiClient client = null!)
    {
        try
        {
            var payload = await client.GetJsonOrNullIfNotFoundAsync($"/api/notes/{handle}?backlinks=true");
            if (payload is null || payload.Value.ValueKind == JsonValueKind.Null)
                return NotFoundHelper.NotFoundMessage("Note", handle);
            var response = payload.Value;

            var hasBacklinks = false;
            var backlinksInfo = new StringBuilder();
            if (response.TryGetProperty("backlinks", out var backlinksElement))
            {
                if (backlinksElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in backlinksElement.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.Array && property.Value.GetArrayLength() > 0)
                        {
                            hasBacklinks = true;
                            backlinksInfo.AppendLine($"  • {property.Name}: {property.Value.GetArrayLength()} reference(s)");
                        }
                    }
                }
            }

            if (hasBacklinks && !force)
            {
                return $"⚠️ Cannot delete note [{handle}] — it has references:\n" +
                       $"{backlinksInfo}" +
                       $"To delete anyway, call delete_note(handle, force=true).";
            }

            await client.DeleteAsync($"/api/notes/{handle}");
            return ResponseEnvelope.DeleteSuccess("Note", handle);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }
}
