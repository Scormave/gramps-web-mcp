using System.Text;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Formats source API responses.
/// </summary>
public static class SourceFormatter
{
    public static string FormatSourceFull(GrampsSource source)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"SOURCE: {source.Title} [handle: {source.Handle}] (gramps_id: {source.GrampsId})");
        sb.AppendLine(new string('=', 60));

        if (!string.IsNullOrEmpty(source.Author))
            sb.AppendLine($"Author:        {source.Author}");
        if (!string.IsNullOrEmpty(source.Abbrev))
            sb.AppendLine($"Abbreviation:  {source.Abbrev}");
        if (!string.IsNullOrEmpty(source.PubInfo))
            sb.AppendLine($"Publication:   {source.PubInfo}");

        if (source.RepositoryRefList?.Length > 0)
        {
            sb.AppendLine("\nRepositories:");
            foreach (var repo in source.RepositoryRefList)
            {
                var label = repo.Ref ?? "(no ref)";
                if (!string.IsNullOrEmpty(repo.CallNumber))
                    label += $" — call #: {repo.CallNumber}";
                sb.AppendLine($"  • {label}");
            }
        }

        HandleListFormatter.AppendHandleBulletSection(sb, "Notes", source.NoteList);
        HandleListFormatter.AppendHandleBulletSection(sb, "Media", source.MediaList);
        if (source.TagList?.Length > 0)
            sb.AppendLine($"Tags:    {string.Join(", ", source.TagList)}");

        return sb.ToString();
    }
}
