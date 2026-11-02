using System.Text.Json;

namespace GrampsWeb.Mcp.Exceptions;

/// <summary>
/// A <see cref="JsonException"/> whose message tells the agent what to fix in a tool argument, such as an
/// unknown field in a name object. The SDK answers any other exception from argument binding with a bare
/// "An error occurred invoking"; the call-tool filter returns this message instead.
/// </summary>
public sealed class JsonInputException : JsonException
{
    public JsonInputException(string message) : base(message)
    {
    }

    public JsonInputException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
