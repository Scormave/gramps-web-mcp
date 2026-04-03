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

    public static FamilyRefRequest[]? ToFamilyRefRequests(GrampsFamilyRef[]? list) =>
        list == null ? null : list.Select(fr => new FamilyRefRequest
        {
            Ref = fr.Ref,
            Relationship = fr.Relationship,
            FatherRelationship = fr.FatherRelationship,
            MotherRelationship = fr.MotherRelationship
        }).ToArray();

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
