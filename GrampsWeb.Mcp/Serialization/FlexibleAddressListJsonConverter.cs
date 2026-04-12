using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Serialization;

public sealed class FlexibleAddressListJsonConverter : JsonConverter<FlexibleAddressList?>
{
    public override FlexibleAddressList? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return ParseFromString(reader.GetString(), options);
            case JsonTokenType.StartArray:
                return ReadArray(ref reader, options);
            default:
                throw new JsonException(
                    $"Unexpected token {reader.TokenType} for address list; use JSON array, string, or null.");
        }
    }

    public override void Write(Utf8JsonWriter writer, FlexibleAddressList? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(writer, value.Items, options);
    }

    private static FlexibleAddressList? ParseFromString(string? s, JsonSerializerOptions options)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;

        s = s.Trim();
        if (s.Length > 0 && s[0] == '[')
        {
            try
            {
                using var doc = JsonDocument.Parse(s);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return null;
                return ReadFromJsonArray(doc.RootElement, options);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        var blocks = SplitAddressBlocks(s);
        var list = blocks.Select(ParseAddressBlock).ToArray();
        return new FlexibleAddressList { Items = list };
    }

    internal static List<string> SplitAddressBlocks(string s)
    {
        var blocks = new List<string>();
        var current = new List<string>();
        foreach (var rawLine in s.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0 || line == "---")
            {
                if (current.Count > 0)
                {
                    blocks.Add(string.Join("\n", current));
                    current.Clear();
                }
            }
            else
                current.Add(line);
        }

        if (current.Count > 0)
            blocks.Add(string.Join("\n", current));

        return blocks;
    }

    internal static bool IsKeyedAddressLine(string line)
    {
        var idx = line.IndexOf(':');
        if (idx <= 0)
            return false;
        var key = line[..idx].Trim();
        return key.Length > 0 && key.All(c => char.IsLetter(c) || c == '_');
    }

    internal static GrampsAddress ParseAddressBlock(string block)
    {
        block = block.Trim();
        if (string.IsNullOrEmpty(block))
            throw new JsonException("Empty address block.");

        var lines = block.Split('\n')
            .Select(l => l.TrimEnd('\r').Trim())
            .Where(l => l.Length > 0)
            .ToArray();

        if (lines.Length == 0)
            throw new JsonException("Empty address block.");

        var addr = new GrampsAddress();

        if (lines.Length == 1 && !IsKeyedAddressLine(lines[0]))
        {
            addr.Street = lines[0];
            return addr;
        }

        foreach (var line in lines)
        {
            if (!IsKeyedAddressLine(line))
                throw new JsonException($"Expected \"field: value\" address line, got: \"{line}\"");

            var idx = line.IndexOf(':');
            var key = line[..idx].Trim().ToLowerInvariant();
            var val = line[(idx + 1)..].Trim();

            switch (key)
            {
                case "street":
                    addr.Street = val;
                    break;
                case "locality":
                    addr.Locality = val;
                    break;
                case "city":
                    addr.City = val;
                    break;
                case "county":
                    addr.County = val;
                    break;
                case "state":
                    addr.State = val;
                    break;
                case "country":
                    addr.Country = val;
                    break;
                case "postal":
                case "zip":
                    addr.Postal = val;
                    break;
                case "phone":
                    addr.Phone = val;
                    break;
                default:
                    throw new JsonException(
                        $"Unknown address field \"{key}\". Use: street, locality, city, county, state, postal, country, phone.");
            }
        }

        return addr;
    }

    private static FlexibleAddressList ReadArray(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var list = new List<GrampsAddress>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                {
                    var seg = reader.GetString();
                    if (string.IsNullOrWhiteSpace(seg))
                        break;
                    list.Add(ParseAddressBlock(seg.Trim()));
                    break;
                }
                case JsonTokenType.StartObject:
                {
                    var a = JsonSerializer.Deserialize<GrampsAddress>(ref reader, options);
                    if (a != null)
                        list.Add(a);
                    break;
                }
                default:
                    reader.Skip();
                    break;
            }
        }

        return new FlexibleAddressList { Items = list.ToArray() };
    }

    private static FlexibleAddressList ReadFromJsonArray(JsonElement array, JsonSerializerOptions options)
    {
        var list = new List<GrampsAddress>();
        foreach (var el in array.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                var seg = el.GetString();
                if (string.IsNullOrWhiteSpace(seg))
                    continue;
                list.Add(ParseAddressBlock(seg.Trim()));
            }
            else if (el.ValueKind == JsonValueKind.Object)
            {
                var a = el.Deserialize<GrampsAddress>(options);
                if (a != null)
                    list.Add(a);
            }
        }

        return new FlexibleAddressList { Items = list.ToArray() };
    }
}
