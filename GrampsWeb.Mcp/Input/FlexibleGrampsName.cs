using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Input;

/// <summary>
/// MCP parameter for one Gramps name (primary or single alternate). Accepts a full <see cref="GrampsName"/> JSON object
/// or a string: optional <c>NameType:: given|surname</c> or <c>NameType:: Jane Doe</c>; without <c>::</c>, same rules on the whole string.
/// </summary>
[JsonConverter(typeof(FlexibleGrampsNameJsonConverter))]
public sealed class FlexibleGrampsName
{
    public const string DescriptionHint =
        "Name: full Gramps name JSON (see gramps://input-guide), a plain string (\"Married Name:: Jane|Smith\"), " +
        "or a simple object. Object fields: " +
        "given/first (given name), " +
        "surname/last (primary surname value), " +
        "prefix/surname_prefix (particle before surname: von, de, van, del…), " +
        "connector/surname_connector (joins compound surnames: - or y), " +
        "origin_type/surname_origin (Gramps OriginType for the primary surname: " +
        "Inherited, Given, Taken, Patronymic, Matronymic, Feudal, Pseudonym, " +
        "Patrilineal, Matrilineal, Occupation, Location, Custom, Unknown), " +
        "patronymic (shortcut: stored as a separate Patronymic surname entry), " +
        "matronymic (shortcut: stored as a separate Matronymic surname entry), " +
        "title (Dr./Prof./Sir), suffix (Jr./Sr./III/PhD), " +
        "call (preferred call name), nick/nickname, famnick/family_nick, " +
        "type (name type: Birth Name/Married Name/Also Known As/…). " +
        "For complex multi-surname cases use native Gramps surname_list format. " +
        "Also accepts: name/text/full as a full-name string with optional type field.";

    public required GrampsName Name { get; init; }

    public static implicit operator GrampsName?(FlexibleGrampsName? value) => value?.Name;
}
