using GrampsWeb.Mcp.Dates;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Requests;

/// <summary>Maps tool input to request DTOs for creating objects.</summary>
internal static class GrampsRequestMapping
{
    public static DateRequest? ToDateRequestOrNull(GrampsDate? date)
    {
        if (GrampsDateHelpers.IsEmpty(date))
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

    /// <summary>
    /// Gramps <c>get_schema()</c> for Person, Family, Event, etc. expects <c>media_list</c> items to match
    /// <c>MediaRef</c>, not bare handle strings (Gramps Web API <c>fix_object_dict</c> does not coerce them).
    /// </summary>
    public static MediaRefRequest[]? ToMediaRefRequests(string[]? handles) =>
        handles is null || handles.Length == 0
            ? null
            : handles.Select(static h => new MediaRefRequest { Ref = string.IsNullOrWhiteSpace(h) ? h : h.Trim() }).ToArray();

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
}
