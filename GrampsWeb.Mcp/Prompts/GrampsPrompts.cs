using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Prompts;

[McpServerPromptType]
public sealed class GrampsPrompts
{
    [McpServerPrompt(Name = "add-person")]
    [Description("Add a new person with optional birth/death events")]
    public static ChatMessage AddPerson(
        [Description("Person's full name (e.g. 'John Smith')")] string name,
        [Description("Gender: Male, Female, or Unknown")] string gender = "Unknown",
        [Description("Birth date (e.g. '1920-05-15', 'about 1920')")] string? birthDate = null,
        [Description("Birth place name")] string? birthPlace = null,
        [Description("Death date")] string? deathDate = null,
        [Description("Death place name")] string? deathPlace = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Add a new person to the Gramps genealogy database with the following details:");
        sb.AppendLine();
        sb.AppendLine($"Name: {name}");
        sb.AppendLine($"Gender: {gender}");
        if (birthDate != null || birthPlace != null)
        {
            sb.Append("Birth:");
            if (birthDate != null)
                sb.Append($" {birthDate}");
            if (birthPlace != null)
                sb.Append(birthDate != null ? $" at {birthPlace}" : $" place: {birthPlace}");
            sb.AppendLine();
        }

        if (deathDate != null || deathPlace != null)
        {
            sb.Append("Death:");
            if (deathDate != null)
                sb.Append($" {deathDate}");
            if (deathPlace != null)
                sb.Append(deathDate != null ? $" at {deathPlace}" : $" place: {deathPlace}");
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("Steps:");
        sb.AppendLine("1. Search for the person first. If a match exists, show it and ask before creating another.");
        sb.AppendLine("2. Find each place with search; create it with create_place only if it is not in the tree.");
        sb.AppendLine("3. Create the Birth and Death events that have a date or place with create_event, using the place handles.");
        sb.AppendLine("4. Create the person with create_person: the name, gender, and those events in eventRefs with role Primary.");
        sb.AppendLine("After creation, confirm the result by showing the handle and Gramps ID.");
        return new ChatMessage(ChatRole.User, sb.ToString());
    }

    [McpServerPrompt(Name = "research-person")]
    [Description("Research a person, expanding to timelines or trees only when relevant")]
    public static ChatMessage ResearchPerson(
        [Description("Person handle, Gramps ID (e.g. I0001), or name to search")] string person)
    {
        var text =
            $"Research person \"{person}\" in the Gramps database.\n" +
            "1. If the input is a Gramps ID, call get_object(identifier: ID). If it is an opaque handle, call get_object(objectType: \"person\", identifier: HANDLE). Otherwise search by name, confirm the match, then get_object.\n" +
            "2. Start with the person's record. Set extended=true only when linked family or event details are needed.\n" +
            "3. Call get_timeline(objectType: \"person\", ...) only for chronological questions. Call get_person_tree(direction: \"ancestors\" or \"descendants\", ...) only for the requested branch; use a small generation count first.\n" +
            "4. Report only facts supported by the retrieved records, distinguish uncertainty, and mention missing citations when relevant.";
        return new ChatMessage(ChatRole.User, text);
    }

    [McpServerPrompt(Name = "add-family")]
    [Description("Create a family connecting two people with optional marriage event")]
    public static ChatMessage AddFamily(
        [Description("Father: name, handle, or Gramps ID")] string? father = null,
        [Description("Mother: name, handle, or Gramps ID")] string? mother = null,
        [Description("Relationship type: Married, Unmarried, Civil Union, Unknown")] string relationship = "Married",
        [Description("Marriage date (optional)")] string? marriageDate = null,
        [Description("Marriage place (optional)")] string? marriagePlace = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Create a family in the Gramps database:");
        if (father != null)
            sb.AppendLine($"Father: {father}");
        if (mother != null)
            sb.AppendLine($"Mother: {mother}");
        sb.AppendLine($"Relationship: {relationship}");
        if (marriageDate != null)
        {
            sb.Append($"Marriage: {marriageDate}");
            if (marriagePlace != null)
                sb.Append($" at {marriagePlace}");
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("Steps:");
        sb.AppendLine("1. Find or verify each person exists. If a name is given instead of a handle/ID,");
        sb.AppendLine("   search for them first. If not found, ask whether to create them.");
        sb.AppendLine("2. Create the family with create_family, passing the person handles.");
        sb.AppendLine("3. If a marriage date is provided, create a Marriage event with create_event,");
        sb.AppendLine("   then link it to the family using update_family with eventRefs and linkMode: \"add\".");
        sb.AppendLine("4. Show the created family details with get_object(objectType: \"family\", ...).");
        return new ChatMessage(ChatRole.User, sb.ToString());
    }

    [McpServerPrompt(Name = "find-connections")]
    [Description("Find the genealogical relationship between two people")]
    public static ChatMessage FindConnections(
        [Description("First person: name, handle, or Gramps ID")] string person1,
        [Description("Second person: name, handle, or Gramps ID")] string person2)
    {
        var text =
            $"Find the genealogical relationship between \"{person1}\" and \"{person2}\".\n" +
            "Steps:\n" +
            "1. Resolve both people to handles. If given names, use search() to find them.\n" +
            "   If given Gramps IDs (like I0001), use get_object with the ID.\n" +
            "2. Call get_relations(handle1, handle2) to find their relationship.\n" +
            "3. If related, explain the connection in plain language (e.g. \"3rd cousin once removed\").\n" +
            "4. If no direct relationship found, try showing both their ancestor trees\n" +
            "   with get_person_tree(direction=ancestors, 3 generations each) to see if there's a common ancestor.";
        return new ChatMessage(ChatRole.User, text);
    }

    [McpServerPrompt(Name = "import-from-text")]
    [Description("Parse genealogical information from text and add to the database")]
    public static ChatMessage ImportFromText(
        [Description("Text containing genealogy information to import")] string text)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Parse the following text and add the genealogical data to the Gramps database:");
        sb.AppendLine("---");
        sb.AppendLine(text);
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("Steps:");
        sb.AppendLine("1. Identify all people mentioned: extract names, dates, places, and relationships.");
        sb.AppendLine("2. For each person, check if they already exist: use search(name).");
        sb.AppendLine("3. Find each place with search; create it with create_place only if it is not in the tree.");
        sb.AppendLine("4. Create the events (births, baptisms, marriages, deaths) with create_event, using the place handles.");
        sb.AppendLine("5. Create missing people with create_person, passing their events in eventRefs with roles.");
        sb.AppendLine("6. Create families to connect parents and children using create_family, with family events such as marriages.");
        sb.AppendLine("7. Attach events to people who already existed with update_person (eventRefs, linkMode: \"add\").");
        sb.AppendLine("8. Add sources and citations if the text mentions them.");
        sb.AppendLine("After importing, provide a summary:");
        sb.AppendLine("- How many people were created vs. already existed");
        sb.AppendLine("- Families created");
        sb.AppendLine("- Events added");
        sb.AppendLine("- Any information that could not be imported and why");
        return new ChatMessage(ChatRole.User, sb.ToString());
    }

    [McpServerPrompt(Name = "change-link")]
    [Description("Add or remove one link between existing Gramps records")]
    public static ChatMessage ChangeLink(
        [Description("Owner record type: person, family, event, place, source, citation, note, media, or repository")]
        string ownerType,
        [Description("Owner handle, Gramps ID, or unambiguous name")]
        string owner,
        [Description("Owner's exact update parameter, e.g. eventRefs, childRefs, noteHandles, citationHandles, repositoryHandles")]
        string linkField,
        [Description("Existing target handle, Gramps ID, or unambiguous name")]
        string target,
        [Description("Action: add or remove")]
        string action = "add")
    {
        var text =
            $"{action} one existing link on {ownerType} \"{owner}\" using {linkField} and target \"{target}\".\n" +
            "1. Resolve both records to handles with get_object or search; confirm ambiguous names. Inspect the owner to confirm it owns this link field. Events do not own person-event links: update_person(eventRefs) or update_family(eventRefs).\n" +
            "2. Use the matching update_* tool with handle: OWNER_HANDLE, the chosen link field containing TARGET_HANDLE, and linkMode: \"add\" or \"remove\". For childRefs, eventRefs, repositoryHandles, or enclosedBy, include known relationship/role metadata on add; do not guess unknown relationship facts.\n" +
            "3. Read the owner again to verify the link changed. Do not create a new record to satisfy this request. For metadata changes to an existing reference, read and replace the full list instead.\n" +
            "See get_reference(topic: \"input-guide\", section: \"link_updates\") if the list semantics are unclear.";
        return new ChatMessage(ChatRole.User, text);
    }

    [McpServerPrompt(Name = "cite-fact")]
    [Description("Attach a source citation to an existing person, family, event, place, or media record")]
    public static ChatMessage CiteFact(
        [Description("Record type: person, family, event, place, or media")]
        string recordType,
        [Description("Record handle, Gramps ID, or unambiguous name")]
        string record,
        [Description("Source title or existing source handle/Gramps ID")]
        string source,
        [Description("Page or location within the source, if known")]
        string? page = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Attach a citation from source \"{source}\" to {recordType} \"{record}\".");
        if (!string.IsNullOrWhiteSpace(page))
            sb.AppendLine($"Page/location: {page}");
        sb.AppendLine("1. Resolve and inspect the target with get_object; confirm an ambiguous name before editing. Check its existing citations for the same source and page to avoid duplicates.");
        sb.AppendLine("2. Find the source by handle/Gramps ID or search by title. Reuse a matching source; create_source only if no matching source exists and the supplied information is sufficient.");
        sb.AppendLine("3. Reuse an existing matching citation if available; otherwise create_citation(sourceHandle: SOURCE_HANDLE, page: PAGE_IF_KNOWN). Do not invent page, confidence, or other evidence details.");
        sb.AppendLine("4. Attach with the target's update_* tool: handle: TARGET_HANDLE, citationHandles: [CITATION_HANDLE], linkMode: \"add\". Read the target again to verify it is linked.");
        return new ChatMessage(ChatRole.User, sb.ToString());
    }
}
