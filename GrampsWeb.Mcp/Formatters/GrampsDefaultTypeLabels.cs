using System.Globalization;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Loads default type label lists from <c>/api/types/default/…</c> and maps stored values (plain name or numeric enum index) to display text.
/// </summary>
public static class GrampsDefaultTypeLabels
{
    /// <summary>All common vocabularies in one round-trip (parallel) for search result lines.</summary>
    public static async Task<GrampsTypeLabelTables> PrefetchAllAsync(GrampsApiClient client)
    {
        var repo = FetchLabelsAsync(client, "repository_types", "repository_types", "repositoryTypes");
        var place = FetchLabelsAsync(client, "place_types", "place_types", "placeTypes");
        var evt = FetchLabelsAsync(client, "event_types", "event_types", "eventTypes");
        var note = FetchLabelsAsync(client, "note_types", "note_types", "noteTypes");
        var fam = FetchLabelsAsync(client, "family_relation_types", "family_relation_types", "familyRelationTypes");
        var name = FetchLabelsAsync(client, "name_types", "name_types", "nameTypes");
        await Task.WhenAll(repo, place, evt, note, fam, name).ConfigureAwait(false);
        return new GrampsTypeLabelTables(
            await repo.ConfigureAwait(false),
            await place.ConfigureAwait(false),
            await evt.ConfigureAwait(false),
            await note.ConfigureAwait(false),
            await fam.ConfigureAwait(false),
            await name.ConfigureAwait(false));
    }

    /// <summary>Loads one vocabulary for <see cref="SearchFormatter.FormatObjectListResultsAsync"/>.</summary>
    public static async Task<GrampsTypeLabelTables> PrefetchForObjectListAsync(string objectTypeKey, GrampsApiClient client)
    {
        return objectTypeKey.ToLowerInvariant() switch
        {
            "repositories" => new GrampsTypeLabelTables(
                await FetchLabelsAsync(client, "repository_types", "repository_types", "repositoryTypes").ConfigureAwait(false),
                null, null, null, null, null),
            "places" => new GrampsTypeLabelTables(
                null,
                await FetchLabelsAsync(client, "place_types", "place_types", "placeTypes").ConfigureAwait(false),
                null, null, null, null),
            "events" => new GrampsTypeLabelTables(
                null, null,
                await FetchLabelsAsync(client, "event_types", "event_types", "eventTypes").ConfigureAwait(false),
                null, null, null),
            "notes" => new GrampsTypeLabelTables(
                null, null, null,
                await FetchLabelsAsync(client, "note_types", "note_types", "noteTypes").ConfigureAwait(false),
                null, null),
            "families" => new GrampsTypeLabelTables(
                null, null, null, null,
                await FetchLabelsAsync(client, "family_relation_types", "family_relation_types", "familyRelationTypes").ConfigureAwait(false),
                null),
            _ => GrampsTypeLabelTables.Empty
        };
    }

    public static Task<string> FormatRepositoryTypeAsync(GrampsApiClient client, string? stored) =>
        FormatStoredTypeAsync(client, stored, "repository_types", "repository_types", "repositoryTypes");

    public static Task<string> FormatEventTypeAsync(GrampsApiClient client, string? stored) =>
        FormatStoredTypeAsync(client, stored, "event_types", "event_types", "eventTypes");

    public static Task<string> FormatNoteTypeAsync(GrampsApiClient client, string? stored) =>
        FormatStoredTypeAsync(client, stored, "note_types", "note_types", "noteTypes");

    public static Task<string> FormatFamilyRelationTypeAsync(GrampsApiClient client, string? stored) =>
        FormatStoredTypeAsync(client, stored, "family_relation_types", "family_relation_types", "familyRelationTypes");

    public static Task<string> FormatNameTypeAsync(GrampsApiClient client, string? stored) =>
        FormatStoredTypeAsync(client, stored, "name_types", "name_types", "nameTypes");

    /// <summary>Loads only <c>name_types</c> labels (e.g. for alternate names on <see cref="PersonFormatter.FormatPersonFull"/>).</summary>
    public static Task<IReadOnlyList<string>?> LoadNameTypeLabelsAsync(GrampsApiClient client) =>
        FetchLabelsAsync(client, "name_types", "name_types", "nameTypes");

    public static async Task<string> FormatStoredTypeAsync(
        GrampsApiClient client,
        string? storedType,
        string typesListSegment,
        params string[] bulkCategoryKeys)
    {
        var labels = await FetchLabelsAsync(client, typesListSegment, bulkCategoryKeys).ConfigureAwait(false);
        return ResolveStored(storedType, labels);
    }

    public static string ResolveStored(string? storedType, IReadOnlyList<string>? labels)
    {
        if (string.IsNullOrWhiteSpace(storedType))
            return "—";

        var t = storedType.Trim();
        if (!IsNumericIndex(t))
            return t;

        if (!int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx) || idx < 0)
            return t;

        if (labels != null && idx < labels.Count && labels[idx].Length > 0)
            return labels[idx];

        return t;
    }

    public static bool IsNumericIndex(string t) => t.Length > 0 && t.All(c => c is >= '0' and <= '9');

    internal static async Task<IReadOnlyList<string>?> FetchLabelsAsync(
        GrampsApiClient client,
        string typesListSegment,
        params string[] bulkCategoryKeys)
    {
        try
        {
            var el = await client.GetAsync<JsonElement>($"/api/types/default/{typesListSegment}").ConfigureAwait(false);
            if (el.ValueKind == JsonValueKind.Array)
            {
                var list = ParseStringArray(el);
                if (list.Count > 0)
                    return list;
            }
        }
        catch
        {
            /* fall through */
        }

        try
        {
            var root = await client.GetAsync<JsonElement>("/api/types/default/").ConfigureAwait(false);
            var categories = TypesPayloadParser.ParseCategories(root);
            foreach (var key in bulkCategoryKeys)
            {
                if (categories.TryGetValue(key, out var list) && list.Count > 0)
                    return list;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static List<string> ParseStringArray(JsonElement el) =>
        el.EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.GetRawText())
            .Where(s => s.Length > 0)
            .ToList();
}

/// <summary>Optional label tables for batched formatting (search / list_objects).</summary>
public sealed record GrampsTypeLabelTables(
    IReadOnlyList<string>? RepositoryTypes,
    IReadOnlyList<string>? PlaceTypes,
    IReadOnlyList<string>? EventTypes,
    IReadOnlyList<string>? NoteTypes,
    IReadOnlyList<string>? FamilyRelationTypes,
    IReadOnlyList<string>? NameTypes)
{
    public static GrampsTypeLabelTables Empty { get; } = new(null, null, null, null, null, null);
}
