using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Requests;

/// <summary>Maps Gramps GET models to request DTOs for PUT round-trips.</summary>
internal static class GrampsRequestMapping
{
    public static DateRequest? ToDateRequestOrNull(GrampsDate? date)
    {
        if (IsEmptyGrampsDate(date))
            return null;

        var d = date!;
        return new DateRequest
        {
            Calendar = d.Calendar,
            Modifier = d.Modifier,
            Quality = d.Quality,
            Text = d.Text,
            NewYear = d.NewYear,
            Day = d.Day,
            Month = d.Month,
            Year = d.Year,
            Slash = d.Slash,
            EndDay = d.EndDay,
            EndMonth = d.EndMonth,
            EndYear = d.EndYear,
            EndSlash = d.EndSlash
        };
    }

    public static AttributeRequest[]? ToAttributeRequests(GrampsAttribute[]? list) =>
        list == null ? null : list.Select(a => new AttributeRequest { Type = a.Type, Value = a.Value }).ToArray();

    public static EventRefRequest[]? ToEventRefRequests(GrampsEventRef[]? list) =>
        list == null ? null : list.Select(er => new EventRefRequest
        {
            Ref = er.Ref,
            Role = er.Role,
            NoteList = er.NoteList,
            AttributeList = er.AttributeList
        }).ToArray();

    /// <summary>
    /// Extracts plain handle strings from <see cref="GrampsFamilyRef"/> items for use in
    /// <c>parent_family_list</c> request bodies. Gramps Web API expects plain handles here,
    /// not objects — <c>_get_class_name</c> has no mapping for <c>parent_family_list</c>.
    /// </summary>
    public static string[]? ToParentFamilyHandles(GrampsFamilyRef[]? list) =>
        list == null ? null : list.Select(fr => fr.Ref ?? "").Where(r => r.Length > 0).ToArray();

    /// <summary>
    /// Gramps <c>get_schema()</c> for Person, Family, Event, etc. expects <c>media_list</c> items to match
    /// <c>MediaRef</c>, not bare handle strings (Gramps Web API <c>fix_object_dict</c> does not coerce them).
    /// </summary>
    public static MediaRefRequest[]? ToMediaRefRequests(string[]? handles) =>
        ToMediaRefRequests(handles, existingMedia: null);

    /// <summary>
    /// Builds <see cref="MediaRefRequest"/> from tool handle lists. When <paramref name="existingMedia"/> is provided,
    /// entries whose ref matches a handle (case-insensitive) reuse <c>rect</c>, notes, and other fields from the GET payload.
    /// </summary>
    public static MediaRefRequest[]? ToMediaRefRequests(string[]? handles, GrampsMediaRef[]? existingMedia)
    {
        if (handles is null || handles.Length == 0)
            return null;

        if (existingMedia is null || existingMedia.Length == 0)
            return handles.Select(static h => new MediaRefRequest { Ref = string.IsNullOrWhiteSpace(h) ? h : h.Trim() }).ToArray();

        var byRef = new Dictionary<string, GrampsMediaRef>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in existingMedia)
        {
            var key = m.ResolvedRef;
            if (string.IsNullOrEmpty(key))
                continue;
            if (!byRef.ContainsKey(key))
                byRef[key] = m;
        }

        return handles.Select(h =>
        {
            var trimmed = h?.Trim() ?? "";
            if (trimmed.Length == 0)
                return new MediaRefRequest { Ref = h };

            if (!byRef.TryGetValue(trimmed, out var m))
                return new MediaRefRequest { Ref = trimmed };

            return new MediaRefRequest
            {
                Ref = trimmed,
                Private = m.Private,
                Rect = m.Rect,
                CitationList = m.CitationList,
                NoteList = m.NoteList,
                AttributeList = m.AttributeList
            };
        }).ToArray();
    }

    /// <summary>
    /// Maps GET <see cref="GrampsMediaRef"/> items to mutation bodies so crop <c>rect</c>, notes, etc. are preserved on PUT.
    /// </summary>
    public static MediaRefRequest[]? ToMediaRefRequests(GrampsMediaRef[]? list)
    {
        if (list is null || list.Length == 0)
            return null;

        return list.Select(static m => new MediaRefRequest
        {
            Ref = m.ResolvedRef,
            Private = m.Private,
            Rect = m.Rect,
            CitationList = m.CitationList,
            NoteList = m.NoteList,
            AttributeList = m.AttributeList
        }).ToArray();
    }

    /// <summary>Maps repository refs to request payload shape.</summary>
    public static GrampsRepositoryRef[]? ToRepositoryRefRequests(GrampsRepositoryRef[]? refs) =>
        ToRepositoryRefRequests(refs, existingRepositoryRefs: null);

    /// <summary>
    /// Builds repository ref list from tool input and preserves existing fields for overlapping refs.
    /// For overlapping refs, explicit input values win; missing values are copied from existing.
    /// </summary>
    public static GrampsRepositoryRef[]? ToRepositoryRefRequests(
        GrampsRepositoryRef[]? refs,
        GrampsRepositoryRef[]? existingRepositoryRefs)
    {
        if (refs is null || refs.Length == 0)
            return null;

        if (existingRepositoryRefs is null || existingRepositoryRefs.Length == 0)
        {
            return refs.Select(static rr => new GrampsRepositoryRef
            {
                Ref = rr.Ref,
                CallNumber = rr.CallNumber,
                MediaType = rr.MediaType,
                NoteList = rr.NoteList,
                Private = rr.Private
            }).ToArray();
        }

        var byRef = new Dictionary<string, GrampsRepositoryRef>(StringComparer.OrdinalIgnoreCase);
        foreach (var rr in existingRepositoryRefs)
        {
            var key = rr.Ref?.Trim();
            if (string.IsNullOrEmpty(key))
                continue;
            if (!byRef.ContainsKey(key))
                byRef[key] = rr;
        }

        return refs.Select(input =>
        {
            var trimmed = input.Ref?.Trim() ?? "";
            if (trimmed.Length == 0)
                return new GrampsRepositoryRef
                {
                    Ref = input.Ref,
                    CallNumber = input.CallNumber,
                    MediaType = input.MediaType,
                    NoteList = input.NoteList,
                    Private = input.Private
                };

            if (!byRef.TryGetValue(trimmed, out var existing))
                return new GrampsRepositoryRef
                {
                    Ref = trimmed,
                    CallNumber = string.IsNullOrWhiteSpace(input.CallNumber) ? null : input.CallNumber.Trim(),
                    MediaType = string.IsNullOrWhiteSpace(input.MediaType) ? null : input.MediaType.Trim(),
                    NoteList = input.NoteList,
                    Private = input.Private
                };

            return new GrampsRepositoryRef
            {
                Ref = trimmed,
                CallNumber = string.IsNullOrWhiteSpace(input.CallNumber)
                    ? existing.CallNumber
                    : input.CallNumber.Trim(),
                MediaType = string.IsNullOrWhiteSpace(input.MediaType)
                    ? existing.MediaType
                    : input.MediaType.Trim(),
                NoteList = input.NoteList ?? existing.NoteList,
                // bool is not nullable here, so keep existing for overlap to avoid accidental reset from shorthand input.
                Private = existing.Private
            };
        }).ToArray();
    }

    /// <summary>Builds event_ref_list from parallel handle/role arrays (default role Primary).</summary>
    public static EventRefRequest[] BuildEventRefList(string[]? handles, string[]? roles)
    {
        if (handles is null || handles.Length == 0)
            return [];
        var list = new List<EventRefRequest>();
        for (var i = 0; i < handles.Length; i++)
        {
            list.Add(new EventRefRequest
            {
                Ref = handles[i],
                Role = roles?.Length > i ? roles[i] : "Primary"
            });
        }
        return list.ToArray();
    }

    private static bool IsEmptyGrampsDate(GrampsDate? date)
    {
        if (date is null)
            return true;

        if (date.Modifier == 6)
            return string.IsNullOrWhiteSpace(date.Text);

        if (!string.IsNullOrWhiteSpace(date.Text))
            return false;

        if (date.Calendar != 0 || date.Quality != 0 || date.NewYear != 0)
            return false;

        if (date.Modifier != 0)
            return false;

        if (date.Day != 0 || date.Month != 0 || date.Year != 0 || date.Slash)
            return false;

        return date.EndDay == 0 && date.EndMonth == 0 && date.EndYear == 0 && !date.EndSlash;
    }
}
