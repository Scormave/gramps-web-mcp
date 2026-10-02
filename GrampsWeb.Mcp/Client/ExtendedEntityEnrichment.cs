using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Client;

/// <summary>
/// Gramps Web <c>extend=all</c> on person/family fills <c>extended.*</c> with first-level records, but those records
/// do not apply nested <c>extend</c> (e.g. citation <c>source_handle</c>, event <c>place</c>). This class refetches
/// those payloads where needed, with one batched request per object type. <see cref="GrampsPersonExtendedData.Media"/>
/// / family media are filled from <c>media_list</c> handles when the API omits <c>extended.media</c>.
/// </summary>
public static class ExtendedEntityEnrichment
{
    public static async Task EnrichPersonExtendedAsync(GrampsPersonExtended? person, GrampsApiClient client)
    {
        if (person?.Extended == null)
            return;

        var ext = person.Extended;
        var citations = EnrichCitationListAsync(ext.Citations, client);
        var events = EnrichEventListAsync(ext.Events, client);
        var media = FillMediaAsync(ext.Media, person.MediaList, client);
        await Task.WhenAll(citations, events, media).ConfigureAwait(false);
        ext.Citations = await citations.ConfigureAwait(false);
        ext.Events = await events.ConfigureAwait(false);
        ext.Media = await media.ConfigureAwait(false);
    }

    public static async Task EnrichFamilyExtendedAsync(GrampsFamilyExtended? family, GrampsApiClient client)
    {
        if (family?.Extended == null)
            return;

        var ext = family.Extended;
        var citations = EnrichCitationListAsync(ext.Citations, client);
        var events = EnrichEventListAsync(ext.Events, client);
        var media = FillMediaAsync(ext.Media, family.MediaList, client);
        await Task.WhenAll(citations, events, media).ConfigureAwait(false);
        ext.Citations = await citations.ConfigureAwait(false);
        ext.Events = await events.ConfigureAwait(false);
        ext.Media = await media.ConfigureAwait(false);
    }

    private static async Task<GrampsCitationExtended[]?> EnrichCitationListAsync(
        GrampsCitationExtended[]? list,
        GrampsApiClient client)
    {
        if (list is not { Length: > 0 })
            return list;

        var stale = list.Where(c => c is not null && c.Extended?.Source == null).Select(c => c!.Handle);
        var fresh = await FetchAsync<GrampsCitationExtended>(client, "citations", stale, c => c.Handle, "extend=all")
            .ConfigureAwait(false);
        return list.Select(c => Replace(c, c?.Handle, fresh)).ToArray();
    }

    private static async Task<GrampsEventExtended[]?> EnrichEventListAsync(
        GrampsEventExtended[]? list,
        GrampsApiClient client)
    {
        if (list is not { Length: > 0 })
            return list;

        var stale = list
            .Where(e => e is not null && e.Extended?.Place == null && !string.IsNullOrWhiteSpace(e.Place))
            .Select(e => e!.Handle);
        var fresh = await FetchAsync<GrampsEventExtended>(client, "events", stale, e => e.Handle, "extend=place")
            .ConfigureAwait(false);
        return list.Select(e => Replace(e, e?.Handle, fresh)).ToArray();
    }

    private static async Task<GrampsMedia[]?> FillMediaAsync(
        GrampsMedia[]? media,
        GrampsMediaRef[]? mediaList,
        GrampsApiClient client)
    {
        var handles = GrampsMediaRef.ToHandleStrings(mediaList);
        if (media is { Length: > 0 } || handles is null)
            return media;

        var found = await FetchAsync<GrampsMedia>(client, "media", handles, m => m.Handle).ConfigureAwait(false);
        return handles.Select(h => found.GetValueOrDefault(h.Trim())).OfType<GrampsMedia>().ToArray();
    }

    /// <summary>The refetched object for <paramref name="handle"/>, or the row the API sent.</summary>
    private static T Replace<T>(T? row, string? handle, IReadOnlyDictionary<string, T> fresh)
        where T : class, new()
    {
        if (row is null)
            return new T();
        return handle is not null && fresh.TryGetValue(handle.Trim(), out var replacement) ? replacement : row;
    }

    /// <summary>Objects by handle; none when the request fails, so the rows the API sent stay as they are.</summary>
    private static async Task<IReadOnlyDictionary<string, T>> FetchAsync<T>(
        GrampsApiClient client,
        string collection,
        IEnumerable<string?> handles,
        Func<T, string?> handleOf,
        string? query = null)
        where T : class
    {
        try
        {
            return await client.GetByHandlesAsync(collection, handles, handleOf, query).ConfigureAwait(false);
        }
        catch
        {
            return new Dictionary<string, T>();
        }
    }
}
