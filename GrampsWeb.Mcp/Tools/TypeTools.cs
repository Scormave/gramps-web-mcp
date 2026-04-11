using System.ComponentModel;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Serialization;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// MCP Server tools for retrieving Gramps type vocabularies, name schemas, and the date-string input guide.
/// These are essential for validating data before create/update operations.
/// </summary>
[McpServerToolType]
public static class TypeTools
{
    /// <summary>
    /// Returns all built-in Gramps type vocabularies from /api/types/default/.
    /// ALWAYS call this before any create or update operation to get valid type values.
    /// </summary>
    [McpServerTool]
    [Description(
        "Returns all built-in Gramps type vocabularies including event_types, place_types, " +
        "note_types, repository_types, source_media_types, family_relation_types, " +
        "child_reference_types, event_role_types, name_types, name_origin_types, and more. " +
        "ALWAYS call this before any create_person, create_event, create_place or other create/update operations " +
        "to ensure you use only valid type strings. Using non-standard type strings corrupts the database.")]
    public static async Task<string> GetTypes(GrampsApiClient client)
    {
        try
        {
            var root = await client.GetAsync<JsonElement>("/api/types/default/");
            var types = TypesPayloadParser.ParseCategories(root);
            return TypesFormatter.FormatTypesResponse(types);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    /// <summary>
    /// Returns user-defined custom types added to this specific Gramps database.
    /// Call alongside get_types() for the complete type vocabulary.
    /// </summary>
    [McpServerTool]
    [Description(
        "Returns user-defined custom types that have been added to this specific Gramps database. " +
        "Custom types extend the standard built-in types from get_types(). " +
        "Call this alongside get_types() to get the complete vocabulary for your database.")]
    public static async Task<string> GetCustomTypes(GrampsApiClient client)
    {
        try
        {
            var root = await client.GetAsync<JsonElement>("/api/types/custom/");
            var customTypes = TypesPayloadParser.ParseCategories(root);
            return TypesFormatter.FormatCustomTypesResponse(customTypes);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    /// <summary>
    /// Documents how to pass dates as strings in MCP create/update tools (no Gramps <c>dateval</c> arrays).
    /// </summary>
    [McpServerTool]
    [Description(
        "Returns the authoritative guide for date strings in MCP tools: ISO and regional formats, " +
        "dateComponentOrder (Iso / DayMonthYear / MonthDayYear), modifiers (before, after, about, circa), " +
        "year ranges, separators (- / .), and primaryNameDate for persons. " +
        "Call before create_event, update_event, create_citation, update_citation, update_media, or when setting person name dates.")]
    public static Task<string> GetDateInputGuide()
    {
        var json = JsonSerializer.Serialize(BuildDateInputGuidePayload(), new JsonSerializerOptions { WriteIndented = true });
        return Task.FromResult(json);
    }

    private static object BuildDateInputGuidePayload() => new
    {
        overview =
            "MCP tools take human-readable date strings. The server still receives Gramps Date JSON with dateval; the MCP layer parses your string.",
        preferred_iso = new[] { "yyyy-MM-dd", "yyyy-MM", "yyyy" },
        examples_iso = new[] { "1990-03-15", "1990-03", "1920" },
        date_component_order = new
        {
            Iso =
                "Default. Use hyphenated ISO only for full dates. Slash or dot triplets (e.g. 15/03/1990 or 15.03.1990) require DayMonthYear or MonthDayYear or you get a validation error.",
            DayMonthYear = "dd/MM/yyyy, dd-MM-yyyy, dd.MM.yyyy — day first.",
            MonthDayYear = "MM/dd/yyyy, MM-dd-yyyy, MM.dd.yyyy — US style."
        },
        modifiers = new
        {
            prefixes = new[] { "before ", "after ", "about ", "circa " },
            example = "before 1920"
        },
        year_ranges = new
        {
            dash_between_years = "1800-1850 (both parts 3–4 digit years)",
            between = "between 1800 and 1850",
            span = "from 1800 to 1850"
        },
        tools = new
        {
            events = "create_event / update_event — parameter date (string) + dateComponentOrder",
            citations = "create_citation / update_citation — date + dateComponentOrder",
            media = "update_media — date + dateComponentOrder",
            persons =
                "create_person / update_person — primaryNameDate (string) + primaryNameDateOrder; gender: Female, Male, or Unknown (not integers)"
        },
        fallback =
            "Strings that do not match structured patterns are stored as Gramps text-only dates (modifier 6)."
    };

    /// <summary>
    /// Returns the complete Name object schema with field descriptions, constraints, and an example.
    /// ALWAYS call this before create_person or update_person to understand the name structure.
    /// Gramps supports multiple surnames with prefix, connector, and origin type per person.
    /// </summary>
    [McpServerTool]
    [Description(
        "Returns the complete Name object schema with field descriptions and constraints. " +
        "ALWAYS call this before create_person or update_person. " +
        "Gramps supports complex names with multiple surnames, prefixes, connectors, and origin types. " +
        "JSON field names match the Gramps Web API (e.g. call, nick, famnick, first_name, surname_list).")]
    public static Task<string> GetNameSchema()
    {
        var schema = new
        {
            name_object = new
            {
                type = "Object",
                description = "Represents a person's name in Gramps Web. On Person objects use primary_name plus optional alternate_names array.",
                fields = new
                {
                    type = new
                    {
                        type = "string",
                        description = "Name type from name_types (get_types). Examples: 'Birth Name', 'Married Name', 'Also Known As', 'Patronymic'",
                        examples = new[] { "Birth Name", "Married Name", "Also Known As" }
                    },
                    first_name = new
                    {
                        type = "string",
                        description = "Given names (all of them together). Example: 'Edwin Jose'",
                        examples = new[] { "Edwin Jose", "John", "Mary Anne" }
                    },
                    call = new
                    {
                        type = "string",
                        description = "The name by which the person is commonly called. Example: 'Jose' when first_name is 'Edwin Jose'",
                        examples = new[] { "Jose", "Bob", "Betty" }
                    },
                    nick = new
                    {
                        type = "string",
                        description = "Nickname or short form. Example: 'Ed' for Edwin",
                        examples = new[] { "Ed", "Liz", "Rob" }
                    },
                    famnick = new
                    {
                        type = "string",
                        description = "Family nickname. Example: 'Underhills'",
                        examples = new[] { "Underhills", "The Smiths" }
                    },
                    title = new
                    {
                        type = "string",
                        description = "Title prefix. Example: 'Dr.', 'Rev.', 'Sir', 'Count'",
                        examples = new[] { "Dr.", "Rev.", "Sir", "Count" }
                    },
                    suffix = new
                    {
                        type = "string",
                        description = "Suffix after name. Example: 'Jr.', 'III', 'Sr.', 'Ph.D.'",
                        examples = new[] { "Jr.", "III", "Sr.", "Ph.D." }
                    },
                    surname_list = new
                    {
                        type = "Array of Surname",
                        description = "One or more surnames. Most people have one primary surname, but Gramps allows multiples.",
                        constraint = "Must have at least one surname",
                        example_structure = "See surname_object schema below"
                    }
                }
            },
            surname_object = new
            {
                type = "Object",
                description = "Represents a single surname within a Name object.",
                fields = new
                {
                    surname = new
                    {
                        type = "string",
                        description = "The surname itself. Examples: 'Smith', 'van der Berg', 'O'Brien'",
                        examples = new[] { "Smith", "van der Berg", "O'Brien" }
                    },
                    prefix = new
                    {
                        type = "string",
                        description = "Prefix not used for sorting. Examples: 'von', 'de', 'van', 'von der'",
                        examples = new[] { "von", "de", "van", "von der" }
                    },
                    connector = new
                    {
                        type = "string",
                        description = "Connector between surnames. Examples: 'and', 'y', '-'",
                        examples = new[] { "and", "y", "-" }
                    },
                    origintype = new
                    {
                        type = "string",
                        description = "Origin of surname (from name_origin_types via get_types()). Examples: 'Inherited', 'Patronymic', 'Matronymic', 'Occupation', 'Location'",
                        examples = new[] { "Inherited", "Patronymic", "Matronymic", "Occupation", "Location" }
                    },
                    primary = new
                    {
                        type = "boolean",
                        description = "Is this the primary surname for sorting/display? Usually true for the first surname.",
                        examples = new[] { "true", "false" }
                    }
                }
            },
            parsing_example = new
            {
                full_name_text = "Dr. Edwin Jose von der Smith and Weston Wilson Sr. (Jose) — Underhills",
                parsed = new
                {
                    title = "Dr.",
                    first_name = "Edwin Jose",
                    call = "Jose",
                    nick = "",
                    famnick = "Underhills",
                    suffix = "Sr.",
                    surname_list = new object[]
                    {
                        new
                        {
                            surname = "Smith and Weston",
                            prefix = "von der",
                            connector = "and",
                            origintype = "Inherited",
                            primary = true
                        },
                        new
                        {
                            surname = "Wilson",
                            prefix = "",
                            connector = "",
                            origintype = "Patronymic",
                            primary = false
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true });
        return Task.FromResult(json);
    }

}
