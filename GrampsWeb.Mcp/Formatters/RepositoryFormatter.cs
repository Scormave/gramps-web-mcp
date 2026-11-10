using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Formats repository API responses.
/// </summary>
public static class RepositoryFormatter
{
    public static async Task<string> FormatRepositoryFullAsync(
        GrampsRepository repo,
        GrampsApiClient client,
        IReadOnlyList<BacklinkGroup>? backlinks = null)
    {
        var labelsTask = LinkedObjectLabels.LoadAsync(client, [("notes", repo.NoteList), ("tags", repo.TagList)], backlinks);
        var sb = new StringBuilder();
        sb.AppendLine($"REPOSITORY: {repo.Name} [handle: {repo.Handle}] (gramps_id: {repo.GrampsId})");
        sb.AppendLine(new string('=', 60));

        var typeLabel = await GrampsDefaultTypeLabels.FormatRepositoryTypeAsync(client, repo.Type);
        sb.AppendLine($"Type: {typeLabel}");

        AddressListFormatter.AppendAddressesSection(sb, repo.AddressList);
        AddressListFormatter.AppendUrlsSection(sb, repo.UrlList);

        var labels = await labelsTask;
        HandleListFormatter.AppendHandleBulletSection(sb, "Notes", repo.NoteList, labels);
        HandleListFormatter.AppendHandleBulletSection(sb, "Tags", repo.TagList, labels);

        BacklinkFormatter.AppendReferencedBySections(sb, backlinks, labels);
        return sb.ToString();
    }
}
