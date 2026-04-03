using System.Text;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Formats tag API responses.
/// </summary>
public static class TagFormatter
{
    public static string FormatTagFull(GrampsTag tag)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"TAG: {tag.Name} [handle: {tag.Handle}]");
        sb.AppendLine(new string('=', 60));

        if (!string.IsNullOrEmpty(tag.Color))
        {
            sb.AppendLine($"Color: {tag.Color}");
            sb.AppendLine($"       ■ (hex: {tag.Color})");
        }

        if (tag.Priority > 0)
            sb.AppendLine($"Priority: {tag.Priority}");

        return sb.ToString();
    }
}
