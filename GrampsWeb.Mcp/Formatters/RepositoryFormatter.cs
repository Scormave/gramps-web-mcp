using System.Text;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Formats repository API responses.
/// </summary>
public static class RepositoryFormatter
{
    public static string FormatRepositoryFull(GrampsRepository repo)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"REPOSITORY: {repo.Name} [handle: {repo.Handle}] (gramps_id: {repo.GrampsId})");
        sb.AppendLine(new string('=', 60));

        if (!string.IsNullOrEmpty(repo.Type))
            sb.AppendLine($"Type: {repo.Type}");

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

        if (repo.NoteList?.Length > 0)
            sb.AppendLine($"Notes: {repo.NoteList.Length}");
        if (repo.TagList?.Length > 0)
            sb.AppendLine($"Tags:  {string.Join(", ", repo.TagList)}");

        return sb.ToString();
    }
}
