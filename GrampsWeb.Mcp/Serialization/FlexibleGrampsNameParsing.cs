using System.Text.Json;
using System.Text.Json.Nodes;
using GrampsWeb.Mcp.Exceptions;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>Parses the string and object forms of a name argument into <see cref="GrampsName"/>.</summary>
public static class FlexibleGrampsNameParsing
{
    // Name object fields, keyed by the sent name in lower case without underscores so that
    // first_name, firstName and FirstName all reach the same field. Values are the canonical
    // field: a native Gramps key, or one of the shortcuts surname, prefix, connector,
    // origin_type, patronymic, matronymic and name.
    private static readonly Dictionary<string, string> NameFields = new(StringComparer.Ordinal)
    {
        ["given"] = "first_name",
        ["first"] = "first_name",
        ["firstname"] = "first_name",
        ["givenname"] = "first_name",
        ["surname"] = "surname",
        ["last"] = "surname",
        ["lastname"] = "surname",
        ["familyname"] = "surname",
        ["prefix"] = "prefix",
        ["surnameprefix"] = "prefix",
        ["connector"] = "connector",
        ["surnameconnector"] = "connector",
        ["origintype"] = "origin_type",
        ["surnameorigin"] = "origin_type",
        ["patronymic"] = "patronymic",
        ["matronymic"] = "matronymic",
        ["name"] = "name",
        ["text"] = "name",
        ["full"] = "name",
        ["fullname"] = "name",
        ["surnamelist"] = "surname_list",
        ["title"] = "title",
        ["suffix"] = "suffix",
        ["call"] = "call",
        ["nick"] = "nick",
        ["nickname"] = "nick",
        ["famnick"] = "famnick",
        ["familynick"] = "famnick",
        ["familynickname"] = "famnick",
        ["type"] = "type",
        ["date"] = "date",
        ["citationlist"] = "citation_list",
        ["notelist"] = "note_list",
        ["groupas"] = "group_as",
        ["sortas"] = "sort_as",
        ["displayas"] = "display_as",
        ["private"] = "private"
    };

    // Surname entry fields inside surname_list, keyed the same way.
    private static readonly Dictionary<string, string> SurnameFields = new(StringComparer.Ordinal)
    {
        ["surname"] = "surname",
        ["prefix"] = "prefix",
        ["connector"] = "connector",
        ["origintype"] = "origintype",
        ["primary"] = "primary"
    };

    // Shortcut fields that build the surname list; every other name field is passed to Gramps as sent.
    private static readonly string[] ShortcutFields =
        ["surname", "prefix", "connector", "origin_type", "patronymic", "matronymic", "name", "surname_list"];

    private static readonly string[] StringNameFields =
    [
        "first_name", "surname", "prefix", "connector", "origin_type", "patronymic", "matronymic", "name",
        "title", "suffix", "call", "nick", "famnick", "group_as"
    ];

    private const string AcceptedNameFields =
        "given (or first_name), surname, prefix, connector, origin_type, patronymic, matronymic, " +
        "name (the whole name as one string), title, suffix, call, nick, famnick, type, surname_list, " +
        "and the other Gramps name fields date, citation_list, note_list, group_as, sort_as, display_as, private. " +
        "camelCase forms such as firstName, surnameList and originType work too";

    private const string AcceptedSurnameFields = "surname, prefix, connector, origintype (or origin_type), primary";

    /// <summary>
    /// Parses a name from a string that is either a JSON object (when it starts with <c>{</c>)
    /// or a simple name line (<see cref="ParseSimpleLine"/>).
    /// </summary>
    public static GrampsName ParseNameLineOrJsonObject(string line, JsonSerializerOptions options)
    {
        line = line.Trim();
        if (line.Length == 0 || line[0] != '{')
            return ParseSimpleLine(line);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(line);
        }
        catch (JsonException ex)
        {
            throw new JsonInputException($"Invalid JSON for name object: {ex.Message}", ex);
        }

        using (doc)
            return ParseObjectElement(doc.RootElement, options);
    }

    /// <summary>
    /// Parses a name string that may contain multiple lines; the first non-empty line wins.
    /// </summary>
    public static GrampsName ParseStringValue(string s, JsonSerializerOptions options)
    {
        foreach (var raw in s.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.Length > 0)
                return ParseNameLineOrJsonObject(raw, options);
        }

        return ParseNameLineOrJsonObject(s.Trim(), options);
    }

    /// <summary>
    /// Optional Gramps name type (e.g. Birth Name, Married Name) as <c>Label:: remainder</c> (double colon).
    /// Remainder: <c>given|surname</c> (first pipe) or Western <c>given … surname</c> (last space).
    /// Single word: stored as <c>first_name</c> with one primary surname entry (empty string).
    /// </summary>
    public static GrampsName ParseSimpleLine(string line)
    {
        line = line.Trim();
        if (string.IsNullOrEmpty(line))
            throw new JsonInputException("Name line is empty.");

        string? type = null;
        var body = line;
        var dc = line.IndexOf("::", StringComparison.Ordinal);
        if (dc >= 0)
        {
            type = line[..dc].Trim();
            body = line[(dc + 2)..].Trim();
            if (string.IsNullOrEmpty(body))
                throw new JsonInputException(
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
    /// Parses a name object. Accepts the native Gramps fields (<c>first_name</c>, <c>surname_list</c>, …)
    /// in snake_case or camelCase, and the shortcuts <c>given</c>, <c>surname</c>, <c>prefix</c>,
    /// <c>connector</c>, <c>origin_type</c>, <c>patronymic</c>, <c>matronymic</c> and <c>name</c>
    /// (the whole name as one string), which build the surname list. Both kinds can be mixed.
    /// Throws <see cref="JsonInputException"/> for unknown or conflicting fields and for an object with no name,
    /// so nothing the caller sent is dropped silently.
    /// </summary>
    public static GrampsName ParseObjectElement(JsonElement el, JsonSerializerOptions options)
    {
        var fields = ReadFields(el, NameFields, "name object", AcceptedNameFields);
        foreach (var field in StringNameFields)
        {
            if (fields.TryGetValue(field, out var f) && f.Value.ValueKind != JsonValueKind.String)
                throw new JsonInputException(
                    $"Field \"{f.Key}\" in name object must be a string, not {Describe(f.Value.ValueKind)}.");
        }

        string KeyOf(string field) => fields[field].Key;
        string? Str(string field) => fields.TryGetValue(field, out var f) ? f.Value.GetString() : null;

        var fullName = Str("name");
        if (fullName != null)
        {
            var clash = ((string[])["first_name", "surname", "surname_list"]).Where(fields.ContainsKey).Select(KeyOf).ToList();
            if (clash.Count > 0)
                throw new JsonInputException(
                    $"Field \"{KeyOf("name")}\" holds the whole name, so it cannot be combined with {Quote(clash)}; " +
                    "send either the whole name or its parts.");
        }

        var qualifiers = ((string[])["prefix", "connector", "origin_type"]).Where(fields.ContainsKey).ToList();
        if (fields.ContainsKey("surname_list"))
        {
            var clash = qualifiers.Prepend("surname").Where(fields.ContainsKey).Select(KeyOf).ToList();
            if (clash.Count > 0)
                throw new JsonInputException(
                    $"Field \"{KeyOf("surname_list")}\" cannot be combined with {Quote(clash)}; " +
                    "put them in the surname_list entries instead.");
        }
        else if (qualifiers.Count > 0 && fullName == null && !fields.ContainsKey("surname"))
        {
            throw new JsonInputException(
                $"{Quote(qualifiers.Select(KeyOf))} describe the surname, so the name object needs \"surname\" as well.");
        }

        var native = new JsonObject();
        foreach (var (field, f) in fields)
        {
            if (!ShortcutFields.Contains(field))
                native[field] = JsonNode.Parse(f.Value.GetRawText());
        }

        var name = Deserialize<GrampsName>(native, "name object", options);
        var surnames = fields.TryGetValue("surname_list", out var list)
            ? ParseSurnameList(list.Key, list.Value, options)
            : [];

        var surname = Str("surname");
        if (fullName != null)
        {
            var parsed = ParseSimpleLine(fullName);
            name.FirstName = parsed.FirstName;
            name.Type ??= parsed.Type;
            surname = parsed.SurnameList![0].Surname;
        }

        if (!fields.ContainsKey("surname_list") && (!string.IsNullOrEmpty(surname) || qualifiers.Count > 0))
        {
            surnames.Add(new GrampsSurname
            {
                Surname = surname ?? "",
                Prefix = Str("prefix"),
                Connector = Str("connector"),
                OriginType = Str("origin_type"),
                Primary = true
            });
        }

        // Patronymic and matronymic become surname entries of their own, primary only when nothing else is.
        foreach (var (field, origin) in ((string, string)[])[("patronymic", "Patronymic"), ("matronymic", "Matronymic")])
        {
            if (Str(field) is { } value)
                surnames.Add(new GrampsSurname { Surname = value, OriginType = origin, Primary = surnames.Count == 0 });
        }

        if (string.IsNullOrWhiteSpace(name.FirstName) && surnames.All(s => string.IsNullOrWhiteSpace(s.Surname)))
            throw new JsonInputException(
                "Name object has no name in it: send given and surname, patronymic, matronymic, " +
                "name (the whole name as one string) or surname_list.");

        // Gramps needs at least one surname entry, even an empty one.
        if (surnames.Count == 0)
            surnames.Add(new GrampsSurname { Surname = "", Primary = true });

        name.SurnameList = surnames.ToArray();
        return name;
    }

    private static List<GrampsSurname> ParseSurnameList(string key, JsonElement list, JsonSerializerOptions options)
    {
        if (list.ValueKind != JsonValueKind.Array)
            throw new JsonInputException(
                $"Field \"{key}\" in name object must be an array of surname objects, not {Describe(list.ValueKind)}.");

        var surnames = new List<GrampsSurname>();
        var primarySent = new List<bool>();
        foreach (var item in list.EnumerateArray())
        {
            var where = $"surname_list[{surnames.Count}]";
            if (item.ValueKind != JsonValueKind.Object)
                throw new JsonInputException(
                    $"Entry {where} must be an object such as {{\"surname\": \"Smith\"}}, not {Describe(item.ValueKind)}.");

            var fields = ReadFields(item, SurnameFields, where, AcceptedSurnameFields);
            foreach (var field in (string[])["surname", "prefix", "connector"])
            {
                if (fields.TryGetValue(field, out var f) && f.Value.ValueKind != JsonValueKind.String)
                    throw new JsonInputException(
                        $"Field \"{f.Key}\" in {where} must be a string, not {Describe(f.Value.ValueKind)}.");
            }

            if (fields.TryGetValue("primary", out var p) && p.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new JsonInputException(
                    $"Field \"{p.Key}\" in {where} must be true or false, not {Describe(p.Value.ValueKind)}.");

            var entry = new JsonObject();
            foreach (var (field, f) in fields)
                entry[field] = JsonNode.Parse(f.Value.GetRawText());
            surnames.Add(Deserialize<GrampsSurname>(entry, where, options));
            primarySent.Add(fields.ContainsKey("primary"));
        }

        // An entry without "primary" is the primary surname only when it is the first such entry
        // and no entry says "primary": true; a list of two plain entries gets one primary surname, not two.
        var hasPrimary = surnames.Where((_, i) => primarySent[i]).Any(s => s.Primary);
        for (var i = 0; i < surnames.Count; i++)
        {
            if (primarySent[i])
                continue;
            surnames[i].Primary = !hasPrimary;
            hasPrimary = true;
        }

        return surnames;
    }

    /// <summary>
    /// Maps each property of <paramref name="el"/> to its canonical field. Skips <c>_class</c>, nulls and blank strings;
    /// throws for unknown fields and for two properties that set the same field.
    /// </summary>
    private static Dictionary<string, (string Key, JsonElement Value)> ReadFields(
        JsonElement el, Dictionary<string, string> aliases, string where, string accepted)
    {
        var fields = new Dictionary<string, (string Key, JsonElement Value)>(StringComparer.Ordinal);
        var unknown = new List<string>();
        foreach (var prop in el.EnumerateObject())
        {
            if (prop.Name == "_class")
                continue;
            if (!aliases.TryGetValue(prop.Name.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant(), out var field))
            {
                unknown.Add(prop.Name);
                continue;
            }

            if (prop.Value.ValueKind == JsonValueKind.Null
                || prop.Value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(prop.Value.GetString()))
                continue;
            if (fields.TryGetValue(field, out var earlier))
                throw new JsonInputException(
                    $"Fields \"{earlier.Key}\" and \"{prop.Name}\" in {where} set the same thing; send only one of them.");
            fields[field] = (prop.Name, prop.Value);
        }

        if (unknown.Count > 0)
            throw new JsonInputException(
                $"Unknown {(unknown.Count == 1 ? "field" : "fields")} {Quote(unknown)} in {where}. Accepted fields: {accepted}.");
        return fields;
    }

    private static T Deserialize<T>(JsonObject node, string where, JsonSerializerOptions options)
    {
        try
        {
            return node.Deserialize<T>(options)!;
        }
        catch (JsonException ex)
        {
            throw new JsonInputException($"Invalid value in {where}: {ex.Message}", ex);
        }
    }

    private static string Quote(IEnumerable<string> keys) => string.Join(", ", keys.Select(k => $"\"{k}\""));

    private static string Describe(JsonValueKind kind) => kind switch
    {
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "an array",
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        _ => "null"
    };
}
