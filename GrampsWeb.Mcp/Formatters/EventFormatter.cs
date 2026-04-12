using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Formats event API responses.
/// </summary>
public static class EventFormatter
{
    public static async Task<string> FormatEventFull(GrampsEvent evt, GrampsApiClient client)
    {
        var typeDisplay = await GrampsDefaultTypeLabels.FormatEventTypeAsync(client, evt.Type);
        var sb = new StringBuilder();
        sb.AppendLine($"EVENT: {typeDisplay} [handle: {evt.Handle}] (gramps_id: {evt.GrampsId})");
        sb.AppendLine(new string('=', 60));

        if (evt.Date != null)
            sb.AppendLine($"Date:  {GrampsValueFormatter.FormatDate(evt.Date)}");

        if (!string.IsNullOrEmpty(evt.Place))
        {
            try
            {
                var place = await client.GetAsync<GrampsPlace>($"/api/places/{evt.Place}");
                if (place != null)
                    sb.AppendLine($"Place: {GrampsValueFormatter.FormatPlace(place)} [handle: {evt.Place}]");
            }
            catch { }
        }

        if (!string.IsNullOrEmpty(evt.Description))
            sb.AppendLine($"\nDescription:\n{evt.Description}");

        if (evt.AttributeList?.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Attributes:");
            foreach (var attr in evt.AttributeList)
                sb.AppendLine($"  • {attr.Type}: {attr.Value}");
        }

        HandleListFormatter.AppendHandleBulletSection(sb, "Citations", evt.CitationList);
        HandleListFormatter.AppendHandleBulletSection(sb, "Notes", evt.NoteList);
        HandleListFormatter.AppendHandleBulletSection(sb, "Media", evt.MediaList);
        HandleListFormatter.AppendHandleBulletSection(sb, "Tags", evt.TagList);
        if (evt.Private)
            sb.AppendLine("⚠ Private record");

        return sb.ToString();
    }
}
