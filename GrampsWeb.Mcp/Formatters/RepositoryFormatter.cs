using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Formats repository API responses.
/// </summary>
public static class RepositoryFormatter
{
    public static async Task<string> FormatRepositoryFullAsync(GrampsRepository repo, GrampsApiClient client)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"REPOSITORY: {repo.Name} [handle: {repo.Handle}] (gramps_id: {repo.GrampsId})");
        sb.AppendLine(new string('=', 60));

        var typeLabel = await GrampsDefaultTypeLabels.FormatRepositoryTypeAsync(client, repo.Type);
        sb.AppendLine($"Type: {typeLabel}");

        if (repo.AddressList?.Length > 0)
        {
            sb.AppendLine("\nAddresses:");
            foreach (var addr in repo.AddressList)
                sb.AppendLine($"  • {addr}");
        }

        if (repo.UrlList?.Length > 0)
        {
            sb.AppendLine("\nURLs:");
            foreach (var url in repo.UrlList)
                sb.AppendLine($"  • {url}");
        }

        HandleListFormatter.AppendHandleBulletSection(sb, "Notes", repo.NoteList);
        if (repo.TagList?.Length > 0)
            sb.AppendLine($"Tags:  {string.Join(", ", repo.TagList)}");

        return sb.ToString();
    }
}
