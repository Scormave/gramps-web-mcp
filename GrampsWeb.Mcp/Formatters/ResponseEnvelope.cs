using System.Text;
using System.Text.Json;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Adds machine-readable metadata headers and next-step hints to tool responses.
/// </summary>
public static class ResponseEnvelope
{
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
        if (!string.IsNullOrWhiteSpace(displayName))
            sb.AppendLine($"name: {JsonSerializer.Serialize(displayName)}");
        sb.AppendLine("---");

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
        return sb.ToString();
    }

    /// <summary>
    /// Formats a successful delete response.
    /// </summary>
    public static string DeleteSuccess(string objectType, string handle)
    {
        return $"---\ntype: {objectType}\naction: deleted\nhandle: {handle}\n---";
    }

    public static string[] PersonCreateNextSteps(string? handle,
        bool hasEvents = false, bool hasFamily = false, bool hasNotes = false)
    {
        if (string.IsNullOrWhiteSpace(handle))
            return ["The person was created, but no handle was returned. Find the existing record with search before adding links; do not create it again."];

        var steps = new List<string>();
        // Existing references do not tell us their event types. Do not suggest duplicates.
        if (hasEvents)
            steps.Add($"Check linked events before adding birth/death: get_object(objectType: \"person\", identifier: \"{handle}\", extended: true)");
        else
            steps.Add($"If birth or death is known: create_event(eventType: \"Birth\", date: \"...\", placeHandle: \"...\"), then update_person(handle: \"{handle}\", eventRefs: [{{ref: \"<event_handle>\", role: \"Primary\"}}], linkMode: \"add\")");
        if (!hasFamily)
            steps.Add($"If a parent/spouse family is needed, check for an existing family first; create_family(fatherHandle: \"{handle}\", ...) or create_family(motherHandle: \"{handle}\", ...) creates a new one.");
        if (!hasNotes)
            steps.Add($"If a note is needed: create_note(text: \"...\") then update_person(handle: \"{handle}\", noteHandles: [\"<note_handle>\"], linkMode: \"add\").");
        return steps.ToArray();
    }

    public static string[] EventCreateNextSteps(string handle) => new[]
    {
        $"Attach to person: update_person(handle: \"<person>\", eventRefs: [{{ref: \"{handle}\", role: \"Primary\"}}], linkMode: \"add\"). Existing event references and roles are preserved.",
    };

    public static string[] SourceCreateNextSteps(string handle) => new[]
    {
        $"Create citation: create_citation(sourceHandle: \"{handle}\", page: \"...\", confidence: \"Normal\")",
    };

    public static string[] NoteCreateNextSteps(string handle) => new[]
    {
        $"Attach to person: update_person(handle: \"<person>\", noteHandles: [\"{handle}\"], linkMode: \"add\")",
        $"Attach to event: update_event(handle: \"<event>\", noteHandles: [\"{handle}\"], linkMode: \"add\")",
    };

    public static string[] PlaceCreateNextSteps(string handle) => new[]
    {
        $"Use in event: create_event(eventType: \"...\", placeHandle: \"{handle}\", ...)",
    };

    public static string[] TagCreateNextSteps(string handle) => new[]
    {
        $"Attach to any object via its tagHandles parameter on create/update",
    };

    public static string[] CitationCreateNextSteps(string handle) => new[]
    {
        $"Attach to person/event/place via citationHandles on create/update",
    };

    public static string[] FamilyCreateNextSteps(string? handle, bool hasChildren = false, bool hasEvents = false)
    {
        if (string.IsNullOrWhiteSpace(handle))
            return ["The family was created, but no handle was returned. Find the existing record with search before adding links; do not create it again."];
        var steps = new List<string>();
        if (!hasChildren || !hasEvents)
            steps.Add("Use linkMode: \"add\" to keep existing childRefs/eventRefs and their relationship or role metadata.");
        if (!hasChildren)
            steps.Add($"If children are known: update_family(handle: \"{handle}\", childRefs: [{{ref: \"<person_handle>\", frel: \"Birth\", mrel: \"Birth\"}}], linkMode: \"add\")");
        if (!hasEvents)
            steps.Add($"If family events are known: update_family(handle: \"{handle}\", eventRefs: [{{ref: \"<event_handle>\", role: \"Primary\"}}], linkMode: \"add\")");
        return steps.ToArray();
    }

    public static string[] RepositoryCreateNextSteps(string handle) => new[]
    {
        $"Create source referencing this repository",
        $"View: get_object(objectType: \"repository\", identifier: \"{handle}\")",
    };
}
