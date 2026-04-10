using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>
/// Gramps Web API Name object (JSON).
/// </summary>
public class GrampsName
{
    [JsonPropertyName("call")]
    public string? Call { get; set; }

    [JsonPropertyName("citation_list")]
    public string[]? CitationList { get; set; }

    [JsonPropertyName("date")]
    public GrampsDate? Date { get; set; }

    [JsonPropertyName("display_as")]
    public int DisplayAs { get; set; }

    [JsonPropertyName("famnick")]
    public string? FamNick { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("group_as")]
    public string? GroupAs { get; set; }

    [JsonPropertyName("nick")]
    public string? Nick { get; set; }

    [JsonPropertyName("note_list")]
    public string[]? NoteList { get; set; }

    [JsonPropertyName("private")]
    public bool Private { get; set; }

    [JsonPropertyName("sort_as")]
    public int SortAs { get; set; }

    [JsonPropertyName("suffix")]
    public string? Suffix { get; set; }

    [JsonPropertyName("surname_list")]
    public GrampsSurname[]? SurnameList { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Gramps name type (JSON key <c>type</c>). See <see cref="GrampsNameTypeObjectConverter"/>.</summary>
    [JsonPropertyName("type")]
    [JsonConverter(typeof(GrampsNameTypeObjectConverter))]
    public string? Type { get; set; }

    /// <summary>
    /// Returns a formatted full name for display.
    /// </summary>
    public string ToDisplayString()
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(Title))
            parts.Add(Title);

        if (!string.IsNullOrEmpty(FirstName))
            parts.Add(FirstName);

        if (!string.IsNullOrEmpty(Call))
            parts.Add($"({Call})");

        if (SurnameList != null && SurnameList.Length > 0)
        {
            var surnameParts = new List<string>();
            foreach (var surname in SurnameList)
            {
                var sn = "";
                if (!string.IsNullOrEmpty(surname.Prefix))
                    sn += surname.Prefix + " ";
                if (!string.IsNullOrEmpty(surname.Surname))
                    sn += surname.Surname;
                if (!string.IsNullOrEmpty(surname.Connector))
                    sn += " " + surname.Connector;
                surnameParts.Add(sn.Trim());
            }
            parts.Add(string.Join(" ", surnameParts));
        }

        if (!string.IsNullOrEmpty(Suffix))
            parts.Add(Suffix);

        return string.Join(" ", parts).Trim();
    }
}
