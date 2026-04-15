using System.ComponentModel;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;
using GrampsWeb.Mcp.Tools;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Resources;

/// <summary>
/// MCP resources with reference/discovery data used by agents before write operations.
/// </summary>
[McpServerResourceType]
public sealed class GrampsResources
{
    [McpServerResource(
        Name = "input-guide",
        UriTemplate = "gramps://input-guide",
        MimeType = "application/json")]
    [Description("Complete write-input reference: date formats, structured fields, and full Name schema.")]
    public static string GetInputGuide()
    {
        return BuildInputGuideText();
    }

    [McpServerResource(
        Name = "types",
        UriTemplate = "gramps://types",
        MimeType = "text/plain")]
    [Description("Read-only type vocabularies (built-in + custom) for validating type/role/origin strings.")]
    public static async Task<string> GetTypes(GrampsApiClient client)
    {
        try
        {
            var defaultRoot = await client.GetAsync<JsonElement>("/api/types/default/");
            var types = TypesPayloadParser.ParseCategories(defaultRoot);

            var customRoot = await client.GetAsync<JsonElement>("/api/types/custom/");
            var customTypes = TypesPayloadParser.ParseCategories(customRoot);

            foreach (var kvp in customTypes)
            {
                if (types.TryGetValue(kvp.Key, out var existing))
                {
                    var merged = existing.ToList();
                    merged.AddRange(kvp.Value);
                    types[kvp.Key] = merged;
                }
                else
                {
                    types[kvp.Key] = kvp.Value.ToList();
                }
            }

            return TypesFormatter.FormatTypesResponse(types);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerResource(
        Name = "metadata",
        UriTemplate = "gramps://metadata",
        MimeType = "text/plain")]
    [Description("Connection and tree metadata (API version, tree id/name, owner, default person, etc.).")]
    public static async Task<string> GetMetadata(GrampsApiClient client)
    {
        try
        {
            var metadata = await client.GetAsync<JsonElement>("/api/metadata/");
            string? defaultPersonFullName = null;
            if (metadata.TryGetProperty("default_person", out var defaultPersonEl)
                && defaultPersonEl.ValueKind == JsonValueKind.String)
            {
                var handle = defaultPersonEl.GetString();
                if (!string.IsNullOrEmpty(handle))
                {
                    try
                    {
                        var person = await client.GetAsync<GrampsPerson>(
                            $"/api/people/{Uri.EscapeDataString(handle)}");
                        if (person.PrimaryName != null)
                            defaultPersonFullName = GrampsValueFormatter.FormatName(person.PrimaryName);
                    }
                    catch
                    {
                        // Keep handle-only output if the person cannot be loaded.
                    }
                }
            }

            return SystemFormatter.FormatMetadata(metadata, defaultPersonFullName);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerResource(
        Name = "name-settings",
        UriTemplate = "gramps://name-settings",
        MimeType = "text/plain")]
    [Description("Name display format definitions and surname grouping rules configured in this tree.")]
    public static async Task<string> GetNameSettings(GrampsApiClient client)
    {
        try
        {
            var formats = await client.GetAsync<dynamic>("/api/name-formats/");
            var groups = await client.GetAsync<dynamic>("/api/name-groups/");
            return $"NAME FORMATS\n{new string('=', 60)}\n\n{JsonResponseFormatter.FormatDynamic(formats)}\n\n" +
                   $"NAME GROUPS\n{new string('=', 60)}\n\n{JsonResponseFormatter.FormatDynamic(groups)}";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    private static string BuildInputGuideText()
    {
        var guide = new
        {
            dates = BuildDateInputGuidePayload(),
            structured_fields = BuildStructuredFieldInputGuidePayload(),
            name_schema = BuildNameSchemaPayload()
        };
        return JsonSerializer.Serialize(guide, new JsonSerializerOptions { WriteIndented = true });
    }

    private static object BuildStructuredFieldInputGuidePayload() => new
    {
        overview =
            "Create/update tools accept either JSON arrays of Gramps-shaped objects or simpler strings. " +
            "A parameter may be a JSON array, a single JSON string with multiple lines or | separators, or (where noted) a string starting with [ containing a JSON array.",
        primary_and_alternate_names = new
        {
            primary = new
            {
                grammar =
                    "Full Gramps name object (see gramps://input-guide name schema), or one string. Optional Gramps name type uses double colon: \"Married Name:: Jane|Smith\" or \"Also Known As:: Mary Ann Jones\" (last space splits given and surname when | is absent).",
                examples = new[]
                {
                    "{\"first_name\":\"John\",\"surname_list\":[{\"surname\":\"Doe\",\"primary\":true}]}",
                    "John|Doe",
                    "John Doe",
                    "Birth Name:: Anna|Kovacs"
                },
                tools = "create_person (primaryName required), update_person (primaryName optional)"
            },
            alternate_names = new
            {
                grammar =
                    "JSON array of name objects or of simple strings (same rules as primary). One JSON string with multiple names: use newlines between names only - do not use | between names (| separates given|surname within one name).",
                examples = new[] { "[\"Also Known As:: Sue|Smith\", \"Nickname:: Red\"]", "Married Name:: Jane|Roe\\nAlso Known As:: Jane Doe" },
                tools = "create_person, update_person"
            }
        },
        attributes = new
        {
            grammar = "Type: Value - only the first colon separates type from value; the value may contain more colons.",
            examples = new[] { "Nickname: Joe", "Occupation: Farmer" },
            forms = new[]
            {
                "JSON array of objects {\"type\":\"T\",\"value\":\"V\",...}",
                "JSON array of strings [\"Nick: Joe\"]",
                "One JSON string: \"Line1\\nLine2\" or \"A: 1|B: 2\""
            },
            tools = "create_person, update_person, create_event, update_event, create_family, update_family, create_source, update_source, create_citation, update_citation, update_media"
        },
        urls = new
        {
            grammar =
                "Type: URL - first colon separates type from path. Optional description after path: em dash - or ASCII \" - \" (space hyphen space).",
            examples = new[] { "Web Home: http://example.com", "Web Home: https://x.org - my site" },
            forms = new[]
            {
                "JSON array of objects {\"type\",\"path\",\"desc\"}",
                "JSON array of strings or one multiline / |-separated string"
            },
            tools = "create_person, update_person only"
        },
        addresses = new
        {
            shortcut = "A single line without key: prefix sets street only (e.g. \"123 Main St\").",
            keyed = "Multiple lines per address: street:, locality:, city:, county:, state:, postal: or zip:, country:, phone: (case-insensitive keys).",
            multiple = "Separate addresses with a blank line or a line containing only ---.",
            forms = new[]
            {
                "JSON array of Gramps address objects",
                "JSON array of strings (each string is one address block)",
                "One multiline JSON string with blocks separated by blank line or ---"
            },
            tools = "create_person, update_person only"
        },
        person_associations = new
        {
            grammar =
                "HANDLE:: relationship - double colon after the related person's handle; relationship text is free-form (may include spaces).",
            examples = new[] { "a1b2c3d4e5f678901234567890abcd:: Godfather" },
            forms = new[]
            {
                "JSON array of objects {\"ref\":\"handle\",\"rel\":\"relationship\",...}",
                "JSON array of strings or one multiline / |-separated string"
            },
            tools = "create_person, update_person only (person_ref_list)"
        }
    };

    private static object BuildDateInputGuidePayload() => new
    {
        overview =
            "MCP tools take human-readable date strings. The server still receives Gramps Date JSON with dateval; the MCP layer parses your string.",
        preferred_iso = new[] { "yyyy-MM-dd", "yyyy-MM", "yyyy" },
        examples_iso = new[] { "1990-03-15", "1990-03", "1920" },
        date_component_order = new
        {
            Iso =
                "Default parser behavior in MCP write tools. Use hyphenated ISO for full dates. Slash or dot triplets (e.g. 15/03/1990 or 15.03.1990) are not accepted and produce validation errors.",
            DayMonthYear = "dd/MM/yyyy, dd-MM-yyyy, dd.MM.yyyy - day first.",
            MonthDayYear = "MM/dd/yyyy, MM-dd-yyyy, MM.dd.yyyy - US style."
        },
        modifiers = new
        {
            prefixes = new[] { "before ", "after ", "about ", "circa " },
            example = "before 1920"
        },
        year_ranges = new
        {
            dash_between_years = "1800-1850 (both parts 3-4 digit years)",
            between = "between 1800 and 1850",
            span = "from 1800 to 1850"
        },
        tools = new
        {
            events = "create_event / update_event - parameter date (string)",
            citations = "create_citation / update_citation - date",
            media = "update_media - date",
            persons =
                "create_person / update_person - gender: Female, Male, or Unknown"
        },
        fallback =
            "Strings that do not match structured patterns are stored as Gramps text-only dates (modifier 6)."
    };

    private static object BuildNameSchemaPayload() => new
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
                    description = "Name type from name_types (see gramps://types). Examples: 'Birth Name', 'Married Name', 'Also Known As', 'Patronymic'",
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
                    description = "Origin of surname (from name_origin_types via gramps://types). Examples: 'Inherited', 'Patronymic', 'Matronymic', 'Occupation', 'Location'",
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
            full_name_text = "Dr. Edwin Jose von der Smith and Weston Wilson Sr. (Jose) - Underhills",
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
}
