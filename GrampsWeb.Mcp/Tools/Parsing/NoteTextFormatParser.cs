namespace GrampsWeb.Mcp.Tools.Parsing;

/// <summary>
/// Maps note text format names to Gramps Web <c>format</c> integer (0=plain, 1=flowed/HTML).
/// </summary>
internal static class NoteTextFormatParser
{
    internal static int ParseRequired(string format)
    {
        if (string.IsNullOrWhiteSpace(format))
            throw McpToolErrors.ValidationError("Note format is required: use Plain or Html.");

        switch (format.Trim().ToLowerInvariant())
        {
            case "plain":
            case "plaintext":
            case "text":
                return 0;
            case "html":
            case "formatted":
                return 1;
            default:
                throw McpToolErrors.ValidationError(
                    $"Invalid note format '{format}'. Use Plain or Html (case-insensitive).");
        }
    }

    internal static int? ParseOptional(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
            return null;
        return ParseRequired(format);
    }
}
