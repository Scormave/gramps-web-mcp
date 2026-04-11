using System.ComponentModel;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;
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
        "Get note data by handle. Returns the text content, note type (General/Research/TODO/etc), " +
        "and format (plain text or HTML).")]
    public static async Task<string> GetNote(
        [Description("Note handle — use list_objects('notes') or search() to find handles")]
        string handle,
        GrampsApiClient client)
    {
        try
        {
            var note = await client.GetOrNullIfNotFoundAsync<GrampsNote>($"/api/notes/{handle}");
            return note == null
                ? $"Note not found: {handle}"
                : await NoteFormatter.FormatNoteFullAsync(note, client);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Create a text note. Call get_types() for valid note_type values. " +
        "format: 0=Plain text, 1=Formatted (HTML). " +
        "After creating, add note handle to any object via update_{type}(noteHandles). " +
        "Returns note handle.")]
    public static async Task<string> CreateNote(
        [Description("Note text content")]
        string text,
        [Description("Note type — call get_types to get valid values (default: 'General')")]
        string noteType = "General",
        [Description("Text format: 0=Plain Text, 1=HTML (default: 0)")]
        int format = 0,
        [Description("Array of tag handles (optional)")]
        string[]? tagHandles = null,
        [Description("Mark as private (optional)")]
        bool isPrivate = false,
        GrampsApiClient client = null!)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text))
                throw McpToolErrors.ValidationError("Error: text is required");

            var request = new CreateNoteRequest
            {
                Text = new StyledTextRequest { Text = text, Tags = [] },
                Type = noteType,
                Format = format,
                TagList = tagHandles,
                Private = isPrivate
            };

            var response = await client.PostMutationAsync<GrampsNote>("/api/notes/", request, "Note");
            var typeLabel = string.IsNullOrWhiteSpace(response.Type)
                ? "General"
                : await GrampsDefaultTypeLabels.FormatNoteTypeAsync(client, response.Type);
            return $"Note created successfully\n" +
                   $"Handle: {response.Handle}\n" +
                   $"Gramps ID: {response.GrampsId}\n" +
                   $"Type: {typeLabel}\n" +
                   $"Text preview: {(response.Text?.Substring(0, Math.Min(50, response.Text.Length)) ?? "—")}...";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Update existing note. Pass only fields that need to change. " +
        "⚠ WARNING: passing empty lists will REMOVE those linked objects.")]
    public static async Task<string> UpdateNote(
        [Description("Note handle")]
        string handle,
        [Description("Update note text")]
        string? text = null,
        [Description("Update note type")]
        string? noteType = null,
        [Description("Update format (0=Plain, 1=HTML)")]
        int? format = null,
        [Description("Replace tag handles")]
        string[]? tagHandles = null,
        GrampsApiClient client = null!)
    {
        try
        {
            var note = await client.GetOrNullIfNotFoundAsync<GrampsNote>($"/api/notes/{handle}");
            if (note == null)
                return $"Note not found: {handle}";

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
                Format = format ?? note.Format,
                TagList = tagHandles ?? note.TagList,
                Private = note.Private
            };

            var response = await client.PutMutationAsync<GrampsNote>($"/api/notes/{handle}", updateRequest, "Note");
            var typeLabel = string.IsNullOrWhiteSpace(response.Type)
                ? "General"
                : await GrampsDefaultTypeLabels.FormatNoteTypeAsync(client, response.Type);
            return $"Note updated successfully\n" +
                   $"Handle: {response.Handle}\n" +
                   $"Gramps ID: {response.GrampsId}\n" +
                   $"Type: {typeLabel}";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Delete a note. Will warn if referenced by any genealogical object.")]
    public static async Task<string> DeleteNote(
        [Description("Note handle")]
        string handle,
        [Description("Force delete despite backlinks (default: false)")]
        bool force = false,
        GrampsApiClient client = null!)
    {
        try
        {
            var payload = await client.GetJsonOrNullIfNotFoundAsync($"/api/notes/{handle}?backlinks=true");
            if (payload is null || payload.Value.ValueKind == JsonValueKind.Null)
                return $"Note not found: {handle}";
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
            return $"Note deleted successfully [{handle}]";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }
}
