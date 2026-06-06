using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Input;

/// <summary>
/// MCP tool parameter for scalar text values. Accepts JSON strings and JSON numbers,
/// preserving either shape as the text sent to Gramps Web.
/// </summary>
[JsonConverter(typeof(FlexibleStringJsonConverter))]
public sealed class FlexibleString
{
    public const string DescriptionHint =
        "Text value. JSON strings are accepted; JSON numbers are also accepted and sent as text.";

    public required string Value { get; init; }

    public override string ToString() => Value;

    public static implicit operator string?(FlexibleString? value) => value?.Value;
}
