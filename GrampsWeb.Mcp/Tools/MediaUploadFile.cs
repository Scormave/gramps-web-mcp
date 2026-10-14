using System.Buffers.Binary;
using System.Security.Cryptography;
using GrampsWeb.Mcp.Config;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// A local file read for upload to Gramps Web: its real path, bytes, sniffed MIME type and the MD5
/// checksum Gramps uses to name and identify media files.
/// </summary>
internal sealed record MediaUploadFile(string Path, byte[] Bytes, string MimeType, string Checksum)
{
    /// <summary>The types create_media and update_media accept, as listed in errors.</summary>
    public const string SupportedTypes = "JPEG, PNG, GIF, WebP, TIFF, BMP or PDF";

    /// <summary>File name without its extension: the default description, as in Gramps Web's upload form.</summary>
    public string DefaultDescription => System.IO.Path.GetFileNameWithoutExtension(Path);

    /// <summary>
    /// Reads <paramref name="filePath"/> when it lies inside one of the configured upload folders.
    /// A relative path is looked up in each folder in turn. Symbolic links are resolved before the check,
    /// so a link cannot lead outside the folders, and the resolved path is the one read. The type comes from
    /// the file's first bytes, never from its name, so only photos, scans and PDFs are uploaded.
    /// </summary>
    public static async Task<MediaUploadFile> ReadAsync(string filePath, GrampsConfig config)
    {
        if (!config.MediaUploadEnabled)
            throw McpToolErrors.ValidationError(
                "Uploading files is turned off: set GRAMPS_MEDIA_UPLOAD_DIRS to the folders files may be uploaded from.");
        if (string.IsNullOrWhiteSpace(filePath))
            throw McpToolErrors.ValidationError("filePath is required.");

        var directories = config.MediaUploadDirs!
            .Where(Directory.Exists)
            .Select(RealPath)
            .ToArray();
        if (directories.Length == 0)
            throw McpToolErrors.ValidationError(
                $"None of the upload folders exists on the server: {string.Join(", ", config.MediaUploadDirs!)}.");

        var path = FindFile(filePath.Trim(), directories);
        var info = new FileInfo(path);
        if (!info.Exists)
            throw McpToolErrors.ValidationError(Directory.Exists(path)
                ? $"{path} is a folder; pass a file inside it."
                : $"{path} does not exist.");
        // Pipes and devices report no length; they would also block a read.
        if (info.Length == 0)
            throw McpToolErrors.ValidationError($"{path} is empty.");
        if (info.Length > config.MediaUploadMaxBytes)
            throw FileTooLarge(path, info.Length, config.MediaUploadMaxBytes);

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path);
        }
        catch (UnauthorizedAccessException)
        {
            throw McpToolErrors.ValidationError(
                $"The server is not allowed to read {path}. On macOS, allow the app that runs the server " +
                "(for example Claude) to access this folder in System Settings → Privacy & Security → Files and Folders.");
        }
        if (bytes.Length > config.MediaUploadMaxBytes)
            throw FileTooLarge(path, bytes.Length, config.MediaUploadMaxBytes);

        var mimeType = SniffMimeType(bytes)
            ?? throw McpToolErrors.ValidationError(
                $"{System.IO.Path.GetFileName(path)} is not a {SupportedTypes} file (judged by its content, not its name). " +
                "Upload other files in Gramps Web.");
        return new MediaUploadFile(path, bytes, mimeType, ChecksumOf(bytes));
    }

    /// <summary>
    /// Lowercase hex MD5 of the bytes, as Gramps stores in a media record's checksum.
    /// </summary>
#pragma warning disable CA5351 // Gramps identifies media files by MD5; this is a file identity, not a security check.
    public static string ChecksumOf(byte[] bytes) => Convert.ToHexStringLower(MD5.HashData(bytes));
#pragma warning restore CA5351

    /// <summary>The MIME type indicated by the file's leading bytes, or null for anything else.</summary>
    public static string? SniffMimeType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
            return "image/jpeg";
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]))
            return "image/png";
        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8))
            return "image/gif";
        if (bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
            return "image/webp";
        if (bytes.StartsWith((ReadOnlySpan<byte>)[(byte)'I', (byte)'I', 0x2A, 0x00])
            || bytes.StartsWith((ReadOnlySpan<byte>)[(byte)'M', (byte)'M', 0x00, 0x2A]))
            return "image/tiff";
        if (bytes.StartsWith("%PDF-"u8))
            return "application/pdf";
        // "BM" alone is too common a start; also require a known DIB header size.
        if (bytes.Length >= 18 && bytes.StartsWith("BM"u8)
            && BinaryPrimitives.ReadUInt32LittleEndian(bytes[14..18]) is 12 or 40 or 52 or 56 or 64 or 108 or 124)
            return "image/bmp";
        return null;
    }

    /// <summary>
    /// The real path of <paramref name="filePath"/> when it is inside one of <paramref name="directories"/>
    /// (themselves real paths); a relative path is tried in each folder until one has it.
    /// </summary>
    private static string FindFile(string filePath, string[] directories)
    {
        var expanded = GrampsConfig.ExpandHome(filePath);
        if (System.IO.Path.IsPathFullyQualified(expanded))
        {
            var real = RealPath(expanded);
            if (directories.Any(directory => IsInside(real, directory)))
                return real;
            throw McpToolErrors.ValidationError(
                $"{filePath} is outside the upload folders ({string.Join(", ", directories)}); only files there can be uploaded.");
        }

        string? firstInside = null;
        foreach (var directory in directories)
        {
            var real = RealPath(System.IO.Path.Combine(directory, expanded));
            if (!IsInside(real, directory))
                continue;
            if (File.Exists(real))
                return real;
            firstInside ??= real;
        }

        return firstInside ?? throw McpToolErrors.ValidationError(
            $"{filePath} is outside the upload folders ({string.Join(", ", directories)}); only files there can be uploaded.");
    }

    private static bool IsInside(string path, string directory)
    {
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (string.Equals(path, directory, comparison))
            return false;
        var prefix = System.IO.Path.EndsInDirectorySeparator(directory)
            ? directory
            : directory + System.IO.Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, comparison);
    }

    /// <summary>
    /// The absolute path with every symbolic link resolved, like realpath(3); missing trailing parts are kept
    /// as written. <c>..</c> is folded before links are followed, and callers read the returned path rather
    /// than the one they were given, so the checked path and the read file are the same.
    /// </summary>
    internal static string RealPath(string path) => ResolveLinks(path, depth: 0);

    private static string ResolveLinks(string path, int depth)
    {
        if (depth > 40)
            throw McpToolErrors.ValidationError($"{path}: too many levels of symbolic links.");

        var full = System.IO.Path.GetFullPath(path);
        var root = System.IO.Path.GetPathRoot(full)!;
        var current = root;
        var separators = new[] { System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar };
        foreach (var segment in full[root.Length..].Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var next = System.IO.Path.Combine(current, segment);
            FileSystemInfo entry = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
            if (entry.LinkTarget is { } target)
                next = ResolveLinks(System.IO.Path.IsPathFullyQualified(target) ? target : System.IO.Path.Combine(current, target), depth + 1);
            current = next;
        }

        return current;
    }

    private static Exception FileTooLarge(string path, long length, long maxBytes) =>
        McpToolErrors.ValidationError(
            $"{System.IO.Path.GetFileName(path)} is {length} bytes, more than the upload limit of {maxBytes} bytes (GRAMPS_MEDIA_UPLOAD_MAX_BYTES).");
}
