namespace GrampsWeb.Mcp.Client;

/// <summary>
/// Binary response body and content type returned by Gramps Web.
/// </summary>
public sealed record GrampsBinaryResponse(ReadOnlyMemory<byte> Bytes, string? MimeType);
