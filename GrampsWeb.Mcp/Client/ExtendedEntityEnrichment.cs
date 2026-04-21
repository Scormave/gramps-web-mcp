using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Client;

/// <summary>
/// Gramps Web <c>extend=all</c> on person/family fills <c>extended.*</c> with first-level records, but those records
/// do not apply nested <c>extend</c> (e.g. citation <c>source_handle</c>, event <c>place</c>). This class refetches
/// those payloads where needed. <see cref="GrampsPersonExtendedData.Media"/> / family media are filled from
/// <c>media_list</c> handles when the API omits <c>extended.media</c>.
/// </summary>
public static class ExtendedEntityEnrichment
{
    public static async Task EnrichPersonExtendedAsync(GrampsPersonExtended? person, GrampsApiClient client)
    {
        if (person?.Extended == null)
            return;

        var ext = person.Extended;
        if (ext.Citations is { Length: > 0 })
            ext.Citations = await EnrichCitationListAsync(ext.Citations, client).ConfigureAwait(false);
        if (ext.Events is { Length: > 0 })
            ext.Events = await EnrichEventListAsync(ext.Events, client).ConfigureAwait(false);
        var personMediaHandles = GrampsMediaRef.ToHandleStrings(person.MediaList);
        if (ext.Media is not { Length: > 0 } && personMediaHandles is { Length: > 0 })
            ext.Media = await FetchMediaByHandlesAsync(personMediaHandles, client).ConfigureAwait(false);
    }

    public static async Task EnrichFamilyExtendedAsync(GrampsFamilyExtended? family, GrampsApiClient client)
    {
        if (family?.Extended == null)
            return;

        var ext = family.Extended;
        if (ext.Citations is { Length: > 0 })
            ext.Citations = await EnrichCitationListAsync(ext.Citations, client).ConfigureAwait(false);
        if (ext.Events is { Length: > 0 })
            ext.Events = await EnrichEventListAsync(ext.Events, client).ConfigureAwait(false);
        var familyMediaHandles = GrampsMediaRef.ToHandleStrings(family.MediaList);
        if (ext.Media is not { Length: > 0 } && familyMediaHandles is { Length: > 0 })
            ext.Media = await FetchMediaByHandlesAsync(familyMediaHandles, client).ConfigureAwait(false);
    }

    private static async Task<GrampsCitationExtended[]> EnrichCitationListAsync(
        GrampsCitationExtended[] list,
        GrampsApiClient client)
    {
        var tasks = list.Select(c => EnrichSingleCitationAsync(c, client));
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task<GrampsCitationExtended> EnrichSingleCitationAsync(
        GrampsCitationExtended? c,
        GrampsApiClient client)
    {
        if (c == null)
            return new GrampsCitationExtended();

        if (string.IsNullOrWhiteSpace(c.Handle))
            return c;

        if (c.Extended?.Source != null)
            return c;

        try
        {
            var path = $"/api/citations/{Uri.EscapeDataString(c.Handle.Trim())}?extend=all";
            var fresh = await client.GetOrNullIfNotFoundAsync<GrampsCitationExtended>(path).ConfigureAwait(false);
            if (fresh != null)
                return fresh;
        }
        catch
        {
            /* keep original */
        }

        return c;
    }

    private static async Task<GrampsEventExtended[]> EnrichEventListAsync(
        GrampsEventExtended[] list,
        GrampsApiClient client)
    {
        var tasks = list.Select(e => EnrichSingleEventAsync(e, client));
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task<GrampsEventExtended> EnrichSingleEventAsync(
        GrampsEventExtended? e,
        GrampsApiClient client)
    {
        if (e == null)
            return new GrampsEventExtended();

        if (e.Extended?.Place != null)
            return e;

        if (string.IsNullOrWhiteSpace(e.Handle) || string.IsNullOrWhiteSpace(e.Place))
            return e;

        try
        {
            var path = $"/api/events/{Uri.EscapeDataString(e.Handle.Trim())}?extend=place";
            var fresh = await client.GetOrNullIfNotFoundAsync<GrampsEventExtended>(path).ConfigureAwait(false);
            if (fresh != null)
                return fresh;
        }
        catch
        {
            /* keep original */
        }

        return e;
    }

    private static async Task<GrampsMedia[]> FetchMediaByHandlesAsync(string[] handles, GrampsApiClient client)
    {
        var tasks = handles
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(async h =>
            {
                try
                {
                    return await client
                        .GetOrNullIfNotFoundAsync<GrampsMedia>($"/api/media/{Uri.EscapeDataString(h.Trim())}")
                        .ConfigureAwait(false);
                }
                catch
                {
                    return null;
                }
            });
        var arr = await Task.WhenAll(tasks).ConfigureAwait(false);
        return arr.Where(m => m != null).Cast<GrampsMedia>().ToArray();
    }
}
