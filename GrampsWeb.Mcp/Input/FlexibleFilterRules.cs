using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Input;

/// <summary>
/// MCP tool parameter holding Gramps filter rules: a JSON object or array, or the same JSON as a string, as clients
/// send either. The value is kept as given and checked by <see cref="Tools.FilterRules.Build"/>, so any other JSON
/// value reaches it and is rejected there.
/// </summary>
[JsonConverter(typeof(FlexibleFilterRulesJsonConverter))]
public sealed class FlexibleFilterRules
{
    public required JsonElement Value { get; init; }
}
