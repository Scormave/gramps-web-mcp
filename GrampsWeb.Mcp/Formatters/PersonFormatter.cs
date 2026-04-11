using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Formats person API responses and related relationship output.
/// </summary>
public static class PersonFormatter
{
    private static readonly string[] GenderLabels = { "Female", "Male", "Unknown" };

    public static async Task<string> FormatPersonSummary(GrampsPerson person, GrampsApiClient client)
    {
        if (person == null)
            return "Unknown person";

        var parts = new List<string>();

        var primaryName = person.PrimaryName;
        if (primaryName != null)
            parts.Add(GrampsValueFormatter.FormatName(primaryName));
        else
            parts.Add("Unknown");

        var birthInfo = await ExtractEventInfo(person, "Birth", client);
        var deathInfo = await ExtractEventInfo(person, "Death", client);

        if (!string.IsNullOrEmpty(birthInfo))
            parts.Add($"b. {birthInfo}");

        if (!string.IsNullOrEmpty(deathInfo))
            parts.Add($"d. {deathInfo}");

        var summary = $"{string.Join(", ", parts)}";
        if (!string.IsNullOrEmpty(person.GrampsId))
            summary += $" [{person.GrampsId}]";

        return summary;
    }

    public static async Task<string?> ExtractEventInfo(GrampsPerson person, string eventType, GrampsApiClient client)
    {
        if (person.EventRefList == null || person.EventRefList.Length == 0)
            return null;

        foreach (var eventRef in person.EventRefList.Where(e => e.Ref != null))
        {
            try
            {
                var eventObj = await client.GetAsync<GrampsEvent>($"/api/events/{eventRef.Ref}");
                if (eventObj?.Type != eventType)
                    continue;

                var parts = new List<string>();

                if (eventObj.Date != null)
                    parts.Add(GrampsValueFormatter.FormatDate(eventObj.Date));

                if (!string.IsNullOrEmpty(eventObj.Place))
                {
                    try
                    {
                        var place = await client.GetAsync<GrampsPlace>($"/api/places/{eventObj.Place}");
                        if (place != null)
                            parts.Add(GrampsValueFormatter.FormatPlace(place));
                    }
                    catch { }
                }

                return string.Join(", ", parts);
            }
            catch { }
        }

        return null;
    }

    public static async Task<string> FormatPersonFull(GrampsPerson person, GrampsApiClient client)
    {
        IReadOnlyList<string>? nameTypeLabels = null;
        if (person.AlternateNames is { Length: > 0 })
            nameTypeLabels = await GrampsDefaultTypeLabels.LoadNameTypeLabelsAsync(client).ConfigureAwait(false);

        var sb = new StringBuilder();
        var name = person.PrimaryName;
        var displayName = name != null ? GrampsValueFormatter.FormatName(name) : "Unknown";

        sb.AppendLine($"PERSON: {displayName} [handle: {person.Handle}] (gramps_id: {person.GrampsId})");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"Gender:    {GenderLabels[Math.Clamp(person.Gender, 0, 2)]}");

        var birthInfo = await ExtractEventInfo(person, "Birth", client);
        var deathInfo = await ExtractEventInfo(person, "Death", client);
        if (birthInfo != null) sb.AppendLine($"Birth:     {birthInfo}");
        if (deathInfo != null) sb.AppendLine($"Death:     {deathInfo}");

        sb.AppendLine();

        if (person.FamilyList?.Length > 0)
        {
            sb.AppendLine($"Families (as parent/spouse): {person.FamilyList.Length}");
            foreach (var fh in person.FamilyList)
                sb.AppendLine($"  • [handle: {fh}]");
        }

        if (person.ParentFamilyList?.Length > 0)
        {
            sb.AppendLine($"Parent families (as child): {person.ParentFamilyList.Length}");
            foreach (var pf in person.ParentFamilyList)
                sb.AppendLine($"  • [handle: {pf.Ref}]");
        }

        if (person.EventRefList?.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Events ({person.EventRefList.Length}):");
            foreach (var er in person.EventRefList)
                sb.AppendLine($"  • [handle: {er.Ref}] role: {er.Role ?? "Primary"}");
        }

        if (person.NoteList?.Length > 0)     sb.AppendLine($"Notes:     {person.NoteList.Length}");
        if (person.CitationList?.Length > 0) sb.AppendLine($"Citations: {person.CitationList.Length}");
        if (person.TagList?.Length > 0)      sb.AppendLine($"Tags:      {string.Join(", ", person.TagList)}");
        if (person.Private)                  sb.AppendLine("⚠ Private record");

        if (person.AlternateNames is { Length: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Alternate names:");
            foreach (var n in person.AlternateNames)
            {
                var typeLabel = GrampsDefaultTypeLabels.ResolveStored(n.Type, nameTypeLabels);
                sb.AppendLine($"  • {GrampsValueFormatter.FormatName(n)} ({typeLabel})");
            }
        }

        return sb.ToString();
    }

    public static async Task<string> FormatPersonExtended(GrampsPersonExtended person, GrampsApiClient client)
    {
        var tables = await GrampsDefaultTypeLabels.PrefetchAllAsync(client).ConfigureAwait(false);
        var sb = new StringBuilder();
        var name = person.PrimaryName;
        var displayName = name != null ? GrampsValueFormatter.FormatName(name) : "Unknown";

        sb.AppendLine($"PERSON (extended): {displayName}");
        sb.AppendLine($"Handle: {person.Handle}  |  Gramps ID: {person.GrampsId}  |  " +
                      $"Gender: {GenderLabels[Math.Clamp(person.Gender, 0, 2)]}");
        sb.AppendLine(new string('=', 60));

        var extEvents = person.Extended?.Events;
        if (extEvents?.Length > 0)
        {
            sb.AppendLine("\nEVENTS:");
            foreach (var evt in extEvents)
            {
                var dateStr = evt.Date != null ? GrampsValueFormatter.FormatDate(evt.Date) : "—";
                var placeStr = "";
                if (!string.IsNullOrEmpty(evt.Place))
                {
                    try
                    {
                        var place = await client.GetAsync<GrampsPlace>($"/api/places/{evt.Place}");
                        if (place != null) placeStr = $" — {GrampsValueFormatter.FormatPlace(place)}";
                    }
                    catch { }
                }
                var role = person.EventRefList?.FirstOrDefault(er => er.Ref == evt.Handle)?.Role ?? "Primary";
                var evtTypeLabel = GrampsDefaultTypeLabels.ResolveStored(evt.Type, tables.EventTypes);
                sb.AppendLine($"  • {evtTypeLabel}: {dateStr}{placeStr} [{role}]");
                if (!string.IsNullOrEmpty(evt.Description))
                    sb.AppendLine($"    {evt.Description}");
            }
        }

        var extFamilies = person.Extended?.Families;
        if (extFamilies?.Length > 0)
        {
            sb.AppendLine("\nFAMILIES (as parent/spouse):");
            foreach (var fam in extFamilies)
            {
                var spouseHandle = fam.FatherHandle == person.Handle ? fam.MotherHandle : fam.FatherHandle;
                var childCount = fam.ChildRefList?.Length ?? 0;
                var relLabel = string.IsNullOrWhiteSpace(fam.Relationship)
                    ? "Married"
                    : GrampsDefaultTypeLabels.ResolveStored(fam.Relationship.Trim(), tables.FamilyRelationTypes);
                if (relLabel == "—")
                    relLabel = "Married";
                sb.AppendLine($"  • [{relLabel}] [handle: {fam.Handle}] " +
                              $"— spouse: {spouseHandle ?? "—"}, children: {childCount}");
            }
        }

        var extParentFamilies = person.Extended?.ParentFamilies;
        if (extParentFamilies?.Length > 0)
        {
            sb.AppendLine("\nPARENT FAMILIES (as child):");
            foreach (var fam in extParentFamilies)
                sb.AppendLine($"  • [handle: {fam.Handle}] " +
                              $"father: {fam.FatherHandle ?? "—"}, mother: {fam.MotherHandle ?? "—"}");
        }

        var extNotes = person.Extended?.Notes;
        if (extNotes?.Length > 0)
        {
            sb.AppendLine("\nNOTES:");
            foreach (var note in extNotes)
            {
                var snippet = note.Text?.Replace('\n', ' ').Trim() ?? "";
                if (snippet.Length > 100) snippet = snippet[..100] + "…";
                var noteTypeLabel = string.IsNullOrWhiteSpace(note.Type)
                    ? "General"
                    : GrampsDefaultTypeLabels.ResolveStored(note.Type.Trim(), tables.NoteTypes);
                sb.AppendLine($"  • [{noteTypeLabel}] {snippet}");
            }
        }

        var extTags = person.Extended?.Tags;
        if (extTags?.Length > 0)
            sb.AppendLine($"\nTAGS: {string.Join(", ", extTags.Select(t => t.Name))}");

        if (person.AlternateNames is { Length: > 0 })
        {
            sb.AppendLine("\nALTERNATE NAMES:");
            foreach (var n in person.AlternateNames)
            {
                var typeLabel = GrampsDefaultTypeLabels.ResolveStored(n.Type, tables.NameTypes);
                sb.AppendLine($"  • {GrampsValueFormatter.FormatName(n)} ({typeLabel})");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats ancestor or descendant tool output with generation index and optional kinship labels.
    /// </summary>
    public static async Task<string> FormatPersonTreeRows(
        string title,
        string rootHandle,
        IReadOnlyList<PersonTreeRow> rows,
        bool kinshipLabels,
        GrampsApiClient client)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{title} [root: {rootHandle}]");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"Total: {rows.Count}");
        sb.AppendLine();

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var summary = await FormatPersonSummary(row.Person, client);
            var genPart = $"Gen {row.Generation}";

            string headLine;
            if (kinshipLabels)
            {
                if (row.AncestorPathFromRoot is { Count: > 0 } ancestorPath)
                    headLine = $"{genPart} — {KinshipLabels.AncestorChainLabel(ancestorPath)}";
                else if (row.AncestorPathFromRoot is not null)
                    headLine = genPart;
                else
                    headLine =
                        $"{genPart} — {KinshipLabels.DescendantKinshipLabel(row.Generation, row.Person.Gender)}";
            }
            else
                headLine = genPart;

            sb.AppendLine($"  {i + 1}. {headLine}");
            sb.AppendLine($"     {summary}");
            sb.AppendLine($"     [handle: {row.Person.Handle}] (gramps_id: {row.Person.GrampsId})");
        }

        return sb.ToString();
    }

    public static async Task<string> FormatPersonList(
        string title, string rootHandle, GrampsPerson[] people, GrampsApiClient client)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{title} [root: {rootHandle}]");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"Total: {people.Length}");
        sb.AppendLine();

        for (int i = 0; i < people.Length; i++)
        {
            var summary = await FormatPersonSummary(people[i], client);
            sb.AppendLine($"  {i + 1}. {summary}");
            sb.AppendLine($"     [handle: {people[i].Handle}] (gramps_id: {people[i].GrampsId})");
        }

        return sb.ToString();
    }

    public static string FormatRelationships(string handle1, string handle2, JsonElement data)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"RELATIONSHIP: {handle1} ↔ {handle2}");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine(JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        return sb.ToString();
    }
}
