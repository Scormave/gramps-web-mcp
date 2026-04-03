using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Formats citation API responses.
/// </summary>
public static class CitationFormatter
{
    public static readonly string[] ConfidenceLabels = { "Very Low", "Low", "Normal", "High", "Very High" };

    public static async Task<string> FormatCitationFull(GrampsCitation citation, GrampsApiClient client)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CITATION [handle: {citation.Handle}] (gramps_id: {citation.GrampsId})");
        sb.AppendLine(new string('=', 60));

        if (!string.IsNullOrEmpty(citation.Source))
        {
            try
            {
                var source = await client.GetAsync<GrampsSource>($"/api/sources/{citation.Source}");
                if (source?.Title != null)
                    sb.AppendLine($"Source: {source.Title} [handle: {citation.Source}]");
            }
            catch { }
        }

        if (!string.IsNullOrEmpty(citation.Page))
            sb.AppendLine($"Page/Location: {citation.Page}");

        var confidenceLabel = ConfidenceLabels[Math.Clamp(citation.Confidence, 0, 4)];
        sb.AppendLine($"Confidence: {confidenceLabel} ({citation.Confidence})");

        if (citation.Date != null)
            sb.AppendLine($"Access Date: {GrampsValueFormatter.FormatDate(citation.Date)}");

        if (citation.NoteList?.Length > 0)
            sb.AppendLine($"Notes: {citation.NoteList.Length}");
        if (citation.MediaList?.Length > 0)
            sb.AppendLine($"Media: {citation.MediaList.Length}");

        return sb.ToString();
    }
}
