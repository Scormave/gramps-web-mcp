using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Input;

/// <summary>
/// MCP tool parameter naming the lists the <c>sort</c> argument of the update tools orders. Accepts a JSON array of
/// names or one string with the names separated by <c>|</c>, commas, semicolons or newlines. The names are checked
/// by <see cref="Tools.LinkOrder.Parse"/>, so any other JSON value reaches it as text and is rejected there.
/// </summary>
[JsonConverter(typeof(FlexibleSortListJsonConverter))]
public sealed class FlexibleSortList
{
    /// <summary>Human-readable hint for MCP tool parameter descriptions.</summary>
    public const string DescriptionHint =
        "JSON array [\"Events\",\"Citations\"] or one string such as \"Events | Citations\"; names are case-insensitive.";

    public required string[] Names { get; init; }
}
