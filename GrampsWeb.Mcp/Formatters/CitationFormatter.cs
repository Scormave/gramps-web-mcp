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

    /// <summary>Single-line bullet for citations listed under person/family <c>extended</c> (includes resolved source title when present).</summary>
    public static string FormatEmbeddedCitationExtendedLine(GrampsCitationExtended c)
    {
        var ch = string.IsNullOrWhiteSpace(c.Handle) ? "—" : c.Handle.Trim();
        var sh = string.IsNullOrWhiteSpace(c.Source) ? "" : $" source [handle: {c.Source.Trim()}]";
        var parts = new List<string>();
        if (c.Extended?.Source is { Title: { } t } && !string.IsNullOrWhiteSpace(t))
            parts.Add(t.Trim());
        if (!string.IsNullOrWhiteSpace(c.Page))
            parts.Add($"p. {c.Page.Trim()}");
        var label = parts.Count > 0 ? string.Join(" — ", parts) : "—";
        return $"  • {label} [handle: {ch}]{sh}";
    }

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

        HandleListFormatter.AppendHandleBulletSection(sb, "Notes", citation.NoteList);
        HandleListFormatter.AppendHandleBulletSection(sb, "Media", GrampsMediaRef.ToHandleStrings(citation.MediaList));
        HandleListFormatter.AppendHandleBulletSection(sb, "Tags", citation.TagList);

        return sb.ToString();
    }
}
