using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Formats search hits and generic object list results. Per-item lines are shared so <c>search</c> and <c>list_objects</c> match.
/// </summary>
public static class SearchFormatter
{
    private const int ResultSeparatorWidth = 60;

    /// <summary>
    /// Profile sections the summary lines read: names, birth and death, event places and citation sources.
    /// Added to search and list reads of people, families, events and citations.
    /// </summary>
    public const string ProfileQuery = "profile=self";

    /// <summary>Formats one page of search hits; the header shows the page count when <paramref name="totalCount"/> is known.</summary>
    public static async Task<string> FormatSearchResults(
        GrampsSearchHit[] hits,
        GrampsApiClient client,
        int page = 1,
        int pageSize = 0,
        int totalCount = -1)
    {
        if (hits == null || hits.Length == 0)
            return "No results found";

        var tablesTask = GrampsDefaultTypeLabels.PrefetchForSearchAsync(
            hits.Select(hit => hit.ObjectType), client);
        var objectsTask = LoadSearchObjectsAsync(hits, client);
        await Task.WhenAll(tablesTask, objectsTask);
        var tables = await tablesTask;
        var objects = await objectsTask;

        var sb = new StringBuilder();
        if (totalCount >= 0 && pageSize > 0)
        {
            var totalPages = Math.Max(1, (int)(((long)totalCount + pageSize - 1) / pageSize));
            sb.AppendLine($"Search Results (Page {page} of {totalPages}, Total: {totalCount}):");
        }
        else
            sb.AppendLine($"Search Results ({hits.Length}):");
        sb.AppendLine(new string('=', ResultSeparatorWidth));

        for (var i = 0; i < hits.Length; i++)
        {
            var hit = hits[i];
            var suffix = FormatHandleGrampsSuffix(hit.Handle, hit.GrampsId);
            if (CollectionOf(hit.ObjectType) == null)
                sb.AppendLine($"{hit.ObjectType}: {hit.GrampsId}{suffix}");
            else if (objects[i] is { } item && FormatLine(item, tables) is { Length: > 0 } line)
                sb.AppendLine($"{line}{suffix}");
            else
                sb.AppendLine($"{hit.ObjectType}: {hit.GrampsId}{suffix} (error loading details)");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Fetches a paged list and formats each row the same way as <see cref="FormatSearchResults"/>.
    /// Lists of people, families, events and citations need <see cref="ProfileQuery"/> in <paramref name="queryString"/>.
    /// </summary>
    public static async Task<string> FetchAndFormatObjects<T>(
        string queryString,
        GrampsApiClient client,
        string objectType,
        int pageSize) where T : class
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        var result = await client.GetPagedListAsync<T>(queryString);

        if (result?.Objects == null || result.Objects.Length == 0)
            return $"No {objectType} found.";

        int totalPages = result.Total >= 0
            ? (int)(((long)result.Total + pageSize - 1) / pageSize)
            : -1;
        return await FormatObjectListResultsAsync(result.Objects, result.Page, totalPages, result.Total, objectType, client, pageSize);
    }

    public static async Task<string> FormatObjectListResultsAsync<T>(
        T[] objects,
        int page,
        int totalPages,
        int totalCount,
        string objectType,
        GrampsApiClient client,
        int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        var sb = new StringBuilder();

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

            long itemNumber = ((long)page - 1) * pageSize + i + 1;
            var suffix = FormatHandleGrampsSuffix(GetHandle(item), GetGrampsId(item));
            var line = FormatLine(item, tables);
            sb.AppendLine(string.IsNullOrEmpty(line)
                ? $"{itemNumber}. {typeKey}: (error loading details){suffix}"
                : $"{itemNumber}. {line}{suffix}");
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
            GrampsFamily f => f.Handle,
            GrampsEvent e => e.Handle,
            GrampsPlace pl => pl.Handle,
            GrampsSource s => s.Handle,
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
            GrampsFamily f => f.GrampsId,
            GrampsEvent e => e.GrampsId,
            GrampsPlace pl => pl.GrampsId,
            GrampsSource s => s.GrampsId,
            GrampsCitation c => c.GrampsId,
            GrampsRepository r => r.GrampsId,
            GrampsNote n => n.GrampsId,
            GrampsMedia m => m.GrampsId,
            GrampsTag t => t.GrampsId,
            _ => null
        };
    }

    /// <summary>The collection of a search hit's object type, singular or plural; null for unknown types.</summary>
    private static string? CollectionOf(string? objectType) => objectType?.ToLowerInvariant() switch
    {
        "person" or "people" => "people",
        "family" or "families" => "families",
        "event" or "events" => "events",
        "place" or "places" => "places",
        "source" or "sources" => "sources",
        "citation" or "citations" => "citations",
        "note" or "notes" => "notes",
        "media" => "media",
        "tag" or "tags" => "tags",
        "repository" or "repositories" => "repositories",
        _ => null
    };

    /// <summary>
    /// The object of each hit: the one embedded in the search reply when it carries everything its line
    /// reads, otherwise read with the other hits of its type in one batch. Null when it could not be loaded.
    /// </summary>
    private static async Task<object?[]> LoadSearchObjectsAsync(GrampsSearchHit[] hits, GrampsApiClient client)
    {
        var objects = new object?[hits.Length];
        var missing = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < hits.Length; i++)
        {
            if (CollectionOf(hits[i].ObjectType) is not { } collection)
                continue;
            objects[i] = TryReadSearchObject(hits[i], collection);
            if (objects[i] == null && !string.IsNullOrWhiteSpace(hits[i].Handle))
            {
                if (!missing.TryGetValue(collection, out var indexes))
                    missing[collection] = indexes = [];
                indexes.Add(i);
            }
        }

        await Task.WhenAll(missing.Select(async group =>
        {
            IReadOnlyDictionary<string, object> found;
            try
            {
                found = await FetchByHandlesAsync(group.Key, group.Value.Select(i => hits[i].Handle!), client);
            }
            catch
            {
                return; // These hits get an error line.
            }

            foreach (var i in group.Value)
                objects[i] = found.GetValueOrDefault(hits[i].Handle!.Trim());
        }));
        return objects;
    }

    private static Task<IReadOnlyDictionary<string, object>> FetchByHandlesAsync(
        string collection,
        IEnumerable<string> handles,
        GrampsApiClient client)
    {
        return collection switch
        {
            "people" => FetchAsync<GrampsPerson>(ProfileQuery),
            "families" => FetchAsync<GrampsFamily>(ProfileQuery),
            "events" => FetchAsync<GrampsEvent>(ProfileQuery),
            "citations" => FetchAsync<GrampsCitation>(ProfileQuery),
            "places" => FetchAsync<GrampsPlace>(),
            "sources" => FetchAsync<GrampsSource>(),
            "notes" => FetchAsync<GrampsNote>(),
            "media" => FetchAsync<GrampsMedia>(),
            "tags" => FetchAsync<GrampsTag>(),
            "repositories" => FetchAsync<GrampsRepository>(),
            _ => throw new ArgumentOutOfRangeException(nameof(collection), collection, null)
        };

        async Task<IReadOnlyDictionary<string, object>> FetchAsync<T>(string? query = null) where T : class
        {
            var found = await client.GetByHandlesAsync<T>(collection, handles, item => GetHandle(item), query);
            return found.ToDictionary(pair => pair.Key, pair => (object)pair.Value, StringComparer.Ordinal);
        }
    }

    private static object? TryReadSearchObject(GrampsSearchHit hit, string collection)
    {
        if (hit.Object is not { ValueKind: JsonValueKind.Object } obj
            || !obj.TryGetProperty("handle", out var handle)
            || handle.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(hit.Handle)
            || handle.GetString() != hit.Handle)
            return null;

        // A deserializable partial object is not necessarily enough for the summary.
        // Require all fields used by that summary, even when their values are null/empty;
        // the profile is there only when the server applied ProfileQuery.
        (Type Model, string[] Fields) schema = collection switch
        {
            "people" => (typeof(GrampsPerson), ["profile"]),
            "families" => (typeof(GrampsFamily), ["profile", "type"]),
            "events" => (typeof(GrampsEvent), ["profile", "type", "date"]),
            "places" => (typeof(GrampsPlace), ["name", "place_type"]),
            "sources" => (typeof(GrampsSource), ["title"]),
            "citations" => (typeof(GrampsCitation), ["profile", "page", "confidence"]),
            "notes" => (typeof(GrampsNote), ["text", "type"]),
            "media" => (typeof(GrampsMedia), ["path", "mime", "desc"]),
            "tags" => (typeof(GrampsTag), ["name"]),
            _ => (typeof(GrampsRepository), ["name", "type"])
        };
        var (model, fields) = schema;
        foreach (var field in fields)
        {
            if (!obj.TryGetProperty(field, out var value)
                || (field == "profile" && value.ValueKind != JsonValueKind.Object))
                return null;
        }

        try
        {
            return obj.Deserialize(model, GrampsJson.Options);
        }
        catch (JsonException)
        {
            return null; // Older/incompatible search payload: read the object again.
        }
    }

    /// <summary>The summary line of a loaded object; reads nothing from the server.</summary>
    private static string? FormatLine(object item, GrampsTypeLabelTables tables)
    {
        return item switch
        {
            GrampsPerson p => BuildPersonSearchLine(p),
            GrampsFamily f => BuildFamilySearchLine(f, tables.FamilyRelationTypes),
            GrampsEvent e => BuildEventSearchLine(e, tables.EventTypes),
            GrampsPlace pl => BuildPlaceSearchLine(pl, tables.PlaceTypes),
            GrampsSource s => BuildSourceSearchLine(s),
            GrampsCitation c => BuildCitationSearchLine(c),
            GrampsRepository r => BuildRepositorySearchLine(r, tables.RepositoryTypes),
            GrampsNote n => BuildNoteSearchLine(n, tables.NoteTypes),
            GrampsMedia m => BuildMediaSearchLine(m),
            GrampsTag tag => BuildTagSearchLine(tag),
            _ => null
        };
    }

    /// <summary>"Person: Ivanov, Pyotr, b. 1880 in Tver, d. 1950", the name in the tree's display format.</summary>
    private static string BuildPersonSearchLine(GrampsPerson person)
    {
        var summary = person.Profile is { } profile
            ? PersonFormatter.FormatProfileSummary(profile, grampsId: null)
            : GrampsValueFormatter.FormatName(person.PrimaryName);
        return $"Person: {summary}";
    }

    private static string BuildFamilySearchLine(GrampsFamily family, IReadOnlyList<string>? familyRelationTypes)
    {
        var names = new[] { family.Profile?.Father, family.Profile?.Mother }
            .Select(partner => partner?.NameDisplay?.Trim())
            .Where(name => !string.IsNullOrEmpty(name))
            .ToArray();
        var partners = names.Length == 0 ? "Unknown partners" : string.Join(" and ", names);

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

    private static string BuildEventSearchLine(GrampsEvent evt, IReadOnlyList<string>? eventTypes)
    {
        var dateStr = evt.Date != null ? GrampsValueFormatter.FormatDate(evt.Date) : null;
        var typeLabel = GrampsDefaultTypeLabels.ResolveStored(evt.Type, eventTypes);
        // Missing parts are left out rather than shown as "— — —".
        var segments = new[] { typeLabel, dateStr, evt.Profile?.PlaceName }
            .Where(s => !string.IsNullOrWhiteSpace(s) && s.Trim() is not ("—" or "Unknown" or "Unknown date"))
            .Select(s => s!.Trim())
            .ToArray();
        return segments.Length == 0 ? "Event" : $"Event: {string.Join(" — ", segments)}";
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

    private static string BuildCitationSearchLine(GrampsCitation citation)
    {
        var pageStr = string.IsNullOrWhiteSpace(citation.Page) ? null : citation.Page.Trim();
        var sourceTitle = citation.Profile?.Source?.Title?.Trim();
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
        var fileName = Path.GetFileName(media.Path ?? "");
        var description = media.Description?.Trim();

        string label;
        if (!string.IsNullOrEmpty(description) && !string.IsNullOrEmpty(fileName))
            label = $"{description} ({fileName})";
        else if (!string.IsNullOrEmpty(description))
            label = description;
        else if (!string.IsNullOrEmpty(fileName))
            label = fileName;
        else
            label = "(unnamed)";

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
