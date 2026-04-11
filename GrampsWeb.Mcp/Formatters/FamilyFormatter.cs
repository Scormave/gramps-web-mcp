using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Formats family API responses.
/// </summary>
public static class FamilyFormatter
{
    public static async Task<string> FormatFamilySummary(GrampsFamily family, GrampsApiClient client)
    {
        if (family == null)
            return "Unknown family";

        var sb = new StringBuilder();

        var relType = string.IsNullOrWhiteSpace(family.Relationship)
            ? "Unknown"
            : await GrampsDefaultTypeLabels.FormatFamilyRelationTypeAsync(client, family.Relationship);
        sb.AppendLine($"Relationship: {relType}");

        if (!string.IsNullOrEmpty(family.FatherHandle))
        {
            try
            {
                var father = await client.GetAsync<GrampsPerson>($"/api/people/{family.FatherHandle}");
                if (father != null)
                {
                    var fatherName = father.PrimaryName;
                    var fatherBirth = await PersonFormatter.ExtractEventInfo(father, "Birth", client);
                    var fatherStr = GrampsValueFormatter.FormatName(fatherName);
                    sb.AppendLine($"Father: {fatherStr} ({(fatherBirth != null ? $"b. {fatherBirth}" : "")})".Trim());
                }
            }
            catch { }
        }

        if (!string.IsNullOrEmpty(family.MotherHandle))
        {
            try
            {
                var mother = await client.GetAsync<GrampsPerson>($"/api/people/{family.MotherHandle}");
                if (mother != null)
                {
                    var motherName = mother.PrimaryName;
                    var motherBirth = await PersonFormatter.ExtractEventInfo(mother, "Birth", client);
                    var motherStr = GrampsValueFormatter.FormatName(motherName);
                    sb.AppendLine($"Mother: {motherStr} ({(motherBirth != null ? $"b. {motherBirth}" : "")})".Trim());
                }
            }
            catch { }
        }

        if (family.ChildRefList != null && family.ChildRefList.Length > 0)
        {
            sb.AppendLine($"\nChildren ({family.ChildRefList.Length}):");
            foreach (var childRef in family.ChildRefList)
            {
                if (string.IsNullOrEmpty(childRef.Ref))
                    continue;

                try
                {
                    var child = await client.GetAsync<GrampsPerson>($"/api/people/{childRef.Ref}");
                    if (child != null)
                    {
                        var childName = child.PrimaryName;
                        var childBirth = await PersonFormatter.ExtractEventInfo(child, "Birth", client);
                        var childStr = GrampsValueFormatter.FormatName(childName);
                        var relTypeStr = !string.IsNullOrEmpty(childRef.FatherRelType) ? $" ({childRef.FatherRelType})" : "";
                        sb.AppendLine($"  • {childStr}{relTypeStr} {(childBirth != null ? $"b. {childBirth}" : "")}".Trim());
                    }
                }
                catch { }
            }
        }

        return sb.ToString();
    }

    public static async Task<string> FormatFamilyFullAsync(GrampsFamily family, GrampsApiClient client)
    {
        var relLabel = string.IsNullOrWhiteSpace(family.Relationship)
            ? "Unknown"
            : await GrampsDefaultTypeLabels.FormatFamilyRelationTypeAsync(client, family.Relationship);
        var sb = new StringBuilder();
        sb.AppendLine($"FAMILY [handle: {family.Handle}] (gramps_id: {family.GrampsId})");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"Relationship: {relLabel}");
        sb.AppendLine();

        sb.AppendLine($"Father: {family.FatherHandle ?? "—"}");
        sb.AppendLine($"Mother: {family.MotherHandle ?? "—"}");

        if (family.ChildRefList?.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Children ({family.ChildRefList.Length}):");
            foreach (var child in family.ChildRefList)
            {
                var frel = child.FatherRelType ?? "Birth";
                var mrel = child.MotherRelType ?? "Birth";
                sb.AppendLine($"  • [handle: {child.Ref}] frel: {frel}, mrel: {mrel}");
            }
        }
        else
        {
            sb.AppendLine("Children: none");
        }

        if (family.EventRefList?.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Events ({family.EventRefList.Length}):");
            foreach (var er in family.EventRefList)
                sb.AppendLine($"  • [handle: {er.Ref}] role: {er.Role ?? "Primary"}");
        }

        HandleListFormatter.AppendHandleBulletSection(sb, "Notes", family.NoteList);
        HandleListFormatter.AppendHandleBulletSection(sb, "Citations", family.CitationList);
        HandleListFormatter.AppendHandleBulletSection(sb, "Media", family.MediaList);
        HandleListFormatter.AppendHandleBulletSection(sb, "Tags", family.TagList);
        if (family.Private)                  sb.AppendLine("⚠ Private record");

        return sb.ToString();
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
        sb.AppendLine($"Relationship: {relLabel}");
        sb.AppendLine();

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
                sb.AppendLine($"  • {summary} [handle: {child.Handle}] (frel: {frel}, mrel: {mrel})");
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
                sb.AppendLine($"  • [handle: {child.Ref}] frel: {frel}, mrel: {mrel}");
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
                if (!string.IsNullOrEmpty(evt.Place))
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

        var extCitations = family.Extended?.Citations;
        if (extCitations?.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Citations ({extCitations.Length}):");
            foreach (var c in extCitations)
            {
                var pagePart = string.IsNullOrWhiteSpace(c.Page) ? "" : $"p. {c.Page.Trim()} — ";
                var ch = string.IsNullOrWhiteSpace(c.Handle) ? "—" : c.Handle.Trim();
                var sh = string.IsNullOrWhiteSpace(c.Source) ? "" : $" source [handle: {c.Source.Trim()}]";
                sb.AppendLine($"  • {pagePart}[handle: {ch}]{sh}");
            }
        }

        HandleListFormatter.AppendHandleBulletSection(sb, "Media", family.MediaList);

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

        if (family.Private) sb.AppendLine("⚠ Private record");

        return sb.ToString();
    }
}
