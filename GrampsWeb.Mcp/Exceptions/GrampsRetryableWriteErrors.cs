namespace GrampsWeb.Mcp.Exceptions;

internal static class GrampsRetryableWriteErrors
{
    public const int DefaultRetryAfterMs = 500;
    public const int DefaultRateLimitRetryAfterMs = 1000;

    public static string DatabaseLocked(int retryAfterMs = DefaultRetryAfterMs) =>
        "Gramps Web database is locked (SQLite single-writer). " +
        "Wait briefly, then retry this same write; do not retry immediately. " +
        $"Retry after ~{retryAfterMs}ms.";

    public static string RateLimited(int retryAfterMs) =>
        "Gramps Web rate-limited this write (HTTP 429). " +
        "Wait briefly, then retry this same write; do not retry immediately. " +
        $"Retry after ~{retryAfterMs}ms.";

    public static string GateTimeout(int retryAfterMs = DefaultRetryAfterMs) =>
        "Another write is still in progress (single-writer queue timed out). " +
        "Wait briefly, then retry this same write; do not retry immediately. " +
        $"Retry after ~{retryAfterMs}ms.";
}
