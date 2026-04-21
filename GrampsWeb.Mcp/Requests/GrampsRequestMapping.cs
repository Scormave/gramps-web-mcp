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
    public static MediaRefRequest[]? ToMediaRefRequests(string[]? handles)
    {
        if (handles is null || handles.Length == 0)
            return null;

        return handles.Select(h => new MediaRefRequest { Ref = h }).ToArray();
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
