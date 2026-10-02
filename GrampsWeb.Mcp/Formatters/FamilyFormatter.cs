using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Formats family API responses.
/// </summary>
public static class FamilyFormatter
{
    /// <summary>
    /// Family card. Names, life dates and event summaries come from <see cref="GrampsFamily.Profile"/>
    /// (<c>?profile=self,events</c>) when present; otherwise members and events are shown by handle.
    /// Tags, media, notes and citations are named by <see cref="LinkedObjectLabels"/>.
    /// </summary>
    public static async Task<string> FormatFamilyFullAsync(GrampsFamily family, GrampsApiClient client)
    {
        var labelsTask = LinkedObjectLabels.LoadAsync(client,
        [
            ("tags", family.TagList),
            ("media", GrampsMediaRef.ToHandleStrings(family.MediaList)),
            ("notes", family.NoteList),
            ("citations", family.CitationList),
        ]);
        var relLabel = string.IsNullOrWhiteSpace(family.Relationship)
            ? "Unknown"
            : await GrampsDefaultTypeLabels.FormatFamilyRelationTypeAsync(client, family.Relationship);
        var sb = new StringBuilder();
        sb.AppendLine($"FAMILY [handle: {family.Handle}] (gramps_id: {family.GrampsId})");
        sb.AppendLine(new string('=', 60));

        var profile = family.Profile;
        AppendParentLine(sb, "Father", family.FatherHandle, profile?.Father);
        AppendParentLine(sb, "Mother", family.MotherHandle, profile?.Mother);
        sb.AppendLine($"Relationship: {relLabel}");
        if (PersonFormatter.FormatProfileEvent(profile?.Marriage) is { } marriage)
            sb.AppendLine($"Marriage: {marriage}");
        if (PersonFormatter.FormatProfileEvent(profile?.Divorce) is { } divorce)
            sb.AppendLine($"Divorce: {divorce}");

        var labels = await labelsTask;
        HandleListFormatter.AppendHandleBulletSection(sb, "Tags", family.TagList, labels);

        if (family.ChildRefList?.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Children ({family.ChildRefList.Length}):");
            for (var i = 0; i < family.ChildRefList.Length; i++)
            {
                var child = family.ChildRefList[i];
                var frel = child.FatherRelType ?? "Birth";
                var mrel = child.MotherRelType ?? "Birth";
                var who = FindProfile(profile?.Children, i, child.Ref) is { } childProfile
                    ? PersonFormatter.FormatProfilePerson(childProfile, child.Ref!)
                    : $"[handle: {child.Ref}]";
                var line = $"  • {who} | frel: {frel}, mrel: {mrel}";
                if (child.Private)
                    line += " ⚠ private (child link)";
                if (child.TagList is { Length: > 0 } ctags)
                {
                    var th = string.Join(", ", ctags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()));
                    if (!string.IsNullOrEmpty(th))
                        line += $" | tags on link: {th}";
                }
                sb.AppendLine(line);
            }
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("Children: none");
        }

        if (family.EventRefList?.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Events ({family.EventRefList.Length}):");
            // Profile events line up with event_ref_list (missing events are sent as empty objects).
            var eventProfiles = profile?.Events?.Length == family.EventRefList.Length ? profile.Events : null;
            for (var i = 0; i < family.EventRefList.Length; i++)
            {
                var er = family.EventRefList[i];
                var summary = PersonFormatter.FormatProfileEvent(eventProfiles?[i], withType: true);
                var line = summary != null
                    ? $"  • {summary} [handle: {er.Ref}] role: {er.Role ?? "Primary"}"
                    : $"  • [handle: {er.Ref}] role: {er.Role ?? "Primary"}";
                if (er.NoteList is { Length: > 0 })
                    line += $" | note refs: {er.NoteList.Length}";
                if (er.AttributeList is { Length: > 0 })
                    line += $" | nested attributes: {er.AttributeList.Length}";
                sb.AppendLine(line);
            }
        }

        HandleListFormatter.AppendHandleBulletSection(sb, "Gallery (media)", GrampsMediaRef.ToHandleStrings(family.MediaList), labels);
        HandleListFormatter.AppendHandleBulletSection(sb, "Notes", family.NoteList, labels);
        HandleListFormatter.AppendHandleBulletSection(sb, "Sources (citations)", family.CitationList, labels);

        AttributeListFormatter.AppendSection(sb, family.AttributeList);

        if (family.Private)
            sb.AppendLine("⚠ Private record");

        return sb.ToString();
    }

    private static void AppendParentLine(StringBuilder sb, string label, string? handle, GrampsPersonProfile? profile)
    {
        if (string.IsNullOrWhiteSpace(handle))
            return;
        var who = !string.IsNullOrWhiteSpace(profile?.NameDisplay)
            ? PersonFormatter.FormatProfilePerson(profile, handle)
            : $"[handle: {handle.Trim()}]";
        sb.AppendLine($"{label}: {who}");
    }

    /// <summary>Profile children line up with child_ref_list; the handle confirms the match.</summary>
    internal static GrampsPersonProfile? FindProfile(GrampsPersonProfile[]? profiles, int index, string? handle)
    {
        if (profiles == null || string.IsNullOrWhiteSpace(handle))
            return null;
        var match = index < profiles.Length && profiles[index].Handle == handle
            ? profiles[index]
            : profiles.FirstOrDefault(p => p.Handle == handle);
        return string.IsNullOrWhiteSpace(match?.NameDisplay) ? null : match;
    }

    public static async Task<string> FormatFamilyExtended(GrampsFamilyExtended family, GrampsApiClient client)
    {
        var tables = await GrampsDefaultTypeLabels.PrefetchAllAsync(client);
        var relLabel = string.IsNullOrWhiteSpace(family.Relationship)
            ? "Unknown"
            : GrampsDefaultTypeLabels.ResolveStored(family.Relationship.Trim(), tables.FamilyRelationTypes);
        if (relLabel == "—")
            relLabel = "Unknown";

        var sb = new StringBuilder();
        sb.AppendLine($"Family (extended) [handle: {family.Handle}] (gramps_id: {family.GrampsId})");
        sb.AppendLine(new string('=', 60));

        var father = family.Extended?.Father;
        if (father != null)
        {
            var summary = await PersonFormatter.FormatPersonSummary(father, client);
            sb.AppendLine($"Father: {summary} [handle: {father.Handle}]");
        }
        else if (!string.IsNullOrEmpty(family.FatherHandle))
        {
            sb.AppendLine($"Father: [handle: {family.FatherHandle}]");
        }

        var mother = family.Extended?.Mother;
        if (mother != null)
        {
            var summary = await PersonFormatter.FormatPersonSummary(mother, client);
            sb.AppendLine($"Mother: {summary} [handle: {mother.Handle}]");
        }
        else if (!string.IsNullOrEmpty(family.MotherHandle))
        {
            sb.AppendLine($"Mother: [handle: {family.MotherHandle}]");
        }

        sb.AppendLine($"Relationship: {relLabel}");

        var extTags = family.Extended?.Tags;
        if (extTags?.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Tags ({extTags.Length}):");
            foreach (var t in extTags)
            {
                var label = string.IsNullOrWhiteSpace(t.Name) ? "Tag" : t.Name.Trim();
                var th = string.IsNullOrWhiteSpace(t.Handle) ? "—" : t.Handle.Trim();
                sb.AppendLine($"  • {label} [handle: {th}]");
            }
        }
        else
            HandleListFormatter.AppendHandleBulletSection(sb, "Tags", family.TagList);

        var children = family.Extended?.Children;
        if (children?.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Children ({children.Length}):");
            foreach (var child in children)
            {
                var summary = await PersonFormatter.FormatPersonSummary(child, client);
                var childRef = family.ChildRefList?.FirstOrDefault(cr => cr.Ref == child.Handle);
                var frel = childRef?.FatherRelType ?? "Birth";
                var mrel = childRef?.MotherRelType ?? "Birth";
                var line = $"  • {summary} [handle: {child.Handle}] (frel: {frel}, mrel: {mrel})";
                if (childRef?.Private == true)
                    line += " ⚠ private (child link)";
                if (childRef?.TagList is { Length: > 0 } xtags)
                {
                    var th = string.Join(", ", xtags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()));
                    if (!string.IsNullOrEmpty(th))
                        line += $" | tags on link: {th}";
                }
                sb.AppendLine(line);
            }
        }
        else if (family.ChildRefList?.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Children ({family.ChildRefList.Length}):");
            foreach (var child in family.ChildRefList)
            {
                var frel = child.FatherRelType ?? "Birth";
                var mrel = child.MotherRelType ?? "Birth";
                var line = $"  • [handle: {child.Ref}] frel: {frel}, mrel: {mrel}";
                if (child.Private)
                    line += " ⚠ private (child link)";
                if (child.TagList is { Length: > 0 } ctags)
                {
                    var th = string.Join(", ", ctags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()));
                    if (!string.IsNullOrEmpty(th))
                        line += $" | tags on link: {th}";
                }
                sb.AppendLine(line);
            }
        }
        else
        {
            sb.AppendLine("Children: none");
        }

        var extEvents = family.Extended?.Events;
        if (extEvents?.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Events ({extEvents.Length}):");
            foreach (var evt in extEvents)
            {
                var dateStr = evt.Date != null ? GrampsValueFormatter.FormatDate(evt.Date) : "—";
                var placeStr = "";
                if (evt is GrampsEventExtended { Extended.Place: { } embedded })
                {
                    var pl = GrampsValueFormatter.FormatPlace(embedded);
                    if (!string.IsNullOrEmpty(pl) && pl != "Unknown place")
                        placeStr = $" — {pl}";
                }
                else if (!string.IsNullOrEmpty(evt.Place))
                {
                    try
                    {
                        var place = await client.GetAsync<GrampsPlace>($"/api/places/{evt.Place}");
                        if (place != null) placeStr = $" — {GrampsValueFormatter.FormatPlace(place)}";
                    }
                    catch { }
                }
                var role = family.EventRefList?.FirstOrDefault(er => er.Ref == evt.Handle)?.Role ?? "Primary";
                var evtTypeLabel = GrampsDefaultTypeLabels.ResolveStored(evt.Type, tables.EventTypes);
                var evtHandleSuffix = string.IsNullOrWhiteSpace(evt.Handle)
                    ? ""
                    : $" [handle: {evt.Handle.Trim()}]";
                var placeHandleSuffix = string.IsNullOrWhiteSpace(evt.Place)
                    ? ""
                    : $" [place: {evt.Place.Trim()}]";
                sb.AppendLine($"  • {evtTypeLabel}: {dateStr}{placeStr} [{role}]{placeHandleSuffix}{evtHandleSuffix}");
            }
        }
        else if (family.EventRefList?.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Events ({family.EventRefList.Length}) (handles only — extended.events empty):");
            foreach (var er in family.EventRefList)
            {
                var line = $"  • [handle: {er.Ref}] role: {er.Role ?? "Primary"}";
                if (er.NoteList is { Length: > 0 })
                    line += $" | note refs: {er.NoteList.Length}";
                if (er.AttributeList is { Length: > 0 })
                    line += $" | nested attributes: {er.AttributeList.Length}";
                sb.AppendLine(line);
            }
        }

        MediaFormatter.AppendExtendedMediaSection(sb, family.Extended?.Media, GrampsMediaRef.ToHandleStrings(family.MediaList));

        var extNotes = family.Extended?.Notes;
        if (extNotes?.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Notes ({extNotes.Length}):");
            foreach (var note in extNotes)
            {
                var snippet = note.Text?.Replace('\n', ' ').Trim() ?? "";
                if (snippet.Length > 100) snippet = snippet[..100] + "…";
                var noteTypeLabel = string.IsNullOrWhiteSpace(note.Type)
                    ? "General"
                    : GrampsDefaultTypeLabels.ResolveStored(note.Type.Trim(), tables.NoteTypes);
                var nh = string.IsNullOrWhiteSpace(note.Handle) ? "—" : note.Handle.Trim();
                sb.AppendLine($"  • [{noteTypeLabel}] {snippet} [handle: {nh}]");
            }
        }
        else
            HandleListFormatter.AppendHandleBulletSection(sb, "Notes", family.NoteList);

        var extCitations = family.Extended?.Citations;
        if (extCitations?.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Sources (citations) ({extCitations.Length}):");
            foreach (var c in extCitations)
                sb.AppendLine(CitationFormatter.FormatEmbeddedCitationExtendedLine(c));
        }
        else
            HandleListFormatter.AppendHandleBulletSection(sb, "Sources (citations)", family.CitationList);

        AttributeListFormatter.AppendSection(sb, family.AttributeList);

        if (family.Private) sb.AppendLine("⚠ Private record");

        return sb.ToString();
    }
}
