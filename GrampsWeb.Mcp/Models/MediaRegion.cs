using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace GrampsWeb.Mcp.Models;

/// <summary>
/// The part of an image a media link shows (Gramps <c>MediaRef.rect</c>): its left, top, right and bottom edges
/// in whole percent of the image width and height, measured from the top-left corner.
/// </summary>
public sealed record MediaRegion(int Left, int Top, int Right, int Bottom)
{
    /// <summary>Human-readable hint for MCP tool parameter descriptions.</summary>
    public const string DescriptionHint =
        "A region is [left, top, right, bottom]: its edges in percent of the image width and height, 0 to 100, " +
        "measured from the top-left corner; [10, 20, 60, 45] runs from 10% to 60% across and from 20% to 45% down.";

    private static readonly char[] Separators = [',', ';', ' ', '\t', '\n', '\r'];

    public static MediaRegion WholeImage { get; } = new(0, 0, 100, 100);

    public bool IsWholeImage => this == WholeImage;

    public int Width => Right - Left;

    public int Height => Bottom - Top;

    /// <summary>The Gramps <c>rect</c> value.</summary>
    public int[] ToArray() => [Left, Top, Right, Bottom];

    /// <summary><c>[left, top, right, bottom]</c>, the form the tools accept.</summary>
    public override string ToString() => $"[{Left}, {Top}, {Right}, {Bottom}]";

    /// <summary>The region a stored link shows; null when it shows the whole image or its <c>rect</c> is not a region.</summary>
    public static MediaRegion? FromStored(int[]? rect) =>
        rect is { Length: 4 } && TryCreate(rect.Select(v => (double)v).ToArray(), out var region, out _) && !region.IsWholeImage
            ? region
            : null;

    /// <summary>
    /// Checks four edges in percent and rounds them to whole percent, as Gramps stores them.
    /// <paramref name="error"/> describes the problem without naming the argument.
    /// </summary>
    public static bool TryCreate(
        IReadOnlyList<double> edges,
        [NotNullWhen(true)] out MediaRegion? region,
        [NotNullWhen(false)] out string? error)
    {
        region = null;
        if (edges.Count != 4)
        {
            error = $"a region needs 4 numbers [left, top, right, bottom], not {edges.Count}.";
            return false;
        }

        foreach (var edge in edges)
        {
            if (edge is >= 0 and <= 100)
                continue;
            error = $"region edges are percent of the image, 0 to 100; {Format(edge)} is outside that.";
            return false;
        }

        var rounded = edges.Select(e => (int)Math.Round(e, MidpointRounding.AwayFromZero)).ToArray();
        if (rounded[0] >= rounded[2] || rounded[1] >= rounded[3])
        {
            error = $"region [{string.Join(", ", edges.Select(Format))}] is empty: left must be less than right and top less than bottom, " +
                    "at least 1% apart.";
            return false;
        }

        region = new MediaRegion(rounded[0], rounded[1], rounded[2], rounded[3]);
        error = null;
        return true;
    }

    /// <summary>
    /// Reads a region from JSON: an array of four numbers, or a string such as <c>"10,20,60,45"</c> or <c>"[10, 20, 60, 45]"</c>.
    /// JSON null gives no region and no error.
    /// </summary>
    public static bool TryRead(JsonElement element, out MediaRegion? region, [NotNullWhen(false)] out string? error)
    {
        region = null;
        error = null;
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return true;
            case JsonValueKind.Array:
            {
                var edges = new List<double>();
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Number)
                        edges.Add(item.GetDouble());
                    else if (item.ValueKind == JsonValueKind.String && TryParseNumber(item.GetString(), out var number))
                        edges.Add(number);
                    else
                    {
                        error = $"region edges must be numbers, not {item.GetRawText()}.";
                        return false;
                    }
                }

                if (!TryCreate(edges, out var created, out error))
                    return false;
                region = created;
                return true;
            }
            case JsonValueKind.String:
            {
                var parts = (element.GetString() ?? "").Trim().TrimStart('[').TrimEnd(']')
                    .Split(Separators, StringSplitOptions.RemoveEmptyEntries);
                var edges = new List<double>();
                foreach (var part in parts)
                {
                    if (!TryParseNumber(part, out var number))
                    {
                        error = $"region edges must be numbers, not \"{part}\".";
                        return false;
                    }

                    edges.Add(number);
                }

                if (!TryCreate(edges, out var created, out error))
                    return false;
                region = created;
                return true;
            }
            default:
                error = $"a region is an array [left, top, right, bottom], not {element.GetRawText()}.";
                return false;
        }
    }

    private static bool TryParseNumber(string? text, out double number) =>
        double.TryParse(text?.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out number);

    private static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);
}
