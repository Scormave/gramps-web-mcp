using System.Text;
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

    /// <summary>
    /// Date/place text for the first matching vital event, preferring <see cref="GrampsPerson.BirthRefIndex"/> /
    /// <see cref="GrampsPerson.DeathRefIndex"/> when set (same logic as person detail headers, without <c>[event: …]</c> suffix).
    /// </summary>
    public static async Task<string?> ExtractEventInfo(GrampsPerson person, string eventType, GrampsApiClient client)
    {
        var death = eventType switch
        {
            "Birth" => false,
            "Death" => true,
            _ => (bool?)null
        };
        if (death is null)
            return null;

        var r = await ResolveBirthOrDeathAsync(person, death.Value, client, preloadedExtendedEvents: null)
            .ConfigureAwait(false);
        return r.DatePlaceSummary;
    }

    public static async Task<string> FormatPersonFull(GrampsPerson person, GrampsApiClient client)
    {
        var nameTypeLabelsTask = GrampsDefaultTypeLabels.LoadNameTypeLabelsAsync(client);
        var nameOriginTypeLabelsTask = GrampsDefaultTypeLabels.LoadNameOriginTypeLabelsAsync(client);
        await Task.WhenAll(nameTypeLabelsTask, nameOriginTypeLabelsTask).ConfigureAwait(false);
        var nameTypeLabels = await nameTypeLabelsTask.ConfigureAwait(false);
        var nameOriginTypeLabels = await nameOriginTypeLabelsTask.ConfigureAwait(false);

        var sb = new StringBuilder();
        var name = person.PrimaryName;
        var displayName = name != null ? GrampsValueFormatter.FormatName(name) : "Unknown";

        sb.AppendLine($"PERSON: {displayName} [handle: {person.Handle}] (gramps_id: {person.GrampsId})");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"Gender:    {GenderLabels[Math.Clamp(person.Gender, 0, 2)]}");

        if (name != null)
        {
            var nameTypeLabel = GrampsDefaultTypeLabels.ResolveStored(name.Type, nameTypeLabels);
            sb.AppendLine($"Primary name [{nameTypeLabel}]: {displayName}");
            sb.AppendLine(GrampsValueFormatter.FormatNameDetailed(name, originTypeLabels: nameOriginTypeLabels));
        }

        await AppendBirthDeathHeaderLinesAsync(sb, person, client, preloadedExtendedEvents: null).ConfigureAwait(false);

        HandleListFormatter.AppendHandleBulletSection(sb, "Tags", person.TagList);
        AppendPersonRelationshipsHandleSections(sb, person);

        if (person.EventRefList?.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Events ({person.EventRefList.Length}):");
            foreach (var er in person.EventRefList)
                sb.AppendLine($"  • [handle: {er.Ref}] role: {er.Role ?? "Primary"}");
        }

        HandleListFormatter.AppendHandleBulletSection(sb, "Gallery (media)", GrampsMediaRef.ToHandleStrings(person.MediaList));

        if (person.AlternateNames is { Length: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine($"Alternate names ({person.AlternateNames.Length}):");
            foreach (var n in person.AlternateNames)
            {
                var typeLabel = GrampsDefaultTypeLabels.ResolveStored(n.Type, nameTypeLabels);
                sb.AppendLine($"  • [{typeLabel}] {GrampsValueFormatter.FormatName(n)}");
                sb.AppendLine(GrampsValueFormatter.FormatNameDetailed(n, originTypeLabels: nameOriginTypeLabels));
            }
        }

        HandleListFormatter.AppendHandleBulletSection(sb, "Notes", person.NoteList);
        HandleListFormatter.AppendHandleBulletSection(sb, "Sources (citations)", person.CitationList);

        AppendMetadataSectionsForPerson(sb, person);
        AppendPersonAssociationsSection(sb, person.PersonRefList);

        if (person.Private)
            sb.AppendLine("⚠ Private record");

        return sb.ToString();
    }

    public static async Task<string> FormatPersonExtended(GrampsPersonExtended person, GrampsApiClient client)
    {
        var tables = await GrampsDefaultTypeLabels.PrefetchAllAsync(client).ConfigureAwait(false);
        var sb = new StringBuilder();
        var name = person.PrimaryName;
        var displayName = name != null ? GrampsValueFormatter.FormatName(name) : "Unknown";

        sb.AppendLine($"Person (extended): {displayName} [handle: {person.Handle}] (gramps_id: {person.GrampsId})");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"Gender:    {GenderLabels[Math.Clamp(person.Gender, 0, 2)]}");

        if (name != null)
        {
            var nameTypeLabel = GrampsDefaultTypeLabels.ResolveStored(name.Type, tables.NameTypes);
            sb.AppendLine($"Primary name [{nameTypeLabel}]: {displayName}");
            sb.AppendLine(GrampsValueFormatter.FormatNameDetailed(name, originTypeLabels: tables.NameOriginTypes));
        }

        await AppendBirthDeathHeaderLinesAsync(sb, person, client, person.Extended?.Events).ConfigureAwait(false);

        var extTags = person.Extended?.Tags;
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

        var extParentFamilies = person.Extended?.ParentFamilies;
        var extFamilies = person.Extended?.Families;
        if (extParentFamilies is { Length: > 0 } || extFamilies is { Length: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Relationships:");
        }

        if (extParentFamilies is { Length: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine($"  Parent families (as child) ({extParentFamilies.Length}):");
            foreach (var fam in extParentFamilies)
            {
                var fh = string.IsNullOrWhiteSpace(fam.FatherHandle) ? "—" : fam.FatherHandle.Trim();
                var mh = string.IsNullOrWhiteSpace(fam.MotherHandle) ? "—" : fam.MotherHandle.Trim();
                sb.AppendLine($"  • [handle: {fam.Handle}] — father [handle: {fh}], mother [handle: {mh}]");
            }
        }

        if (extFamilies is { Length: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine($"  Families as parent or spouse ({extFamilies.Length}):");
            foreach (var fam in extFamilies)
            {
                var spouseHandle = fam.FatherHandle == person.Handle ? fam.MotherHandle : fam.FatherHandle;
                var childCount = fam.ChildRefList?.Length ?? 0;
                var relLabel = string.IsNullOrWhiteSpace(fam.Relationship)
                    ? "Married"
                    : GrampsDefaultTypeLabels.ResolveStored(fam.Relationship.Trim(), tables.FamilyRelationTypes);
                if (relLabel == "—")
                    relLabel = "Married";
                var spousePart = string.IsNullOrWhiteSpace(spouseHandle)
                    ? "spouse: —"
                    : $"spouse [handle: {spouseHandle.Trim()}]";
                sb.AppendLine($"  • [{relLabel}] [handle: {fam.Handle}] — {spousePart}, children: {childCount}");
            }
        }

        var extEvents = person.Extended?.Events;
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
                var role = person.EventRefList?.FirstOrDefault(er => er.Ref == evt.Handle)?.Role ?? "Primary";
                var evtTypeLabel = GrampsDefaultTypeLabels.ResolveStored(evt.Type, tables.EventTypes);
                var evtHandleSuffix = string.IsNullOrWhiteSpace(evt.Handle)
                    ? ""
                    : $" [handle: {evt.Handle.Trim()}]";
                var placeHandleSuffix = string.IsNullOrWhiteSpace(evt.Place)
                    ? ""
                    : $" [place: {evt.Place.Trim()}]";
                sb.AppendLine($"  • {evtTypeLabel}: {dateStr}{placeStr} [{role}]{placeHandleSuffix}{evtHandleSuffix}");
                if (!string.IsNullOrEmpty(evt.Description))
                    sb.AppendLine($"    {evt.Description}");
            }
        }

        MediaFormatter.AppendExtendedMediaSection(sb, person.Extended?.Media, GrampsMediaRef.ToHandleStrings(person.MediaList));

        if (person.AlternateNames is { Length: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine($"Alternate names ({person.AlternateNames.Length}):");
            foreach (var n in person.AlternateNames)
            {
                var typeLabel = GrampsDefaultTypeLabels.ResolveStored(n.Type, tables.NameTypes);
                sb.AppendLine($"  • [{typeLabel}] {GrampsValueFormatter.FormatName(n)}");
                sb.AppendLine(GrampsValueFormatter.FormatNameDetailed(n, originTypeLabels: tables.NameOriginTypes));
            }
        }

        var extNotes = person.Extended?.Notes;
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

        var extCitations = person.Extended?.Citations;
        if (extCitations?.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Sources (citations) ({extCitations.Length}):");
            foreach (var c in extCitations)
                sb.AppendLine(CitationFormatter.FormatEmbeddedCitationExtendedLine(c));
        }

        AppendMetadataSectionsForPerson(sb, person);
        AppendPersonAssociationsSection(sb, person.PersonRefList);

        if (person.Private)
            sb.AppendLine("⚠ Private record");

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
            var summary = row.Person.Profile is { } profile
                ? FormatProfileSummary(profile, row.Person.GrampsId)
                : await FormatPersonSummary(row.Person, client);
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

    private readonly record struct BirthDeathHeaderParts(string? DatePlaceSummary, string? EventHandle);

    private static async Task AppendBirthDeathHeaderLinesAsync(
        StringBuilder sb,
        GrampsPerson person,
        GrampsApiClient client,
        IReadOnlyList<GrampsEventExtended>? preloadedExtendedEvents)
    {
        var birth = await ResolveBirthOrDeathAsync(person, death: false, client, preloadedExtendedEvents)
            .ConfigureAwait(false);
        AppendOneVitalLine(sb, "Birth", birth);
        var death = await ResolveBirthOrDeathAsync(person, death: true, client, preloadedExtendedEvents)
            .ConfigureAwait(false);
        AppendOneVitalLine(sb, "Death", death);
    }

    private static void AppendOneVitalLine(StringBuilder sb, string label, BirthDeathHeaderParts parts)
    {
        if (string.IsNullOrWhiteSpace(parts.EventHandle) && string.IsNullOrWhiteSpace(parts.DatePlaceSummary))
            return;
        var core = string.IsNullOrWhiteSpace(parts.DatePlaceSummary) ? "—" : parts.DatePlaceSummary.Trim();
        var line = string.IsNullOrWhiteSpace(parts.EventHandle)
            ? core
            : $"{core} [event: {parts.EventHandle.Trim()}]";
        sb.AppendLine($"{label}:     {line}");
    }

    /// <summary>
    /// Resolves birth or death using <see cref="GrampsPerson.BirthRefIndex"/> / <see cref="GrampsPerson.DeathRefIndex"/> first,
    /// then the first matching event in <paramref name="preloadedExtendedEvents"/> or <see cref="GrampsPerson.EventRefList"/>
    /// where the person has the Primary role (as in Gramps): a parent's link to a child's birth is not their own birth.
    /// </summary>
    private static async Task<BirthDeathHeaderParts> ResolveBirthOrDeathAsync(
        GrampsPerson person,
        bool death,
        GrampsApiClient client,
        IReadOnlyList<GrampsEventExtended>? preloadedExtendedEvents)
    {
        var type = death ? "Death" : "Birth";
        var refs = person.EventRefList;
        int? idx = death ? person.DeathRefIndex : person.BirthRefIndex;

        if (refs != null && idx is >= 0 && idx < refs.Length)
        {
            var h = refs[idx.Value].Ref;
            if (!string.IsNullOrWhiteSpace(h))
            {
                var trimmed = h.Trim();
                var embedded = FindEmbeddedExtendedEvent(preloadedExtendedEvents, trimmed);
                if (embedded != null)
                {
                    var summary = await FormatEventDatePlaceLineAsync(embedded, client).ConfigureAwait(false);
                    return new BirthDeathHeaderParts(summary, trimmed);
                }

                var fetched = await client.GetAsync<GrampsEvent>($"/api/events/{trimmed}").ConfigureAwait(false);
                if (fetched != null)
                {
                    var summary = await FormatEventDatePlaceLineAsync(fetched, client).ConfigureAwait(false);
                    return new BirthDeathHeaderParts(summary, trimmed);
                }
            }
        }

        if (preloadedExtendedEvents is { Count: > 0 })
        {
            foreach (var evt in preloadedExtendedEvents)
            {
                if (evt.Type != type || !HasPrimaryRole(refs, evt.Handle))
                    continue;
                var summary = await FormatEventDatePlaceLineAsync(evt, client).ConfigureAwait(false);
                var h = evt.Handle?.Trim();
                return new BirthDeathHeaderParts(summary, string.IsNullOrWhiteSpace(h) ? null : h);
            }
        }

        if (refs != null)
        {
            foreach (var eventRef in refs.Where(e => e.Ref != null && IsPrimaryRole(e.Role)))
            {
                try
                {
                    var eventObj = await client.GetAsync<GrampsEvent>($"/api/events/{eventRef.Ref}")
                        .ConfigureAwait(false);
                    if (eventObj?.Type != type)
                        continue;
                    var summary = await FormatEventDatePlaceLineAsync(eventObj, client).ConfigureAwait(false);
                    return new BirthDeathHeaderParts(summary, eventRef.Ref?.Trim());
                }
                catch
                {
                    // try next ref
                }
            }
        }

        return default;
    }

    /// <summary>
    /// <c>true</c> when <paramref name="eventHandle"/> has a Primary-role ref in <paramref name="refs"/>,
    /// or when there are no refs to check it against.
    /// </summary>
    private static bool HasPrimaryRole(GrampsEventRef[]? refs, string? eventHandle)
    {
        if (refs is not { Length: > 0 })
            return true;
        var handle = eventHandle?.Trim();
        return refs.Any(r =>
            string.Equals(r.Ref?.Trim(), handle, StringComparison.Ordinal) && IsPrimaryRole(r.Role));
    }

    /// <summary>Empty roles default to Primary; <c>1</c> is the numeric value of EventRoleType.PRIMARY.</summary>
    private static bool IsPrimaryRole(string? role) =>
        string.IsNullOrWhiteSpace(role) ||
        string.Equals(role.Trim(), "Primary", StringComparison.OrdinalIgnoreCase) ||
        role.Trim() == "1";

    private static GrampsEventExtended? FindEmbeddedExtendedEvent(
        IReadOnlyList<GrampsEventExtended>? events,
        string handle)
    {
        if (events == null)
            return null;
        foreach (var e in events)
        {
            if (string.Equals(e.Handle?.Trim(), handle, StringComparison.Ordinal))
                return e;
        }

        return null;
    }

    private static async Task<string?> FormatEventDatePlaceLineAsync(GrampsEvent evt, GrampsApiClient client)
    {
        var parts = new List<string>();
        if (evt.Date != null)
            parts.Add(GrampsValueFormatter.FormatDate(evt.Date));

        if (evt is GrampsEventExtended { Extended.Place: { } embedded })
        {
            var pl = GrampsValueFormatter.FormatPlace(embedded);
            if (!string.IsNullOrEmpty(pl) && pl != "Unknown place")
                parts.Add(pl);
        }
        else if (!string.IsNullOrEmpty(evt.Place))
        {
            try
            {
                var place = await client.GetAsync<GrampsPlace>($"/api/places/{evt.Place}").ConfigureAwait(false);
                if (place != null)
                    parts.Add(GrampsValueFormatter.FormatPlace(place));
            }
            catch
            {
                // omit place
            }
        }

        var s = string.Join(", ", parts);
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static void AppendPersonRelationshipsHandleSections(StringBuilder sb, GrampsPerson person)
    {
        var parentHandles = person.ParentFamilyList?
            .Select(pf => pf.Ref)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r!)
            .ToArray();
        var hasParents = parentHandles is { Length: > 0 };
        var hasSpouseFamilies = person.FamilyList?.Any(h => !string.IsNullOrWhiteSpace(h)) == true;
        if (!hasParents && !hasSpouseFamilies)
            return;

        sb.AppendLine();
        sb.AppendLine("Relationships:");
        if (hasParents)
            HandleListFormatter.AppendHandleBulletSection(sb, "  Parent families (as child)", parentHandles);
        if (hasSpouseFamilies)
            HandleListFormatter.AppendHandleBulletSection(sb, "  Families as parent or spouse", person.FamilyList);
    }

    private static void AppendMetadataSectionsForPerson(StringBuilder sb, GrampsPerson person)
    {
        AppendAttributesSection(sb, person.AttributeList);
        AppendAddressesSection(sb, person.AddressList);
        AppendUrlsSection(sb, person.UrlList);
    }

    private static void AppendAttributesSection(StringBuilder sb, GrampsAttribute[]? list) =>
        AttributeListFormatter.AppendSection(sb, list);

    private static void AppendAddressesSection(StringBuilder sb, GrampsAddress[]? list)
    {
        if (list is null || list.Length == 0)
            return;

        sb.AppendLine();
        sb.AppendLine($"Addresses ({list.Length}):");
        foreach (var a in list)
        {
            var parts = new List<string>();
            void add(string? s)
            {
                if (!string.IsNullOrWhiteSpace(s))
                    parts.Add(s.Trim());
            }

            add(a.Street);
            add(a.Locality);
            add(a.City);
            add(a.County);
            add(a.State);
            add(a.Postal);
            add(a.Country);

            var line = parts.Count > 0 ? string.Join(", ", parts) : "";
            if (string.IsNullOrEmpty(line) && string.IsNullOrWhiteSpace(a.Phone))
                line = "(empty address lines)";

            sb.AppendLine($"  • {line}");
            if (!string.IsNullOrWhiteSpace(a.Phone))
                sb.AppendLine($"    phone: {a.Phone.Trim()}");
            if (a.Date != null)
            {
                var ds = GrampsValueFormatter.FormatDate(a.Date);
                if (!string.IsNullOrWhiteSpace(ds))
                    sb.AppendLine($"    resident as of: {ds}");
            }

            if (a.Private)
                sb.AppendLine("    ⚠ private");
            AppendIndentedHandleListLine(sb, "    citations", a.CitationList);
            AppendIndentedHandleListLine(sb, "    notes", a.NoteList);
        }
    }

    private static void AppendUrlsSection(StringBuilder sb, GrampsUrl[]? list)
    {
        if (list is null || list.Length == 0)
            return;

        sb.AppendLine();
        sb.AppendLine($"URLs ({list.Length}):");
        foreach (var u in list)
        {
            var typ = string.IsNullOrWhiteSpace(u.Type) ? "URL" : u.Type.Trim();
            var path = string.IsNullOrWhiteSpace(u.Path) ? "—" : u.Path.Trim();
            var desc = string.IsNullOrWhiteSpace(u.Description) ? "" : $" — {u.Description.Trim()}";
            var priv = u.Private ? " ⚠ private" : "";
            sb.AppendLine($"  • [{typ}] {path}{desc}{priv}");
        }
    }

    private static void AppendPersonAssociationsSection(StringBuilder sb, GrampsPersonRef[]? list)
    {
        if (list is null || list.Length == 0)
            return;

        sb.AppendLine();
        sb.AppendLine($"Associations (relations) ({list.Length}):");
        foreach (var p in list)
        {
            var rh = string.IsNullOrWhiteSpace(p.Ref) ? "—" : p.Ref.Trim();
            var rel = string.IsNullOrWhiteSpace(p.Relationship) ? "—" : p.Relationship.Trim();
            var priv = p.Private ? " ⚠ private" : "";
            sb.AppendLine($"  • related person [handle: {rh}] — {rel}{priv}");
            AppendIndentedHandleListLine(sb, "    citations", p.CitationList);
            AppendIndentedHandleListLine(sb, "    notes", p.NoteList);
        }
    }

    private static void AppendIndentedHandleListLine(StringBuilder sb, string label, string[]? handles)
    {
        if (handles is null || handles.Length == 0)
            return;
        var cleaned = handles.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim()).ToArray();
        if (cleaned.Length == 0)
            return;
        sb.AppendLine($"{label}: {string.Join(", ", cleaned.Select(h => $"[handle: {h}]"))}");
    }

    /// <summary>Generations Gramps Web searches for a common ancestor (the API default for <c>depth</c>).</summary>
    internal const int RelationSearchDepth = 15;

    /// <summary>
    /// Both people, the closest relationship as "person 2 is the X of person 1", generations to the
    /// common ancestor, and the common ancestors of every relationship Gramps finds (closest first).
    /// </summary>
    /// <param name="ancestors">Common-ancestor profiles by handle; others are shown by handle.</param>
    public static string FormatRelationships(
        string handle1, GrampsPersonProfile? person1,
        string handle2, GrampsPersonProfile? person2,
        GrampsRelationship relation,
        IReadOnlyList<GrampsRelationshipItem> all,
        IReadOnlyDictionary<string, GrampsPersonProfile> ancestors)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RELATIONSHIP");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"Person 1: {FormatRelationPerson(person1, handle1)}");
        sb.AppendLine($"Person 2: {FormatRelationPerson(person2, handle2)}");
        sb.AppendLine();

        var items = all.Where(i => !string.IsNullOrWhiteSpace(i.RelationshipString)).ToArray();
        var closest = relation.RelationshipString?.Trim();
        if (string.IsNullOrEmpty(closest))
            closest = items.FirstOrDefault()?.RelationshipString?.Trim();
        if (string.IsNullOrEmpty(closest))
        {
            sb.AppendLine($"Not related: they are not spouses and share no ancestor within {RelationSearchDepth} generations.");
            return sb.ToString();
        }

        sb.AppendLine($"{ShortRelationName(person2, handle2)} is the {closest} of {ShortRelationName(person1, handle1)}.");
        if (relation.DistanceCommonOrigin is >= 0 and var fromFirst && relation.DistanceCommonOther is >= 0 and var fromSecond)
            sb.AppendLine($"Generations to the common ancestor: {fromFirst} from person 1, {fromSecond} from person 2.");

        if (items.Length == 1 && items[0].RelationshipString!.Trim() == closest)
        {
            AppendCommonAncestors(sb, items[0], ancestors, "");
        }
        else if (items.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"All relationships ({items.Length}), closest first:");
            for (var i = 0; i < items.Length; i++)
            {
                sb.AppendLine($"  {i + 1}. {items[i].RelationshipString!.Trim()}");
                AppendCommonAncestors(sb, items[i], ancestors, "     ");
            }
        }

        return sb.ToString();
    }

    /// <summary>Distinct non-empty common-ancestor handles in API order.</summary>
    internal static IEnumerable<string> CommonAncestorHandles(GrampsRelationshipItem item) =>
        (item.CommonAncestors ?? []).Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim()).Distinct();

    private static void AppendCommonAncestors(
        StringBuilder sb, GrampsRelationshipItem item, IReadOnlyDictionary<string, GrampsPersonProfile> ancestors, string indent)
    {
        var handles = CommonAncestorHandles(item).ToArray();
        if (handles.Length == 0)
            return;
        if (indent.Length == 0)
            sb.AppendLine();
        sb.AppendLine($"{indent}Common ancestors:");
        foreach (var handle in handles)
            sb.AppendLine($"{indent}  - {FormatRelationPerson(ancestors.GetValueOrDefault(handle), handle)}");
    }

    /// <summary>Profile line when the person has a display name, otherwise the Gramps ID and handle.</summary>
    internal static string FormatRelationPerson(GrampsPersonProfile? person, string handle)
    {
        if (!string.IsNullOrWhiteSpace(person?.NameDisplay))
            return FormatProfilePerson(person, handle);
        return string.IsNullOrWhiteSpace(person?.GrampsId)
            ? $"[handle: {handle.Trim()}]"
            : $"{person.GrampsId.Trim()} [handle: {handle.Trim()}]";
    }

    /// <summary>"Petrov, Ivan (I0001)" for the relationship sentence.</summary>
    private static string ShortRelationName(GrampsPersonProfile? person, string handle)
    {
        var id = person?.GrampsId?.Trim();
        if (!string.IsNullOrWhiteSpace(person?.NameDisplay))
            return string.IsNullOrEmpty(id) ? person.NameDisplay.Trim() : $"{person.NameDisplay.Trim()} ({id})";
        return string.IsNullOrEmpty(id) ? $"[handle: {handle.Trim()}]" : id;
    }

    /// <summary>"Petrov, Ivan (I0012) [handle: h], b. 1880 in Dublin, d. 1950".</summary>
    internal static string FormatProfilePerson(GrampsPersonProfile person, string handle)
    {
        var id = string.IsNullOrWhiteSpace(person.GrampsId) ? "" : $" ({person.GrampsId.Trim()})";
        return string.Join(", ", [$"{person.NameDisplay!.Trim()}{id} [handle: {handle.Trim()}]", .. FormatProfileVitals(person)]);
    }

    /// <summary>"Petrov, Ivan, b. 1880 in Dublin, d. 1950 [I0012]", the <see cref="FormatPersonSummary"/> layout.</summary>
    internal static string FormatProfileSummary(GrampsPersonProfile person, string? grampsId)
    {
        var name = string.IsNullOrWhiteSpace(person.NameDisplay) ? "Unknown" : person.NameDisplay.Trim();
        var summary = string.Join(", ", [name, .. FormatProfileVitals(person)]);
        return string.IsNullOrWhiteSpace(grampsId) ? summary : $"{summary} [{grampsId.Trim()}]";
    }

    /// <summary>
    /// "b. 1880 in Dublin", "d. 1950". The profile falls back to baptism or burial when birth or death is
    /// missing; a fallback is named by its type instead: "burial 1950".
    /// </summary>
    private static IEnumerable<string> FormatProfileVitals(GrampsPersonProfile person)
    {
        foreach (var (evt, abbreviation, type) in new[] { (person.Birth, "b.", "Birth"), (person.Death, "d.", "Death") })
        {
            if (FormatProfileEvent(evt) is not { } details)
                continue;
            var actual = evt!.Type?.Trim();
            yield return string.IsNullOrEmpty(actual) || string.Equals(actual, type, StringComparison.OrdinalIgnoreCase)
                ? $"{abbreviation} {details}"
                : $"{actual.ToLowerInvariant()} {details}";
        }
    }

    /// <summary>"1880 in Dublin", or "Marriage 1880 in Dublin" with <paramref name="withType"/>; null when empty.</summary>
    internal static string? FormatProfileEvent(GrampsEventProfile? evt, bool withType = false)
    {
        if (evt == null)
            return null;
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(evt.Date))
            details.Add(evt.Date.Trim());
        if (!string.IsNullOrWhiteSpace(evt.PlaceName))
            details.Add($"in {evt.PlaceName.Trim()}");
        if (withType && !string.IsNullOrWhiteSpace(evt.Type))
            details.Insert(0, evt.Type.Trim());
        return details.Count == 0 ? null : string.Join(" ", details);
    }
}
