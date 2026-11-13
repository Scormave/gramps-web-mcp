using System.Globalization;
using System.Text.Json;
using GrampsWeb.Mcp.Client;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Names the types of stored records. Gramps stores a built-in type as its number with an
/// empty string, and a custom type as Custom with its name. Records read from the API carry
/// the name, so a number only comes from stored transaction data. The lists hold the names of
/// <c>/api/types/default/{category}/map</c> at the index of their Gramps value.
/// </summary>
public static class GrampsDefaultTypeLabels
{
    /// <summary>Search summaries only need vocabularies for the types actually returned.</summary>
    public static async Task<GrampsTypeLabelTables> PrefetchForSearchAsync(
        IEnumerable<string?> objectTypes, GrampsApiClient client)
    {
        var types = objectTypes.Select(t => t?.ToLowerInvariant()).ToHashSet();
        Task<IReadOnlyList<string>?> Load(string singular, string plural, string category) =>
            types.Contains(singular) || types.Contains(plural)
                ? FetchLabelsAsync(client, category)
                : Task.FromResult<IReadOnlyList<string>?>(null);

        var repo = Load("repository", "repositories", "repository_types");
        var place = Load("place", "places", "place_types");
        var evt = Load("event", "events", "event_types");
        var note = Load("note", "notes", "note_types");
        var family = Load("family", "families", "family_relation_types");
        await Task.WhenAll(repo, place, evt, note, family).ConfigureAwait(false);
        return new(await repo, await place, await evt, await note, await family, null, null);
    }

    /// <summary>All common vocabularies in one round-trip (parallel) for search result lines.</summary>
    public static async Task<GrampsTypeLabelTables> PrefetchAllAsync(GrampsApiClient client)
    {
        var repo = FetchLabelsAsync(client, "repository_types");
        var place = FetchLabelsAsync(client, "place_types");
        var evt = FetchLabelsAsync(client, "event_types");
        var note = FetchLabelsAsync(client, "note_types");
        var fam = FetchLabelsAsync(client, "family_relation_types");
        var name = FetchLabelsAsync(client, "name_types");
        var nameOrigin = FetchLabelsAsync(client, "name_origin_types");
        await Task.WhenAll(repo, place, evt, note, fam, name, nameOrigin).ConfigureAwait(false);
        return new GrampsTypeLabelTables(
            await repo.ConfigureAwait(false),
            await place.ConfigureAwait(false),
            await evt.ConfigureAwait(false),
            await note.ConfigureAwait(false),
            await fam.ConfigureAwait(false),
            await name.ConfigureAwait(false),
            await nameOrigin.ConfigureAwait(false));
    }

    /// <summary>Loads one vocabulary for <see cref="SearchFormatter.FormatObjectListResultsAsync"/>.</summary>
    public static async Task<GrampsTypeLabelTables> PrefetchForObjectListAsync(string objectTypeKey, GrampsApiClient client)
    {
        return objectTypeKey.ToLowerInvariant() switch
        {
            "repositories" => new GrampsTypeLabelTables(
                await FetchLabelsAsync(client, "repository_types").ConfigureAwait(false),
                null, null, null, null, null, null),
            "places" => new GrampsTypeLabelTables(
                null,
                await FetchLabelsAsync(client, "place_types").ConfigureAwait(false),
                null, null, null, null, null),
            "events" => new GrampsTypeLabelTables(
                null, null,
                await FetchLabelsAsync(client, "event_types").ConfigureAwait(false),
                null, null, null, null),
            "notes" => new GrampsTypeLabelTables(
                null, null, null,
                await FetchLabelsAsync(client, "note_types").ConfigureAwait(false),
                null, null, null),
            "families" => new GrampsTypeLabelTables(
                null, null, null, null,
                await FetchLabelsAsync(client, "family_relation_types").ConfigureAwait(false),
                null, null),
            _ => GrampsTypeLabelTables.Empty
        };
    }

    public static Task<string> FormatRepositoryTypeAsync(GrampsApiClient client, string? stored) =>
        FormatStoredTypeAsync(client, stored, "repository_types");

    public static Task<string> FormatEventTypeAsync(GrampsApiClient client, string? stored) =>
        FormatStoredTypeAsync(client, stored, "event_types");

    public static Task<string> FormatNoteTypeAsync(GrampsApiClient client, string? stored) =>
        FormatStoredTypeAsync(client, stored, "note_types");

    public static Task<string> FormatFamilyRelationTypeAsync(GrampsApiClient client, string? stored) =>
        FormatStoredTypeAsync(client, stored, "family_relation_types");

    public static Task<string> FormatNameTypeAsync(GrampsApiClient client, string? stored) =>
        FormatStoredTypeAsync(client, stored, "name_types");

    public static Task<string> FormatNameOriginTypeAsync(GrampsApiClient client, string? stored) =>
        FormatStoredTypeAsync(client, stored, "name_origin_types");

    /// <summary>Loads the <c>name_types</c> labels.</summary>
    public static Task<IReadOnlyList<string>?> LoadNameTypeLabelsAsync(GrampsApiClient client) =>
        FetchLabelsAsync(client, "name_types");

    /// <summary>Loads the surname origin labels for resolving <c>NameOriginType</c> values.</summary>
    public static Task<IReadOnlyList<string>?> LoadNameOriginTypeLabelsAsync(GrampsApiClient client) =>
        FetchLabelsAsync(client, "name_origin_types");

    /// <summary>
    /// Names a stored type of <paramref name="category"/>; reads the labels only for a number.
    /// </summary>
    public static async Task<string> FormatStoredTypeAsync(
        GrampsApiClient client,
        string? storedType,
        string category)
    {
        var t = storedType?.Trim() ?? "";
        var labels = IsNumericIndex(t)
            ? await FetchLabelsAsync(client, category).ConfigureAwait(false)
            : null;
        return ResolveStored(storedType, labels);
    }

    /// <summary>
    /// Names a stored type: a name passes through, -1 is Unknown, and another number is the
    /// label at that index of <paramref name="labels"/>, or stays a number without one.
    /// </summary>
    public static string ResolveStored(string? storedType, IReadOnlyList<string>? labels)
    {
        if (string.IsNullOrWhiteSpace(storedType))
            return "—";

        var t = storedType.Trim();
        if (t == "-1")
            return "Unknown";

        // Names, built-in or custom, are shown as stored.
        if (!IsNumericIndex(t))
            return t;

        if (!int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return t;

        if (labels != null && value < labels.Count && labels[value].Length > 0)
            return labels[value];

        // No labels, or no name for that value: keep the number.
        return t;
    }

    public static bool IsNumericIndex(string t) => t.Length > 0 && t.All(c => c is >= '0' and <= '9');

    /// <summary>
    /// Reads <c>/api/types/default/{category}/map</c> into names at the index of their value,
    /// or null when it cannot be read: a number then stays a number rather than get a wrong name.
    /// </summary>
    internal static async Task<IReadOnlyList<string>?> FetchLabelsAsync(GrampsApiClient client, string category)
    {
        try
        {
            var map = await client.GetDefaultTypesAsync($"{category}/map").ConfigureAwait(false);
            return LabelsByValue(map);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Turns a value-to-name map such as <c>{"-1":"Unknown","0":"Custom","12":"Birth"}</c> into a
    /// list with each name at the index of its value and empty strings between them.
    /// </summary>
    internal static IReadOnlyList<string>? LabelsByValue(JsonElement map)
    {
        if (map.ValueKind != JsonValueKind.Object)
            return null;

        var names = new Dictionary<int, string>();
        foreach (var entry in map.EnumerateObject())
        {
            if (int.TryParse(entry.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                && value < MaxTypeValue
                && entry.Value.ValueKind == JsonValueKind.String)
                names[value] = entry.Value.GetString() ?? "";
        }

        if (names.Count == 0)
            return null;

        var labels = Enumerable.Repeat("", names.Keys.Max() + 1).ToArray();
        foreach (var (value, name) in names)
            labels[value] = name;
        return labels;
    }

    // Gramps types have a few dozen values; a larger one is not a type.
    private const int MaxTypeValue = 1000;
}

/// <summary>Optional label tables for batched formatting (search / list_objects).</summary>
public sealed record GrampsTypeLabelTables(
    IReadOnlyList<string>? RepositoryTypes,
    IReadOnlyList<string>? PlaceTypes,
    IReadOnlyList<string>? EventTypes,
    IReadOnlyList<string>? NoteTypes,
    IReadOnlyList<string>? FamilyRelationTypes,
    IReadOnlyList<string>? NameTypes,
    IReadOnlyList<string>? NameOriginTypes)
{
    public static GrampsTypeLabelTables Empty { get; } = new(null, null, null, null, null, null, null);
}
