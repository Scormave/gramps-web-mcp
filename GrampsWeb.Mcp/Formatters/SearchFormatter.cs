using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Formats search hits and generic object list results. Per-item lines are shared so <c>search</c> and <c>list_objects</c> match.
/// </summary>
public static class SearchFormatter
{
    private const int ResultSeparatorWidth = 60;

    public static async Task<string> FormatSearchResults(GrampsSearchHit[] hits, GrampsApiClient client)
    {
        if (hits == null || hits.Length == 0)
            return "No results found";

        var tables = await GrampsDefaultTypeLabels.PrefetchAllAsync(client);
        var sb = new StringBuilder();
        sb.AppendLine($"Search Results ({hits.Length}):");
        sb.AppendLine(new string('=', ResultSeparatorWidth));

        foreach (var hit in hits)
        {
            try
            {
                var line = await FormatLineForSearchHitAsync(hit, client, tables);
                if (!string.IsNullOrEmpty(line))
                    sb.AppendLine($"{line}{FormatHandleGrampsSuffix(hit.Handle, hit.GrampsId)}");
            }
            catch
            {
                sb.AppendLine($"{hit.ObjectType}: {hit.GrampsId}{FormatHandleGrampsSuffix(hit.Handle, hit.GrampsId)} (error loading details)");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Fetches a paged list and formats each row the same way as <see cref="FormatSearchResults"/>.
    /// </summary>
    public static async Task<string> FetchAndFormatObjects<T>(
        string queryString,
        GrampsApiClient client,
        string objectType) where T : class
    {
        var result = await client.GetPagedListAsync<T>(queryString);

        if (result?.Objects == null || result.Objects.Length == 0)
            return $"No {objectType} found.";

        int totalPages = result.Total >= 0
            ? (result.Total + result.Objects.Length - 1) / result.Objects.Length
            : -1;
        return await FormatObjectListResultsAsync(result.Objects, result.Page, totalPages, result.Total, objectType, client);
    }

    public static async Task<string> FormatObjectListResultsAsync<T>(
        T[] objects,
        int page,
        int totalPages,
        int totalCount,
        string objectType,
        GrampsApiClient client)
    {
        var sb = new StringBuilder();

        int pagesize = objects.Length;
        if (totalCount >= 0 && totalPages > 0)
            sb.AppendLine($"{objectType.ToUpperInvariant()} (Page {page} of {totalPages}, Total: {totalCount})");
        else
            sb.AppendLine($"{objectType.ToUpperInvariant()} (Page {page}, Total: unknown)");
        sb.AppendLine(new string('=', ResultSeparatorWidth));
        sb.AppendLine();

        var typeKey = objectType.ToLowerInvariant();
        var tables = await GrampsDefaultTypeLabels.PrefetchForObjectListAsync(typeKey, client);

        for (int i = 0; i < objects.Length; i++)
        {
            var item = objects[i];
            if (item == null)
                continue;

            int itemNumber = (page - 1) * pagesize + i + 1;
            try
            {
                var line = await FormatLineForListedObjectAsync(item, typeKey, client, tables);
                if (!string.IsNullOrEmpty(line))
                {
                    var handle = GetHandle(item);
                    var grampsId = GetGrampsId(item);
                    sb.AppendLine($"{itemNumber}. {line}{FormatHandleGrampsSuffix(handle, grampsId)}");
                }
            }
            catch
            {
                sb.AppendLine($"{itemNumber}. {typeKey}: (error loading details){FormatHandleGrampsSuffix(GetHandle(item), GetGrampsId(item))}");
            }
        }

        sb.AppendLine();
        if (totalPages > 1)
            sb.AppendLine($"(Page {page} of {totalPages})");

        return sb.ToString();
    }

    /// <summary>Appends <c> — handle: …</c> and optional <c> | gramps_id: …</c>.</summary>
    private static string FormatHandleGrampsSuffix(string? handle, string? grampsId)
    {
        var h = string.IsNullOrEmpty(handle) ? "—" : handle;
        if (string.IsNullOrWhiteSpace(grampsId))
            return $" — handle: {h}";
        return $" — handle: {h} | gramps_id: {grampsId.Trim()}";
    }

    private static string? GetHandle(object item)
    {
        return item switch
        {
            GrampsPerson p => p.Handle,
            GrampsFamilyExtended fx => fx.Handle,
            GrampsFamily f => f.Handle,
            GrampsEvent e => e.Handle,
            GrampsPlace pl => pl.Handle,
            GrampsSource s => s.Handle,
            GrampsCitationExtended cx => cx.Handle,
            GrampsCitation c => c.Handle,
            GrampsRepository r => r.Handle,
            GrampsNote n => n.Handle,
            GrampsMedia m => m.Handle,
            GrampsTag t => t.Handle,
            _ => null
        };
    }

    private static string? GetGrampsId(object item)
    {
        return item switch
        {
            GrampsPerson p => p.GrampsId,
            GrampsFamilyExtended fx => fx.GrampsId,
            GrampsFamily f => f.GrampsId,
            GrampsEvent e => e.GrampsId,
            GrampsPlace pl => pl.GrampsId,
            GrampsSource s => s.GrampsId,
            GrampsCitationExtended cx => cx.GrampsId,
            GrampsCitation c => c.GrampsId,
            GrampsRepository r => r.GrampsId,
            GrampsNote n => n.GrampsId,
            GrampsMedia m => m.GrampsId,
            GrampsTag t => t.GrampsId,
            _ => null
        };
    }

    private static async Task<string?> FormatLineForSearchHitAsync(
        GrampsSearchHit hit,
        GrampsApiClient client,
        GrampsTypeLabelTables tables)
    {
        var t = hit.ObjectType?.ToLowerInvariant();
        return t switch
        {
            "person" or "people" => await FetchAndBuildPersonLineAsync(hit.Handle, client),
            "family" or "families" => await FetchAndBuildFamilyLineAsync(hit.Handle, client, tables.FamilyRelationTypes),
            "event" or "events" => await FetchAndBuildEventLineAsync(hit.Handle, client, tables.EventTypes),
            "place" or "places" => await FetchAndBuildPlaceLineAsync(hit.Handle, client, tables.PlaceTypes),
            "source" or "sources" => await FetchAndBuildSourceLineAsync(hit.Handle, client),
            "citation" or "citations" => await FetchAndBuildCitationLineAsync(hit.Handle, client),
            "note" or "notes" => await FetchAndBuildNoteLineAsync(hit.Handle, client, tables.NoteTypes),
            "media" => await FetchAndBuildMediaLineAsync(hit.Handle, client),
            "tag" or "tags" => await FetchAndBuildTagLineAsync(hit.Handle, client),
            "repository" or "repositories" => await FetchAndBuildRepositoryLineAsync(hit.Handle, client, tables.RepositoryTypes),
            _ => $"{hit.ObjectType}: {hit.GrampsId}"
        };
    }

    private static async Task<string?> FormatLineForListedObjectAsync(
        object item,
        string objectTypeKey,
        GrampsApiClient client,
        GrampsTypeLabelTables tables)
    {
        switch (objectTypeKey)
        {
            case "people" when item is GrampsPerson p:
                return await BuildPersonSearchLineAsync(p, client);
            case "families" when item is GrampsFamilyExtended fx:
                return await BuildFamilySearchLineAsync(fx, client, tables.FamilyRelationTypes);
            case "families" when item is GrampsFamily f:
                return await BuildFamilySearchLineFromHandlesAsync(f, client, tables.FamilyRelationTypes);
            case "events" when item is GrampsEventExtended ee:
                return await BuildEventSearchLineAsync(ee, client, tables.EventTypes);
            case "events" when item is GrampsEvent e:
                return await BuildEventSearchLineAsync(e, client, tables.EventTypes);
            case "places" when item is GrampsPlace pl:
                return BuildPlaceSearchLine(pl, tables.PlaceTypes);
            case "sources" when item is GrampsSource s:
                return BuildSourceSearchLine(s);
            case "citations" when item is GrampsCitationExtended cx:
                return await BuildCitationSearchLineAsync(cx, client);
            case "citations" when item is GrampsCitation c:
                return await BuildCitationSearchLineAsync(c, client);
            case "repositories" when item is GrampsRepository r:
                return BuildRepositorySearchLine(r, tables.RepositoryTypes);
            case "notes" when item is GrampsNote n:
                return BuildNoteSearchLine(n, tables.NoteTypes);
            case "media" when item is GrampsMedia m:
                return BuildMediaSearchLine(m);
            case "tags" when item is GrampsTag tag:
                return BuildTagSearchLine(tag);
            default:
                return null;
        }
    }

    private static async Task<string?> FetchAndBuildPersonLineAsync(string? handle, GrampsApiClient client)
    {
        if (string.IsNullOrEmpty(handle))
            return null;
        var person = await client.GetAsync<GrampsPerson>($"/api/people/{Uri.EscapeDataString(handle)}");
        return person == null ? null : await BuildPersonSearchLineAsync(person, client);
    }

    private static async Task<string?> FetchAndBuildFamilyLineAsync(
        string? handle,
        GrampsApiClient client,
        IReadOnlyList<string>? familyRelationTypes)
    {
        if (string.IsNullOrEmpty(handle))
            return null;
        var familyEx = await client.GetAsync<GrampsFamilyExtended>(
            $"/api/families/{Uri.EscapeDataString(handle)}?extend=father_handle,mother_handle");
        return familyEx == null ? null : await BuildFamilySearchLineAsync(familyEx, client, familyRelationTypes);
    }

    private static async Task<string?> FetchAndBuildEventLineAsync(
        string? handle,
        GrampsApiClient client,
        IReadOnlyList<string>? eventTypes)
    {
        if (string.IsNullOrEmpty(handle))
            return null;
        var evt = await client.GetAsync<GrampsEventExtended>(
            $"/api/events/{Uri.EscapeDataString(handle)}?extend=place");
        return evt == null ? null : await BuildEventSearchLineAsync(evt, client, eventTypes);
    }

    private static async Task<string?> FetchAndBuildPlaceLineAsync(
        string? handle,
        GrampsApiClient client,
        IReadOnlyList<string>? placeTypes)
    {
        if (string.IsNullOrEmpty(handle))
            return null;
        var place = await client.GetAsync<GrampsPlace>($"/api/places/{Uri.EscapeDataString(handle)}");
        return place == null ? null : BuildPlaceSearchLine(place, placeTypes);
    }

    private static async Task<string?> FetchAndBuildSourceLineAsync(string? handle, GrampsApiClient client)
    {
        if (string.IsNullOrEmpty(handle))
            return null;
        var source = await client.GetAsync<GrampsSource>($"/api/sources/{Uri.EscapeDataString(handle)}");
        return source == null ? null : BuildSourceSearchLine(source);
    }

    private static async Task<string?> FetchAndBuildCitationLineAsync(string? handle, GrampsApiClient client)
    {
        if (string.IsNullOrEmpty(handle))
            return null;
        var citation = await client.GetAsync<GrampsCitationExtended>(
            $"/api/citations/{Uri.EscapeDataString(handle)}?extend=source_handle");
        return citation == null ? null : await BuildCitationSearchLineAsync(citation, client);
    }

    private static async Task<string?> FetchAndBuildNoteLineAsync(
        string? handle,
        GrampsApiClient client,
        IReadOnlyList<string>? noteTypes)
    {
        if (string.IsNullOrEmpty(handle))
            return null;
        var note = await client.GetAsync<GrampsNote>($"/api/notes/{Uri.EscapeDataString(handle)}");
        return note == null ? null : BuildNoteSearchLine(note, noteTypes);
    }

    private static async Task<string?> FetchAndBuildMediaLineAsync(string? handle, GrampsApiClient client)
    {
        if (string.IsNullOrEmpty(handle))
            return null;
        var media = await client.GetAsync<GrampsMedia>($"/api/media/{Uri.EscapeDataString(handle)}");
        return media == null ? null : BuildMediaSearchLine(media);
    }

    private static async Task<string?> FetchAndBuildTagLineAsync(string? handle, GrampsApiClient client)
    {
        if (string.IsNullOrEmpty(handle))
            return null;
        var tag = await client.GetAsync<GrampsTag>($"/api/tags/{Uri.EscapeDataString(handle)}");
        return tag == null ? null : BuildTagSearchLine(tag);
    }

    private static async Task<string?> FetchAndBuildRepositoryLineAsync(
        string? handle,
        GrampsApiClient client,
        IReadOnlyList<string>? repositoryTypes)
    {
        if (string.IsNullOrEmpty(handle))
            return null;
        var repo = await client.GetAsync<GrampsRepository>($"/api/repositories/{Uri.EscapeDataString(handle)}");
        return repo == null ? null : BuildRepositorySearchLine(repo, repositoryTypes);
    }

    private static async Task<string?> BuildPersonSearchLineAsync(GrampsPerson person, GrampsApiClient client)
    {
        var birthInfo = await PersonFormatter.ExtractEventInfo(person, "Birth", client);
        var birthStr = !string.IsNullOrEmpty(birthInfo) ? $" (b. {birthInfo})" : "";
        var displayName = GrampsValueFormatter.FormatName(person.PrimaryName);
        return $"Person: {displayName}{birthStr}";
    }

    private static Task<string?> BuildFamilySearchLineFromHandlesAsync(
        GrampsFamily family,
        GrampsApiClient client,
        IReadOnlyList<string>? familyRelationTypes)
    {
        var fx = new GrampsFamilyExtended();
        CopyFamilyBase(family, fx);
        return BuildFamilySearchLineAsync(fx, client, familyRelationTypes);
    }

    private static void CopyFamilyBase(GrampsFamily from, GrampsFamilyExtended to)
    {
        to.Handle = from.Handle;
        to.GrampsId = from.GrampsId;
        to.FatherHandle = from.FatherHandle;
        to.MotherHandle = from.MotherHandle;
        to.ChildRefList = from.ChildRefList;
        to.EventRefList = from.EventRefList;
        to.MediaList = from.MediaList;
        to.AttributeList = from.AttributeList;
        to.NoteList = from.NoteList;
        to.CitationList = from.CitationList;
        to.Change = from.Change;
        to.TagList = from.TagList;
        to.Private = from.Private;
        to.Relationship = from.Relationship;
    }

    private static async Task<string?> BuildFamilySearchLineAsync(
        GrampsFamilyExtended family,
        GrampsApiClient client,
        IReadOnlyList<string>? familyRelationTypes)
    {
        static bool Meaningful(string? s) => !string.IsNullOrEmpty(s) && s != "Unknown";

        string? fatherDisplay = null;
        string? motherDisplay = null;

        var father = family.Extended?.Father;
        if (father != null)
        {
            var n = GrampsValueFormatter.FormatName(father.PrimaryName);
            if (Meaningful(n))
                fatherDisplay = n;
        }

        var mother = family.Extended?.Mother;
        if (mother != null)
        {
            var n = GrampsValueFormatter.FormatName(mother.PrimaryName);
            if (Meaningful(n))
                motherDisplay = n;
        }

        if (fatherDisplay == null && !string.IsNullOrEmpty(family.FatherHandle))
        {
            try
            {
                var p = await client.GetAsync<GrampsPerson>($"/api/people/{Uri.EscapeDataString(family.FatherHandle)}");
                if (p != null)
                {
                    var n = GrampsValueFormatter.FormatName(p.PrimaryName);
                    if (Meaningful(n))
                        fatherDisplay = n;
                }
            }
            catch { /* keep line useful without extra names */ }
        }

        if (motherDisplay == null && !string.IsNullOrEmpty(family.MotherHandle))
        {
            try
            {
                var p = await client.GetAsync<GrampsPerson>($"/api/people/{Uri.EscapeDataString(family.MotherHandle)}");
                if (p != null)
                {
                    var n = GrampsValueFormatter.FormatName(p.PrimaryName);
                    if (Meaningful(n))
                        motherDisplay = n;
                }
            }
            catch { }
        }

        string partners = (fatherDisplay, motherDisplay) switch
        {
            (not null, not null) => $"{fatherDisplay} and {motherDisplay}",
            (not null, null) => fatherDisplay,
            (null, not null) => motherDisplay,
            _ => "Unknown partners"
        };

        var rel = family.Relationship?.Trim();
        string relPart;
        if (string.IsNullOrEmpty(rel))
            relPart = "";
        else
        {
            var relLabel = GrampsDefaultTypeLabels.ResolveStored(rel, familyRelationTypes);
            relPart = $" ({relLabel})";
        }

        return $"Family: {partners}{relPart}";
    }

    private static async Task<string?> BuildEventSearchLineAsync(
        GrampsEvent evt,
        GrampsApiClient client,
        IReadOnlyList<string>? eventTypes)
    {
        var dateStr = evt.Date != null ? GrampsValueFormatter.FormatDate(evt.Date) : "—";
        var placeStr = await FormatEventPlaceSegmentAsync(evt, client);
        var typeLabel = GrampsDefaultTypeLabels.ResolveStored(evt.Type, eventTypes);
        return $"Event: {typeLabel} — {dateStr} — {placeStr}";
    }

    private static async Task<string> FormatEventPlaceSegmentAsync(GrampsEvent evt, GrampsApiClient client)
    {
        if (evt is GrampsEventExtended { Extended.Place: { } embedded })
        {
            var label = GrampsValueFormatter.FormatPlace(embedded);
            if (!string.IsNullOrEmpty(label) && label != "Unknown place")
                return label;
        }

        return await ResolveEventPlaceByHandleAsync(evt.Place, client);
    }

    private static async Task<string> ResolveEventPlaceByHandleAsync(string? placeRef, GrampsApiClient client)
    {
        if (string.IsNullOrWhiteSpace(placeRef))
            return "—";

        try
        {
            var place = await client.GetAsync<GrampsPlace>($"/api/places/{Uri.EscapeDataString(placeRef)}");
            if (place != null)
            {
                var label = GrampsValueFormatter.FormatPlace(place);
                if (!string.IsNullOrEmpty(label) && label != "Unknown place")
                    return label;
            }
        }
        catch
        {
            /* use fallback below */
        }

        return placeRef;
    }

    private static string BuildPlaceSearchLine(GrampsPlace place, IReadOnlyList<string>? placeTypes)
    {
        if (string.IsNullOrWhiteSpace(place.Type))
            return $"Place: {place.Name}";
        var typeLabel = GrampsDefaultTypeLabels.ResolveStored(place.Type.Trim(), placeTypes);
        return $"Place: {place.Name} ({typeLabel})";
    }

    private static string BuildSourceSearchLine(GrampsSource source)
    {
        return $"Source: {source.Title}";
    }

    private static async Task<string?> BuildCitationSearchLineAsync(GrampsCitation citation, GrampsApiClient client)
    {
        var pageStr = string.IsNullOrWhiteSpace(citation.Page) ? null : citation.Page.Trim();
        var sourceTitle = await ResolveCitationSourceTitleAsync(citation, client);
        string core;
        if (!string.IsNullOrEmpty(sourceTitle) && !string.IsNullOrEmpty(pageStr))
            core = $"{sourceTitle} — p. {pageStr}";
        else if (!string.IsNullOrEmpty(sourceTitle))
            core = sourceTitle;
        else if (!string.IsNullOrEmpty(pageStr))
            core = $"p. {pageStr}";
        else
            core = "—";

        var confLabel = CitationFormatter.ConfidenceLabels[Math.Clamp(citation.Confidence, 0, 4)];
        return $"Citation: {core} (confidence: {confLabel})";
    }

    private static async Task<string?> ResolveCitationSourceTitleAsync(GrampsCitation citation, GrampsApiClient client)
    {
        if (citation is GrampsCitationExtended cx
            && cx.Extended?.Source is { Title: { } title }
            && !string.IsNullOrWhiteSpace(title))
            return title.Trim();

        if (string.IsNullOrEmpty(citation.Source))
            return null;

        try
        {
            var src = await client.GetAsync<GrampsSource>($"/api/sources/{Uri.EscapeDataString(citation.Source)}");
            if (!string.IsNullOrWhiteSpace(src?.Title))
                return src.Title.Trim();
        }
        catch { /* fall through */ }

        return null;
    }

    private static string BuildNoteSearchLine(GrampsNote note, IReadOnlyList<string>? noteTypes)
    {
        string preview;
        if (string.IsNullOrEmpty(note.Text))
            preview = "—";
        else if (note.Text.Length <= 50)
            preview = note.Text;
        else
            preview = note.Text.Substring(0, 50) + "…";
        var typeLabel = string.IsNullOrWhiteSpace(note.Type)
            ? "General"
            : GrampsDefaultTypeLabels.ResolveStored(note.Type.Trim(), noteTypes);
        return $"Note: [{typeLabel}] {preview}";
    }

    private static string BuildMediaSearchLine(GrampsMedia media)
    {
        var mimeShort = string.IsNullOrEmpty(media.Mime) ? "unknown" : media.Mime.Split('/')[0];
        var label = Path.GetFileName(media.Path ?? "") is { Length: > 0 } fn ? fn : (media.Description ?? "(unnamed)");
        return $"Media: [{mimeShort}] {label}";
    }

    private static string BuildTagSearchLine(GrampsTag tag)
    {
        return $"Tag: {tag.Name}";
    }

    private static string BuildRepositorySearchLine(GrampsRepository repo, IReadOnlyList<string>? repositoryTypes)
    {
        if (string.IsNullOrWhiteSpace(repo.Type))
            return $"Repository: {repo.Name}";
        var typeLabel = GrampsDefaultTypeLabels.ResolveStored(repo.Type.Trim(), repositoryTypes);
        return $"Repository: {repo.Name} ({typeLabel})";
    }
}
