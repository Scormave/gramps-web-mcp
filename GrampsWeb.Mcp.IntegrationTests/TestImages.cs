using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace GrampsWeb.Mcp.IntegrationTests;

/// <summary>
/// Small PNG files for upload tests. Each has a random fill, so its checksum is new to the tree and
/// create_media uploads it rather than reusing an earlier file. Written by hand to keep image
/// libraries out of the test project.
/// </summary>
internal static class TestImages
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>An 8-bit RGB PNG of one random colour.</summary>
    public static byte[] Png(int width = 64, int height = 48)
    {
        var colour = RandomNumberGenerator.GetBytes(3);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8; // bits per channel
        header[9] = 2; // RGB

        using var pixels = new MemoryStream();
        using (var zlib = new ZLibStream(pixels, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[1 + width * 3]; // filter type 0, then the pixels
            for (var x = 0; x < width; x++)
                colour.CopyTo(row, 1 + x * 3);
            for (var y = 0; y < height; y++)
                zlib.Write(row);
        }

        using var png = new MemoryStream();
        png.Write(Signature);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", pixels.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        stream.Write(number);

        var typeAndData = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, typeAndData);
        data.CopyTo(typeAndData, 4);
        stream.Write(typeAndData);

        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32(typeAndData));
        stream.Write(number);
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
}
