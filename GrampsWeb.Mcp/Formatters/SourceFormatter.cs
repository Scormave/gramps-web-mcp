using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Formats source API responses.
/// </summary>
public static class SourceFormatter
{
    public static async Task<string> FormatSourceFull(
        GrampsSource source,
        GrampsApiClient client,
        IReadOnlyList<BacklinkGroup>? backlinks = null)
    {
        var labels = await LinkedObjectLabels.LoadAsync(client,
            [
                ("repositories", source.RepositoryRefList?.Select(r => r.Ref ?? "")),
                ("notes", source.NoteList),
                ("media", GrampsMediaRef.ToHandleStrings(source.MediaList)),
                ("tags", source.TagList),
            ],
            backlinks);
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
            sb.AppendLine();
            sb.AppendLine($"Repositories ({source.RepositoryRefList.Length}):");
            foreach (var repo in source.RepositoryRefList)
            {
                var h = string.IsNullOrWhiteSpace(repo.Ref) ? "—" : repo.Ref.Trim();
                var line = HandleListFormatter.FormatBullet(h, labels);
                if (!string.IsNullOrWhiteSpace(repo.CallNumber))
                    line += $" — call #: {repo.CallNumber.Trim()}";
                sb.AppendLine(line);
            }
        }

        HandleListFormatter.AppendHandleBulletSection(sb, "Notes", source.NoteList, labels);
        HandleListFormatter.AppendHandleBulletSection(sb, "Media", GrampsMediaRef.ToHandleStrings(source.MediaList), labels);
        HandleListFormatter.AppendHandleBulletSection(sb, "Tags", source.TagList, labels);

        BacklinkFormatter.AppendReferencedBySections(sb, backlinks, labels);
        return sb.ToString();
    }
}
