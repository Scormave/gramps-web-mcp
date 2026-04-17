using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Input;

/// <summary>
/// MCP parameter for <c>alternate_names</c>. Accepts JSON array of <see cref="GrampsName"/> objects,
/// array of simple strings (same grammar as <see cref="FlexibleGrampsName"/> per entry),
/// one multiline JSON string (one name per line — do not use | between names), or embedded JSON array string.
/// </summary>
[JsonConverter(typeof(FlexibleAlternateNameListJsonConverter))]
public sealed class FlexibleAlternateNameList
{
    public const string DescriptionHint =
        "Alternate names: array of strings (\"Married Name:: Jane|Smith\"), simple objects, or full Gramps name objects. " +
        "Object fields: given/first, surname/last, " +
        "prefix/surname_prefix (von/de/van), connector/surname_connector, " +
        "origin_type/surname_origin (Gramps OriginType: Inherited, Given, Taken, Patronymic, Matronymic, " +
        "Feudal, Pseudonym, Patrilineal, Matrilineal, Occupation, Location, Custom, Unknown), " +
        "patronymic (→ separate Patronymic surname entry), " +
        "matronymic (→ separate Matronymic surname entry), " +
        "title, suffix (Jr./Sr./III), call, nick, famnick, type (name type). " +
        "For multi-surname names use native surname_list format. " +
        "Multiple names in one string: separate with newlines (not |).";

    public required GrampsName[] Items { get; init; }

    public static implicit operator GrampsName[]?(FlexibleAlternateNameList? value) => value?.Items;
}
