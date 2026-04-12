using System.Text.Json;
using GrampsWeb.Mcp.Dates;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Reads/writes Gramps Web API <c>Date</c> JSON (calendar, modifier, quality, dateval, text, newyear, sortval).
/// </summary>
internal static class GrampsDateWireCodec
{
    internal static void ReadIntoGrampsDate(ref Utf8JsonReader reader, GrampsDate target)
    {
        ReadInto(ref reader, target, includeSortVal: true);
    }

    internal static void ReadIntoDateRequest(ref Utf8JsonReader reader, DateRequest target)
    {
        ReadInto(ref reader, target, includeSortVal: false);
    }

    private static void ReadInto(ref Utf8JsonReader reader, object target, bool includeSortVal)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected start of object for Date.");

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return;

            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            var name = reader.GetString();
            reader.Read();

            switch (name)
            {
                case "calendar":
                    SetCalendar(target, reader.GetInt32());
                    break;
                case "modifier":
                    SetModifier(target, reader.GetInt32());
                    break;
                case "quality":
                    SetQuality(target, reader.GetInt32());
                    break;
                case "newyear":
                    SetNewYear(target, reader.GetInt32());
                    break;
                case "text":
                    SetText(target, reader.TokenType == JsonTokenType.Null ? null : reader.GetString());
                    break;
                case "sortval" when includeSortVal && target is GrampsDate gd:
                    if (reader.TokenType == JsonTokenType.Null)
                        gd.SortVal = null;
                    else
                        gd.SortVal = reader.GetInt32();
                    break;
                case "dateval":
                    ParseDateVal(reader, target);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
    }

    private static void SetCalendar(object o, int v)
    {
        if (o is GrampsDate d) d.Calendar = v;
        else if (o is DateRequest r) r.Calendar = v;
    }

    private static void SetModifier(object o, int v)
    {
        if (o is GrampsDate d) d.Modifier = v;
        else if (o is DateRequest r) r.Modifier = v;
    }

    private static void SetQuality(object o, int v)
    {
        if (o is GrampsDate d) d.Quality = v;
        else if (o is DateRequest r) r.Quality = v;
    }

    private static void SetNewYear(object o, int v)
    {
        if (o is GrampsDate d) d.NewYear = v;
        else if (o is DateRequest r) r.NewYear = v;
    }

    private static void SetText(object o, string? v)
    {
        if (o is GrampsDate d) d.Text = v;
        else if (o is DateRequest r) r.Text = v;
    }

    private static void ParseDateVal(Utf8JsonReader reader, object target)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            ClearSegments(target);
            return;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return;
        }

        var parts = new List<object?>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            parts.Add(ReadDateValElement(ref reader));
        }

        ApplyDateValParts(parts, target);
    }

    private static object? ReadDateValElement(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.TryGetInt64(out var l) ? l : null;
            case JsonTokenType.True:
                return true;
            case JsonTokenType.False:
                return false;
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.Null:
                return null;
            default:
                reader.Skip();
                return null;
        }
    }

    private static void ClearSegments(object target)
    {
        SetPrimary(target, 0, 0, 0, false);
        SetSecondary(target, 0, 0, 0, false);
    }

    private static void ApplyDateValParts(List<object?> parts, object target)
    {
        if (parts.Count < 4)
        {
            ClearSegments(target);
            return;
        }

        SetPrimary(
            target,
            ToInt(parts[0]),
            ToInt(parts[1]),
            ToInt(parts[2]),
            ToBool(parts[3]));

        if (parts.Count >= 8)
        {
            SetSecondary(
                target,
                ToInt(parts[4]),
                ToInt(parts[5]),
                ToInt(parts[6]),
                ToBool(parts[7]));
        }
        else
            SetSecondary(target, 0, 0, 0, false);
    }

    private static void SetPrimary(object o, int day, int month, int year, bool slash)
    {
        if (o is GrampsDate d)
        {
            d.Day = day;
            d.Month = month;
            d.Year = year;
            d.Slash = slash;
        }
        else if (o is DateRequest r)
        {
            r.Day = day;
            r.Month = month;
            r.Year = year;
            r.Slash = slash;
        }
    }

    private static void SetSecondary(object o, int day, int month, int year, bool slash)
    {
        if (o is GrampsDate d)
        {
            d.EndDay = day;
            d.EndMonth = month;
            d.EndYear = year;
            d.EndSlash = slash;
        }
        else if (o is DateRequest r)
        {
            r.EndDay = day;
            r.EndMonth = month;
            r.EndYear = year;
            r.EndSlash = slash;
        }
    }

    private static int ToInt(object? part) => part switch
    {
        int i => i,
        long l => (int)l,
        JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetInt32(),
        JsonElement je when je.ValueKind == JsonValueKind.String && int.TryParse(je.GetString(), out var x) => x,
        string s when int.TryParse(s, out var x) => x,
        double d => (int)d,
        _ => 0
    };

    private static bool ToBool(object? part) => part switch
    {
        bool b => b,
        int i => i != 0,
        long l => l != 0,
        JsonElement je when je.ValueKind == JsonValueKind.True => true,
        JsonElement je when je.ValueKind == JsonValueKind.False => false,
        _ => false
    };

    internal static void WriteGrampsDate(Utf8JsonWriter writer, GrampsDate value, JsonSerializerOptions options)
    {
        WriteCore(writer, value.Calendar, value.Modifier, value.Quality, value.Text, value.NewYear,
            value.Day, value.Month, value.Year, value.Slash,
            value.EndDay, value.EndMonth, value.EndYear, value.EndSlash,
            writeSortVal: true, sortVal: value.SortVal, options);
    }

    internal static void WriteDateRequest(Utf8JsonWriter writer, DateRequest value, JsonSerializerOptions options)
    {
        var sortVal = GrampsDateSortVal.TryComputeForDateRequest(value);
        WriteCore(writer, value.Calendar, value.Modifier, value.Quality, value.Text, value.NewYear,
            value.Day, value.Month, value.Year, value.Slash,
            value.EndDay, value.EndMonth, value.EndYear, value.EndSlash,
            writeSortVal: sortVal.HasValue, sortVal: sortVal, options);
    }

    private static void WriteCore(
        Utf8JsonWriter writer,
        int calendar, int modifier, int quality, string? text, int newYear,
        int day, int month, int year, bool slash,
        int endDay, int endMonth, int endYear, bool endSlash,
        bool writeSortVal, int? sortVal,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteNumber("calendar", calendar);
        writer.WriteNumber("modifier", modifier);
        writer.WriteNumber("quality", quality);
        writer.WriteNumber("newyear", newYear);

        if (text != null)
            writer.WriteString("text", text);

        WriteDateValArray(writer, modifier, day, month, year, slash, endDay, endMonth, endYear, endSlash);

        if (writeSortVal && sortVal.HasValue)
            writer.WriteNumber("sortval", sortVal.Value);

        writer.WriteEndObject();
    }

    private static void WriteDateValArray(
        Utf8JsonWriter writer,
        int modifier,
        int day, int month, int year, bool slash,
        int endDay, int endMonth, int endYear, bool endSlash)
    {
        writer.WritePropertyName("dateval");
        writer.WriteStartArray();
        writer.WriteNumberValue(day);
        writer.WriteNumberValue(month);
        writer.WriteNumberValue(year);
        writer.WriteBooleanValue(slash);

        if (modifier is 4 or 5)
        {
            writer.WriteNumberValue(endDay);
            writer.WriteNumberValue(endMonth);
            writer.WriteNumberValue(endYear);
            writer.WriteBooleanValue(endSlash);
        }

        writer.WriteEndArray();
    }
}
