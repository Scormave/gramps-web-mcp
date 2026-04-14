using System.Text;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Adds machine-readable metadata headers and next-step hints to tool responses.
/// </summary>
public static class ResponseEnvelope
{
    /// <summary>
    /// Wraps a read-tool response with a machine-readable YAML-like header.
    /// </summary>
    public static string WithMetadata(string body, string objectType, string? handle, string? grampsId)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"type: {objectType}");
        if (!string.IsNullOrWhiteSpace(handle))
            sb.AppendLine($"handle: {handle}");
        if (!string.IsNullOrWhiteSpace(grampsId))
            sb.AppendLine($"gramps_id: {grampsId}");
        sb.AppendLine("---");
        sb.Append(body);
        return sb.ToString();
    }

    /// <summary>
    /// Formats a successful create response with next steps.
    /// </summary>
    public static string CreateSuccess(string objectType, string? handle, string? grampsId, string? displayName, string[]? nextSteps = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"type: {objectType}");
        sb.AppendLine($"action: created");
        if (!string.IsNullOrWhiteSpace(handle))
            sb.AppendLine($"handle: {handle}");
        if (!string.IsNullOrWhiteSpace(grampsId))
            sb.AppendLine($"gramps_id: {grampsId}");
        sb.AppendLine("---");
        sb.AppendLine($"{objectType} created successfully");
        if (!string.IsNullOrWhiteSpace(displayName))
            sb.AppendLine($"Name: {displayName}");
        sb.AppendLine($"Handle: {handle}");
        sb.AppendLine($"Gramps ID: {grampsId}");

        if (nextSteps is { Length: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Next steps:");
            foreach (var step in nextSteps)
                sb.AppendLine($"  • {step}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats a successful update response.
    /// </summary>
    public static string UpdateSuccess(string objectType, string? handle, string? grampsId)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"type: {objectType}");
        sb.AppendLine($"action: updated");
        if (!string.IsNullOrWhiteSpace(handle))
            sb.AppendLine($"handle: {handle}");
        if (!string.IsNullOrWhiteSpace(grampsId))
            sb.AppendLine($"gramps_id: {grampsId}");
        sb.AppendLine("---");
        sb.AppendLine($"{objectType} updated successfully");
        sb.AppendLine($"Handle: {handle}");
        sb.AppendLine($"Gramps ID: {grampsId}");
        return sb.ToString();
    }

    /// <summary>
    /// Formats a successful delete response.
    /// </summary>
    public static string DeleteSuccess(string objectType, string handle)
    {
        return $"---\ntype: {objectType}\naction: deleted\nhandle: {handle}\n---\n{objectType} deleted successfully [{handle}]";
    }

    public static string[] PersonCreateNextSteps(string handle) => new[]
    {
        $"Add birth event: add_event_to_person(personHandle: \"{handle}\", eventType: \"Birth\", date: \"...\", place: \"...\")",
        $"Add death event: add_event_to_person(personHandle: \"{handle}\", eventType: \"Death\", date: \"...\")",
        $"Link to family: create_family(fatherHandle: \"{handle}\", ...) or create_family(motherHandle: \"{handle}\", ...)",
        $"Add a note: create_note(text: \"...\") then update_person(handle: \"{handle}\", noteHandles: [\"<note_handle>\"])",
    };

    public static string[] EventCreateNextSteps(string handle) => new[]
    {
        $"Attach to person: update_person(handle: \"<person>\", eventRefHandles: [\"...\", \"{handle}\"], eventRefRoles: [\"...\", \"Primary\"])",
        $"Or use: add_event_to_person(personHandle: \"<person>\", ...) for new events",
    };

    public static string[] SourceCreateNextSteps(string handle) => new[]
    {
        $"Create citation: create_citation(sourceHandle: \"{handle}\", page: \"...\", confidence: \"Normal\")",
    };

    public static string[] NoteCreateNextSteps(string handle) => new[]
    {
        $"Attach to person: update_person(handle: \"<person>\", noteHandles: [\"{handle}\"])",
        $"Attach to event: update_event(handle: \"<event>\", noteHandles: [\"{handle}\"])",
    };

    public static string[] PlaceCreateNextSteps(string handle) => new[]
    {
        $"Use in event: create_event(eventType: \"...\", placeHandle: \"{handle}\", ...)",
        $"Or use: add_event_to_person(personHandle: \"...\", eventType: \"...\", place: \"{handle}\")",
    };

    public static string[] TagCreateNextSteps(string handle) => new[]
    {
        $"Attach to any object via its tagHandles parameter on create/update",
    };

    public static string[] CitationCreateNextSteps(string handle) => new[]
    {
        $"Attach to person/event/place via citationHandles on create/update",
    };
}
