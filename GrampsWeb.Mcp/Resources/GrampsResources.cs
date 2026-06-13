using System.ComponentModel;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;
using GrampsWeb.Mcp.Tools;
using ModelContextProtocol.Protocol;
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
    public static Task<TextResourceContents> GetInputGuide()
    {
        return Task.FromResult(new TextResourceContents
        {
            Uri = "gramps://input-guide",
            MimeType = "application/json",
            Text = BuildInputGuideText()
        });
    }

    [McpServerResource(
        Name = "types",
        UriTemplate = "gramps://types",
        MimeType = "text/plain")]
    [Description("Read-only type vocabularies (built-in + custom) for validating type/role/origin strings.")]
    public static async Task<TextResourceContents> GetTypes(GrampsApiClient client)
    {
        try
        {
            return new TextResourceContents
            {
                Uri = "gramps://types",
                MimeType = "text/plain",
                Text = await FetchTypesTextAsync(client)
            };
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
    public static async Task<TextResourceContents> GetMetadata(GrampsApiClient client)
    {
        try
        {
            return new TextResourceContents
            {
                Uri = "gramps://metadata",
                MimeType = "text/plain",
                Text = await FetchMetadataTextAsync(client)
            };
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
    public static async Task<TextResourceContents> GetNameSettings(GrampsApiClient client)
    {
        try
        {
            return new TextResourceContents
            {
                Uri = "gramps://name-settings",
                MimeType = "text/plain",
                Text = await FetchNameSettingsTextAsync(client)
            };
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerResource(
        Name = "media-file",
        UriTemplate = "gramps://media/{handle}/file",
        MimeType = "application/octet-stream")]
    [Description(
        "Opt-in binary media file bytes for vision-capable clients. " +
        "Prefer thumbnails for AI analysis when full resolution is not required.")]
    public static async Task<BlobResourceContents> GetMediaFile(
        string handle,
        GrampsApiClient client,
        GrampsConfig config)
    {
        try
        {
            EnsureMediaResourcesEnabled(config);
            EnsureMediaHandle(handle);
            var media = await GetMediaMetadataOrThrowAsync(handle, client);
            EnsurePrivateAllowed(media, config);
            EnsureMimeAllowed(media.Mime, config);

            var escapedHandle = Uri.EscapeDataString(handle);
            var binary = await client.GetBytesAsync(
                $"/api/media/{escapedHandle}/file",
                config.MediaMaxBytes);
            var mimeType = EffectiveMimeType(binary.MimeType, media.Mime);
            EnsureMimeAllowed(mimeType, config);

            return BlobResourceContents.FromBytes(
                binary.Bytes,
                $"gramps://media/{escapedHandle}/file",
                mimeType);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerResource(
        Name = "media-thumbnail",
        UriTemplate = "gramps://media/{handle}/thumbnail/{size}",
        MimeType = "image/*")]
    [Description(
        "Opt-in binary media thumbnail bytes for vision-capable clients. " +
        "Recommended before requesting full-resolution genealogy media.")]
    public static async Task<BlobResourceContents> GetMediaThumbnail(
        string handle,
        int size,
        GrampsApiClient client,
        GrampsConfig config)
    {
        try
        {
            EnsureMediaResourcesEnabled(config);
            EnsureMediaHandle(handle);
            if (size <= 0)
                throw McpToolErrors.ValidationError("Thumbnail size must be a positive integer.");

            var media = await GetMediaMetadataOrThrowAsync(handle, client);
            EnsurePrivateAllowed(media, config);

            var escapedHandle = Uri.EscapeDataString(handle);
            var binary = await client.GetBytesAsync(
                $"/api/media/{escapedHandle}/thumbnail/{size}",
                config.MediaMaxBytes);
            var mimeType = EffectiveMimeType(binary.MimeType, "image/jpeg");
            EnsureMimeAllowed(mimeType, config);

            return BlobResourceContents.FromBytes(
                binary.Bytes,
                $"gramps://media/{escapedHandle}/thumbnail/{size}",
                mimeType);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    internal static async Task<string> FetchTypesTextAsync(GrampsApiClient client)
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

    internal static async Task<string> FetchMetadataTextAsync(GrampsApiClient client)
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

    internal static async Task<string> FetchNameSettingsTextAsync(GrampsApiClient client)
    {
        var formats = await client.GetAsync<dynamic>("/api/name-formats/");
        var groups = await client.GetAsync<dynamic>("/api/name-groups/");
        return $"NAME FORMATS\n{new string('=', 60)}\n\n{JsonResponseFormatter.FormatDynamic(formats)}\n\n" +
               $"NAME GROUPS\n{new string('=', 60)}\n\n{JsonResponseFormatter.FormatDynamic(groups)}";
    }

    internal static async Task<GrampsMedia> GetMediaMetadataOrThrowAsync(string handle, GrampsApiClient client)
    {
        var media = await client.GetOrNullIfNotFoundAsync<GrampsMedia>(
            $"/api/media/{Uri.EscapeDataString(handle)}");

        if (media == null)
            throw McpToolErrors.ValidationError($"Media not found: {handle}");

        return media;
    }

    internal static void EnsureMediaHandle(string handle)
    {
        if (string.IsNullOrWhiteSpace(handle))
            throw McpToolErrors.ValidationError("Media handle must not be empty.");
    }

    internal static void EnsureMediaResourcesEnabled(GrampsConfig config)
    {
        if (!config.MediaResourcesEnabled)
            throw McpToolErrors.ValidationError(
                "Media file resources are disabled. Set GRAMPS_MEDIA_RESOURCES_ENABLED=true to expose media bytes.");
    }

    internal static void EnsurePrivateAllowed(GrampsMedia media, GrampsConfig config)
    {
        if (media.Private && !config.MediaAllowPrivate)
            throw McpToolErrors.ValidationError(
                "Media file resources are blocked for private media records. Set GRAMPS_MEDIA_ALLOW_PRIVATE=true to allow them.");
    }

    internal static void EnsureMimeAllowed(string? mimeType, GrampsConfig config)
    {
        var normalized = NormalizeMimeType(mimeType);
        if (normalized == null)
            throw McpToolErrors.ValidationError("Media MIME type is missing and cannot be checked against the allowlist.");

        if (!config.EffectiveMediaAllowedMimeTypes.Any(allowed => MimeMatches(normalized, allowed)))
            throw McpToolErrors.ValidationError(
                $"Media MIME type '{normalized}' is not allowed by GRAMPS_MEDIA_ALLOWED_MIME_TYPES.");
    }

    internal static string EffectiveMimeType(string? responseMimeType, string? fallbackMimeType)
    {
        return NormalizeMimeType(responseMimeType)
               ?? NormalizeMimeType(fallbackMimeType)
               ?? "application/octet-stream";
    }

    private static string? NormalizeMimeType(string? mimeType)
    {
        var normalized = mimeType?.Split(';', 2)[0].Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized;
    }

    private static bool MimeMatches(string actual, string allowed)
    {
        var normalizedAllowed = NormalizeMimeType(allowed);
        if (normalizedAllowed == null)
            return false;

        if (normalizedAllowed.EndsWith("/*", StringComparison.Ordinal))
        {
            var prefix = normalizedAllowed[..^1];
            return actual.StartsWith(prefix, StringComparison.Ordinal);
        }

        return actual.Equals(normalizedAllowed, StringComparison.Ordinal);
    }

    internal static string BuildInputGuideText()
    {
        var guide = new
        {
            dates = BuildDateInputGuidePayload(),
            structured_fields = BuildStructuredFieldInputGuidePayload(),
            name_schema = BuildNameSchemaPayload()
        };
        return JsonSerializer.Serialize(guide, new JsonSerializerOptions { WriteIndented = true });
    }

    internal static object BuildStructuredFieldInputGuidePayload() => new
    {
        overview =
            "Create/update tools accept either JSON arrays of Gramps-shaped objects or simpler strings. " +
            "A parameter may be a JSON array, a single JSON string with multiple lines or | separators, or (where noted) a string starting with [ containing a JSON array.",
        primary_and_alternate_names = new
        {
            primary = new
            {
                grammar =
                    "Accepts three formats: " +
                    "(1) Simple string — optional 'NameType:: given|surname' or 'given surname' (last word is surname). " +
                    "(2) Simple object with AI-friendly fields (see object_fields below). " +
                    "(3) Full native Gramps name object with first_name + surname_list (see name_schema).",
                object_fields = new
                {
                    given       = "Given/first name(s)",
                    surname     = "Primary surname value (aliases: last, family_name)",
                    prefix      = "Particle before surname: von, de, van, del… (alias: surname_prefix)",
                    connector   = "Connector between compound surnames: - or y (alias: surname_connector)",
                    origin_type = "Gramps OriginType of primary surname (alias: surname_origin). " +
                                  "Values: Inherited, Given, Taken, Patronymic, Matronymic, Feudal, " +
                                  "Pseudonym, Patrilineal, Matrilineal, Occupation, Location, Custom, Unknown",
                    patronymic  = "Shortcut: creates a separate surname entry with OriginType=Patronymic",
                    matronymic  = "Shortcut: creates a separate surname entry with OriginType=Matronymic",
                    title       = "Name title: Dr., Prof., Sir, Count…",
                    suffix      = "Name suffix: Jr., Sr., III, PhD…",
                    call        = "Preferred call name (different from nick)",
                    nick        = "Nickname (alias: nickname)",
                    famnick     = "Family nickname (alias: family_nick)",
                    type        = "Name type: Birth Name, Married Name, Also Known As, Patronymic… " +
                                  "See gramps://types for full list",
                    name        = "Shortcut: full-name string parsed as 'given surname', with optional type field (aliases: text, full)"
                },
                examples = new[]
                {
                    "John|Doe",
                    "Birth Name:: Anna|Kovacs",
                    "{\"given\":\"Ludwig\",\"surname\":\"Beethoven\",\"prefix\":\"van\",\"type\":\"Birth Name\"}",
                    "{\"given\":\"Ivan\",\"surname\":\"Petrov\",\"patronymic\":\"Petrovich\",\"type\":\"Birth Name\"}",
                    "{\"title\":\"Dr.\",\"given\":\"John\",\"surname\":\"Smith\",\"suffix\":\"Jr.\",\"call\":\"Jack\"}",
                    "{\"first_name\":\"John\",\"surname_list\":[{\"surname\":\"Doe\",\"primary\":true}]}"
                },
                tools = "create_person (primaryName required), update_person (primaryName optional)"
            },
            alternate_names = new
            {
                grammar =
                    "JSON array where each item uses the same three formats as primaryName (string, simple object, or native Gramps object). " +
                    "One multiline string: separate names with newlines — do not use | between names (| separates given|surname within one name).",
                examples = new[]
                {
                    "[\"Also Known As:: Sue|Smith\", \"Nickname:: Red\"]",
                    "Married Name:: Jane|Roe\\nAlso Known As:: Jane Doe",
                    "[{\"given\":\"Johnny\",\"surname\":\"Smith\",\"type\":\"Also Known As\"}]",
                    "[{\"given\":\"Ivan\",\"patronymic\":\"Petrovich\",\"type\":\"Patronymic\"}]"
                },
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
        },
        repository_refs = new
        {
            grammar =
                "Repository refs accept full GrampsRepositoryRef objects or simple strings: \"Ref : CallNumber : MediaType\". " +
                "CallNumber and MediaType are optional: \"Ref : CallNumber\" and \"Ref :: MediaType\" are valid.",
            examples = new[]
            {
                "REPO123",
                "REPO123 : A-1",
                "REPO123 :: Book",
                "REPO123 : A-1 : Book",
                "[{\"ref\":\"REPO123\",\"call_number\":\"A-1\",\"media_type\":\"Book\"}]"
            },
            forms = new[]
            {
                "JSON array of objects {\"ref\",\"call_number\",\"media_type\",\"note_list\",\"private\"}",
                "JSON array of strings [\"REPO123 : A-1 : Book\"]",
                "One multiline / |-separated string with one repository ref per segment"
            },
            tools = "create_source, update_source (reporef_list)"
        }
    };

    internal static object BuildDateInputGuidePayload() => new
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

    internal static object BuildNameSchemaPayload() => new
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
                    description = "Origin of surname. Standard Gramps values: " +
                                  "Inherited, Given, Taken, Patronymic, Matronymic, Feudal, " +
                                  "Pseudonym, Patrilineal, Matrilineal, Occupation, Location, Custom, Unknown",
                    examples = new[] { "Inherited", "Patronymic", "Matronymic", "Patrilineal", "Matrilineal", "Taken", "Occupation" }
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
