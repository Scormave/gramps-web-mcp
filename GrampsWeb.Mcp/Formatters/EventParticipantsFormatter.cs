using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Names the people and families of an event from <c>?profile=participants</c>. Place timelines list them
/// on each row; person timelines and extended people list the others in an event the person takes part
/// in under another role, such as the child of a "Birth [Father]" row.
/// </summary>
public static class EventParticipantsFormatter
{
    /// <summary>Participant names listed per row before the rest are counted.</summary>
    internal const int MaxParticipantsShown = 5;

    /// <summary>
    /// Participants of each event by event handle, leaving out <paramref name="personHandle"/>; events
    /// with no one else are left out. One batch read, none without handles.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, string>> LoadOtherParticipantsAsync(
        GrampsApiClient client, IEnumerable<string?> eventHandles, string? personHandle)
    {
        var events = await client.GetByHandlesAsync<GrampsEvent>(
            "events", eventHandles, e => e.Handle, query: "profile=participants");
        var others = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (handle, evt) in events)
        {
            if (FormatParticipants(evt.Profile?.Participants, personHandle) is { } text)
                others[handle] = text;
        }

        return others;
    }

    /// <summary>
    /// "Name (I0001), Name (I0002) [Witness]"; families show as "Father and Mother (F0001)".
    /// Primary and Family roles are implied and left out, and so is <paramref name="excludedPersonHandle"/>.
    /// </summary>
    public static string? FormatParticipants(GrampsEventParticipants? participants, string? excludedPersonHandle = null)
    {
        if (participants is null)
            return null;

        var names = new List<string>();
        foreach (var p in participants.People ?? [])
        {
            if (p.Person is not { } person || string.IsNullOrWhiteSpace(person.NameDisplay))
                continue;
            if (excludedPersonHandle != null && string.Equals(person.Handle, excludedPersonHandle, StringComparison.Ordinal))
                continue;
            names.Add(WithRole(WithId(person.NameDisplay.Trim(), person.GrampsId), p.Role));
        }

        foreach (var f in participants.Families ?? [])
        {
            if (f.Family is not { } family)
                continue;
            var parents = new[] { family.Father?.NameDisplay, family.Mother?.NameDisplay }
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!.Trim())
                .ToArray();
            var label = parents.Length > 0 ? string.Join(" and ", parents) : "Family";
            // "Family" is the usual role of a family event.
            var role = string.Equals(f.Role?.Trim(), "Family", StringComparison.OrdinalIgnoreCase) ? null : f.Role;
            names.Add(WithRole(WithId(label, family.GrampsId), role));
        }

        if (names.Count == 0)
            return null;
        if (names.Count <= MaxParticipantsShown)
            return string.Join(", ", names);
        return string.Join(", ", names.Take(MaxParticipantsShown)) + $", … (+{names.Count - MaxParticipantsShown} more)";
    }

    private static string WithId(string label, string? grampsId) =>
        string.IsNullOrWhiteSpace(grampsId) ? label : $"{label} ({grampsId.Trim()})";

    private static string WithRole(string label, string? role) =>
        string.IsNullOrWhiteSpace(role) || role.Trim().Equals("Primary", StringComparison.OrdinalIgnoreCase)
            ? label
            : $"{label} [{role.Trim()}]";
}
