namespace GrampsWeb.Mcp.Models;

/// <summary>One backlinks category from <c>?backlinks=true</c> responses (handles only, sorted).</summary>
public sealed record BacklinkGroup(string Key, string Title, IReadOnlyList<string> Handles);
