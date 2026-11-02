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
        "Name: a plain string (\"Married Name:: Jane|Smith\", \"Given Middle Surname\") or a JSON object " +
        "(not a quoted JSON string). Object fields: " +
        "given (or first_name), " +
        "surname (primary surname value), " +
        "prefix (particle before the surname: von, de, van, del…), " +
        "connector (joins compound surnames: - or y), " +
        "origin_type (Gramps OriginType of that surname: " +
        "Inherited, Given, Taken, Patronymic, Matronymic, Feudal, Pseudonym, " +
        "Patrilineal, Matrilineal, Occupation, Location, Unknown), " +
        "patronymic and matronymic (each stored as a surname entry of its own with that origin), " +
        "title (Dr./Prof./Sir), suffix (Jr./Sr./III/PhD), " +
        "call (preferred call name), nick, famnick, " +
        "type (name type: Birth Name/Married Name/Also Known As/…), " +
        "name (the whole name as one string, instead of given and surname), " +
        "and native Gramps fields such as surname_list for several surnames " +
        "([{surname, prefix, connector, origintype, primary}], see gramps://input-guide). " +
        "Name types and origins must be standard Gramps ones or custom ones the tree already uses. " +
        "camelCase forms (firstName, surnameList, originType) work too; any other field is an error, not ignored. " +
        "A plain string only splits into given name and surname: " +
        "use object fields for a patronymic, prefix, title and the like.";

    public required GrampsName Name { get; init; }

    public static implicit operator GrampsName?(FlexibleGrampsName? value) => value?.Name;
}
