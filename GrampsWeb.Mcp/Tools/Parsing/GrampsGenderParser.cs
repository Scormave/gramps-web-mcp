namespace GrampsWeb.Mcp.Tools.Parsing;

/// <summary>
/// Maps agent-facing gender names to Gramps Web API integers (0=Female, 1=Male, 2=Unknown).
/// </summary>
internal static class GrampsGenderParser
{
    internal static int ParseRequired(string gender)
    {
        if (string.IsNullOrWhiteSpace(gender))
            throw McpToolErrors.ValidationError("Gender is required: use Female, Male, or Unknown.");

        switch (gender.Trim().ToLowerInvariant())
        {
            case "female":
                return 0;
            case "male":
                return 1;
            case "unknown":
                return 2;
            default:
                throw McpToolErrors.ValidationError(
                    $"Invalid gender '{gender}'. Use Female, Male, or Unknown (case-insensitive).");
        }
    }

    /// <summary>Null or whitespace → no update; otherwise same rules as <see cref="ParseRequired"/>.</summary>
    internal static int? ParseOptional(string? gender)
    {
        if (string.IsNullOrWhiteSpace(gender))
            return null;
        return ParseRequired(gender);
    }
}
