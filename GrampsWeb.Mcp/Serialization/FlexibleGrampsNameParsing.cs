using System.Text.Json;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>Parses simple string forms into <see cref="GrampsName"/>; full objects are deserialized via JSON elsewhere.</summary>
public static class FlexibleGrampsNameParsing
{
    /// <summary>
    /// Optional Gramps name type (e.g. Birth Name, Married Name) as <c>Label:: remainder</c> (double colon).
    /// Remainder: <c>given|surname</c> (first pipe) or Western <c>given … surname</c> (last space).
    /// Single word: stored as <c>first_name</c> with one primary surname entry (empty string).
    /// </summary>
    public static GrampsName ParseSimpleLine(string line)
    {
        line = line.Trim();
        if (string.IsNullOrEmpty(line))
            throw new JsonException("Name line is empty.");

        string? type = null;
        var body = line;
        var dc = line.IndexOf("::", StringComparison.Ordinal);
        if (dc >= 0)
        {
            type = line[..dc].Trim();
            body = line[(dc + 2)..].Trim();
            if (string.IsNullOrEmpty(body))
                throw new JsonException(
                    $"Name type prefix \"{type}\" must be followed by text after ::.");
        }

        string first;
        string sur;

        var pipe = body.IndexOf('|');
        if (pipe >= 0)
        {
            first = body[..pipe].Trim();
            sur = body[(pipe + 1)..].Trim();
        }
        else
        {
            var lastSp = body.LastIndexOf(' ');
            if (lastSp > 0)
            {
                first = body[..lastSp].Trim();
                sur = body[(lastSp + 1)..].Trim();
            }
            else
            {
                first = body;
                sur = "";
            }
        }

        return new GrampsName
        {
            Type = string.IsNullOrEmpty(type) ? null : type,
            FirstName = first,
            SurnameList =
            [
                new GrampsSurname { Surname = sur, Primary = true }
            ]
        };
    }

    /// <summary>
    /// Parses a <see cref="JsonElement"/> that is a JSON object into a <see cref="GrampsName"/>.
    /// Handles both native Gramps field names (<c>first_name</c>, <c>surname_list</c>) and
    /// AI-friendly aliases (<c>given</c>, <c>surname</c>, <c>patronymic</c>, <c>name</c>, <c>text</c>).
    /// When AI sends aliases, builds the correct Gramps structure automatically.
    /// Returns null when the element contains no usable name data.
    /// </summary>
    public static GrampsName? ParseObjectElement(JsonElement el, JsonSerializerOptions options)
    {
        // If the object already uses native Gramps field names, deserialize directly.
        if (el.TryGetProperty("first_name", out _) || el.TryGetProperty("surname_list", out _))
            return el.Deserialize<GrampsName>(options);

        // Try AI-friendly "name" / "text" / "full" / "full_name" fields — parse as a simple string.
        foreach (var key in (string[])["name", "text", "full", "full_name"])
        {
            if (el.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                var str = prop.GetString();
                if (string.IsNullOrWhiteSpace(str))
                    continue;
                var parsed = ParseSimpleLine(str.Trim());
                // Overlay explicit type if provided alongside.
                var typeStr = GetStringProp(el, "type");
                if (!string.IsNullOrWhiteSpace(typeStr))
                    parsed.Type = typeStr;
                return parsed;
            }
        }

        // ── Component fields ────────────────────────────────────────────────
        // Name-level
        var given      = GetStringPropAny(el, "given", "first");
        var title      = GetStringProp(el, "title");
        var suffix     = GetStringProp(el, "suffix");
        var nick       = GetStringPropAny(el, "nick", "nickname");
        var call       = GetStringProp(el, "call");
        var famNick    = GetStringPropAny(el, "famnick", "family_nick", "family_nickname");
        var typeVal    = GetStringProp(el, "type");

        // Primary surname — value + optional prefix, connector, Gramps OriginType
        // Standard Gramps OriginType values: Inherited, Given, Taken, Patronymic, Matronymic,
        //   Feudal, Pseudonym, Patrilineal, Matrilineal, Occupation, Location, Custom, Unknown
        var surname       = GetStringPropAny(el, "surname", "last", "family_name");
        var surnamePrefix = GetStringPropAny(el, "prefix", "surname_prefix"); // von, de, van, del…
        var surnameConn   = GetStringPropAny(el, "connector", "surname_connector"); // - or y
        var surnameOrigin = GetStringPropAny(el, "origin_type", "surname_origin");

        // Patronymic / matronymic shortcuts — convenience for Slavic/Nordic traditions.
        // Creates a separate surname_list entry with the corresponding Gramps OriginType.
        var patronymic = GetStringProp(el, "patronymic");
        var matronymic = GetStringProp(el, "matronymic");

        bool anyData = !string.IsNullOrEmpty(given)
            || !string.IsNullOrEmpty(surname)
            || !string.IsNullOrEmpty(patronymic) || !string.IsNullOrEmpty(matronymic);
        if (!anyData)
            return null; // no useful data — let caller fall back

        // ── Build surname_list ───────────────────────────────────────────────
        var surnameList = new List<GrampsSurname>();

        if (!string.IsNullOrWhiteSpace(surname))
        {
            surnameList.Add(new GrampsSurname
            {
                Surname    = surname,
                Prefix     = string.IsNullOrWhiteSpace(surnamePrefix) ? null : surnamePrefix,
                Connector  = string.IsNullOrWhiteSpace(surnameConn)   ? null : surnameConn,
                OriginType = string.IsNullOrWhiteSpace(surnameOrigin) ? null : surnameOrigin,
                Primary    = true
            });
        }

        // Patronymic — separate non-primary entry with OriginType "Patronymic"
        if (!string.IsNullOrWhiteSpace(patronymic))
        {
            surnameList.Add(new GrampsSurname
            {
                Surname    = patronymic,
                Primary    = surnameList.Count == 0,
                OriginType = "Patronymic"
            });
        }

        // Matronymic — separate non-primary entry with OriginType "Matronymic"
        if (!string.IsNullOrWhiteSpace(matronymic))
        {
            surnameList.Add(new GrampsSurname
            {
                Surname    = matronymic,
                Primary    = surnameList.Count == 0,
                OriginType = "Matronymic"
            });
        }

        // Ensure at least one surname entry exists (Gramps requires it)
        if (surnameList.Count == 0)
            surnameList.Add(new GrampsSurname { Surname = "", Primary = true });

        return new GrampsName
        {
            Type      = string.IsNullOrWhiteSpace(typeVal)  ? null : typeVal,
            Title     = string.IsNullOrWhiteSpace(title)    ? null : title,
            FirstName = string.IsNullOrWhiteSpace(given)    ? null : given,
            Call      = string.IsNullOrWhiteSpace(call)     ? null : call,
            Nick      = string.IsNullOrWhiteSpace(nick)     ? null : nick,
            FamNick   = string.IsNullOrWhiteSpace(famNick)  ? null : famNick,
            Suffix    = string.IsNullOrWhiteSpace(suffix)   ? null : suffix,
            SurnameList = surnameList.ToArray()
        };
    }

    private static string? GetStringProp(JsonElement el, string key) =>
        el.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static string? GetStringPropAny(JsonElement el, params string[] keys)
    {
        foreach (var k in keys)
        {
            var v = GetStringProp(el, k);
            if (v != null)
                return v;
        }
        return null;
    }
}
