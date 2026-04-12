namespace GrampsWeb.Mcp.Tools.Parsing;

/// <summary>
/// Maps citation confidence labels to Gramps integers 0–4 (same order as <see cref="GrampsWeb.Mcp.Formatters.CitationFormatter.ConfidenceLabels"/>).
/// </summary>
internal static class CitationConfidenceParser
{
    internal static int ParseRequired(string confidence)
    {
        if (string.IsNullOrWhiteSpace(confidence))
            throw McpToolErrors.ValidationError(
                "Confidence is required: use Very Low, Low, Normal, High, or Very High.");

        var key = Normalize(confidence);
        return key switch
        {
            "verylow" => 0,
            "low" => 1,
            "normal" => 2,
            "high" => 3,
            "veryhigh" => 4,
            _ => throw McpToolErrors.ValidationError(
                $"Invalid confidence '{confidence}'. Use Very Low, Low, Normal, High, or Very High (case-insensitive).")
        };
    }

    internal static int? ParseOptional(string? confidence)
    {
        if (string.IsNullOrWhiteSpace(confidence))
            return null;
        return ParseRequired(confidence);
    }

    private static string Normalize(string confidence)
    {
        var trimmed = confidence.Trim().ToLowerInvariant();
        return trimmed.Replace(" ", "", StringComparison.Ordinal);
    }
}
