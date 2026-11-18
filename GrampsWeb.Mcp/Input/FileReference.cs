using System.ComponentModel;
using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Input;

/// <summary>
/// A file given by a temporary download link instead of a path: the shape ChatGPT passes for a file the user
/// attached in the chat, in a tool argument named in the tool's <c>openai/fileParams</c> metadata. The server
/// downloads it (<see cref="Tools.MediaDownloader"/>); only <see cref="DownloadUrl"/> and <see cref="FileId"/>
/// are always present.
/// </summary>
public sealed class FileReference
{
    /// <summary>The argument name create_media and update_media take a file reference in.</summary>
    public const string ArgumentName = "file";

    /// <summary>Tool metadata naming the file arguments, for <c>McpMeta.JsonValue</c>.</summary>
    public const string FileParamsMeta = "openai/fileParams";

    /// <summary>The value of <see cref="FileParamsMeta"/>: a JSON array of the file argument names.</summary>
    public const string FileParamsJson = "[\"" + ArgumentName + "\"]";

    [JsonPropertyName("download_url")]
    [Description("Temporary HTTPS link to the file's content.")]
    public required string DownloadUrl { get; init; }

    [JsonPropertyName("file_id")]
    [Description("The chat client's ID of the file.")]
    public required string FileId { get; init; }

    [JsonPropertyName("mime_type")]
    [Description("The type the client reports; the server judges the type by content instead.")]
    public string? MimeType { get; init; }

    [JsonPropertyName("file_name")]
    [Description("The attached file's name; the default description is this name without its extension.")]
    public string? FileName { get; init; }
}
