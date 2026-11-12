namespace GrampsWeb.Mcp.Exceptions;

/// <summary>
/// The connected Gramps Web server runs an API older than <see cref="Client.GrampsServerVersion.Minimum"/>.
/// The call-tool filter returns its message to the agent from every tool, including tools that catch only
/// <see cref="GrampsApiException"/>.
/// </summary>
public sealed class UnsupportedGrampsServerException : InvalidOperationException
{
    public UnsupportedGrampsServerException(string message) : base(message)
    {
    }
}
