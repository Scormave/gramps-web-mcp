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
        "Alternate names: array of strings (\"Married Name:: Jane|Smith\") or name objects. " +
        "Object fields, as in primaryName: given (or first_name), surname, " +
        "prefix (von/de/van), connector, " +
        "origin_type (Gramps OriginType: Inherited, Given, Taken, Patronymic, Matronymic, " +
        "Feudal, Pseudonym, Patrilineal, Matrilineal, Occupation, Location, Unknown), " +
        "patronymic and matronymic (each a surname entry of its own), " +
        "title, suffix (Jr./Sr./III), call, nick, famnick, type (name type), " +
        "name (the whole name as one string), and native surname_list for several surnames. " +
        "Name types and origins must be standard Gramps ones or custom ones the tree already uses. " +
        "camelCase forms work too; any other field is an error, not ignored. " +
        "Multiple names in one string: separate with newlines (not |).";

    public required GrampsName[] Items { get; init; }

    public static implicit operator GrampsName[]?(FlexibleAlternateNameList? value) => value?.Items;
}
