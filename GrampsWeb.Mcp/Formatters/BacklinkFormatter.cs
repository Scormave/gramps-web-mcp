using System.Collections.Generic;
using System.Text;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>Appends "Referenced by …" sections for <c>get_*</c> tool output.</summary>
public static class BacklinkFormatter
{
    private static readonly Dictionary<string, string> CanonicalKeyToSingularLabel =
        new(StringComparer.Ordinal)
        {
            ["people"] = "person",
            ["families"] = "family",
            ["events"] = "event",
            ["places"] = "place",
            ["sources"] = "source",
            ["citations"] = "citation",
            ["media"] = "media",
            ["notes"] = "note",
            ["repositories"] = "repository",
        };

    public static void AppendReferencedBySections(StringBuilder sb, IReadOnlyList<BacklinkGroup>? backlinks)
    {
        if (backlinks == null || backlinks.Count == 0)
            return;

        foreach (var g in backlinks)
        {
            if (g.Handles.Count == 0)
                continue;
            var singular = CanonicalKeyToSingularLabel.TryGetValue(g.Key, out var s)
                ? s
                : g.Key;
            sb.AppendLine();
            sb.AppendLine($"Referenced by {g.Title} ({g.Handles.Count}):");
            foreach (var h in g.Handles)
                sb.AppendLine($"  • {singular} [handle: {h}]");
        }
    }
}
