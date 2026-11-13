using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Prompts;

/// <summary>
/// Workflow templates. Each prompt returns plain text, which the SDK sends as one user-role message. A
/// <c>ChatMessage</c> return type would give the same message, but the SDK would build a JSON schema for the whole
/// <c>AIContent</c> type graph behind it at startup.
/// </summary>
[McpServerPromptType]
public sealed class GrampsPrompts
{
    [McpServerPrompt(Name = "add-person")]
    [Description("Add a new person with optional birth/death events")]
    public static string AddPerson(
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
        sb.AppendLine("2. Find each place with search.");
        sb.AppendLine("3. Create everything new in one create_records call: the places not in the tree, each with a key such as " +
                      "\"birthplace\"; the Birth and Death events that have a date or place, with placeHandle set to the place " +
                      "handle or \"#birthplace\"; and the person with the name, gender and those events in eventRefs " +
                      "(\"#birth\", \"#death\"), where the default Primary role makes them the birth and death.");
        sb.AppendLine("4. If the call is refused because the person may already be in the tree, show the possible duplicates and " +
                      "ask; pass notDuplicateOf only when the user says it is someone else.");
        sb.AppendLine("After creation, confirm the result by showing the handle and Gramps ID.");
        return sb.ToString();
    }

    [McpServerPrompt(Name = "research-person")]
    [Description("Research a person, expanding to timelines or trees only when relevant")]
    public static string ResearchPerson(
        [Description("Person handle, Gramps ID (e.g. I0001), or name to search")] string person)
    {
        var text =
            $"Research person \"{person}\" in the Gramps database.\n" +
            "1. If the input is a Gramps ID, call get_records(identifiers: [ID]). If it is an opaque handle, call get_records(objectType: \"person\", identifiers: [HANDLE]). Otherwise search by name, confirm the match, then get_records.\n" +
            "2. Start with the person's record. Set extended=true only when linked family or event details are needed.\n" +
            "3. Call get_timeline(objectType: \"person\", ...) only for chronological questions. Call get_person_tree(direction: \"ancestors\" or \"descendants\", ...) only for the requested branch; use a small generation count first.\n" +
            "4. Report only facts supported by the retrieved records, distinguish uncertainty, and mention missing citations when relevant.";
        return text;
    }

    [McpServerPrompt(Name = "add-family")]
    [Description("Create a family connecting two people with optional marriage event")]
    public static string AddFamily(
        [Description("Father: name, handle, or Gramps ID")] string? father = null,
        [Description("Mother: name, handle, or Gramps ID")] string? mother = null,
        [Description("Relationship type: Married, Unmarried, Civil Union, Unknown (optional)")] string? relationship = null,
        [Description("Marriage date (optional)")] string? marriageDate = null,
        [Description("Marriage place (optional)")] string? marriagePlace = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Create a family in the Gramps database:");
        if (father != null)
            sb.AppendLine($"Father: {father}");
        if (mother != null)
            sb.AppendLine($"Mother: {mother}");
        if (relationship != null)
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
        sb.AppendLine("2. Create the family with create_records, families: [{fatherHandle, motherHandle, relationshipType}]");
        sb.AppendLine("   with the person handles and the relationship above,");
        sb.AppendLine("   or Married when a source records the marriage. Without one the family is Unknown, as in Gramps.");
        sb.AppendLine("3. If a marriage date is provided, add the Marriage event to the same call, events:");
        sb.AppendLine("   [{key: \"marriage\", eventType: \"Marriage\", date, placeHandle}] with the place found by search,");
        sb.AppendLine("   and list \"#marriage\" in the family's eventRefs.");
        sb.AppendLine("4. Show the created family details with get_records(objectType: \"family\", ...).");
        return sb.ToString();
    }

    [McpServerPrompt(Name = "find-connections")]
    [Description("Find the genealogical relationship between two people")]
    public static string FindConnections(
        [Description("First person: name, handle, or Gramps ID")] string person1,
        [Description("Second person: name, handle, or Gramps ID")] string person2)
    {
        var text =
            $"Find the genealogical relationship between \"{person1}\" and \"{person2}\".\n" +
            "Steps:\n" +
            "1. Resolve both people to handles. If given names, use search() to find them.\n" +
            "   If given Gramps IDs (like I0001), use get_records with the ID.\n" +
            "2. Call get_relations(handle1, handle2) to find their relationship.\n" +
            "3. If related, explain the connection in plain language (e.g. \"3rd cousin once removed\").\n" +
            "4. If no direct relationship found, try showing both their ancestor trees\n" +
            "   with get_person_tree(direction=ancestors, 3 generations each) to see if there's a common ancestor.";
        return text;
    }

    [McpServerPrompt(Name = "import-from-text")]
    [Description("Parse genealogical information from text and add to the database")]
    public static string ImportFromText(
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
        sb.AppendLine("3. Find each place with search, and the source if the text names one.");
        sb.AppendLine("4. Save everything new in one create_records call: places and the source not in the tree, a citation, " +
                      "the events (births, baptisms, marriages, deaths), the missing people with their events in eventRefs " +
                      "with roles, and the families that connect parents and children, with family events such as marriages. " +
                      "Give records keys and link them to each other by \"#key\"; link existing records by handle. " +
                      "Over 50 records, split it and link the later calls by the handles the earlier ones return.");
        sb.AppendLine("5. Attach new events to people who already existed with update_person (eventRefs, linkMode: \"add\"), " +
                      "and new children to existing families with update_family (childRefs, linkMode: \"add\").");
        sb.AppendLine("After importing, provide a summary:");
        sb.AppendLine("- How many people were created vs. already existed");
        sb.AppendLine("- Families created");
        sb.AppendLine("- Events added");
        sb.AppendLine("- Any information that could not be imported and why");
        return sb.ToString();
    }

    [McpServerPrompt(Name = "change-link")]
    [Description("Add or remove one link between existing Gramps records")]
    public static string ChangeLink(
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
            "1. Resolve both records to handles with get_records or search; confirm ambiguous names. Inspect the owner to confirm it owns this link field. Events do not own person-event links: update_person(eventRefs) or update_family(eventRefs).\n" +
            "2. Use the matching update_* tool with handle: OWNER_HANDLE, the chosen link field containing TARGET_HANDLE, and linkMode: \"add\" or \"remove\". For childRefs, eventRefs, repositoryHandles, or enclosedBy, include known relationship/role metadata on add; do not guess unknown relationship facts.\n" +
            "3. Read the owner again to verify the link changed. Do not create a new record to satisfy this request. For metadata changes to an existing reference, read and replace the full list instead.\n" +
            "See get_reference(topic: \"input-guide\", section: \"link_updates\") if the list semantics are unclear.";
        return text;
    }

    [McpServerPrompt(Name = "cite-fact")]
    [Description("Attach a source citation to an existing person, family, event, place, or media record")]
    public static string CiteFact(
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
        sb.AppendLine("1. Resolve and inspect the target with get_records; confirm an ambiguous name before editing. Check its existing citations for the same source and page to avoid duplicates.");
        sb.AppendLine("2. Find the source by handle/Gramps ID or search by title. Reuse a matching source; only if no matching source exists and the supplied information is sufficient, create it in the same create_records call as the citation: sources: [{key: \"src\", title}].");
        sb.AppendLine("3. Reuse an existing matching citation if available; otherwise create_records(citations: [{sourceHandle: SOURCE_HANDLE or \"#src\", page: PAGE_IF_KNOWN}]). Do not invent page, confidence, or other evidence details.");
        sb.AppendLine("4. Attach with the target's update_* tool: handle: TARGET_HANDLE, citationHandles: [CITATION_HANDLE], linkMode: \"add\". Read the target again to verify it is linked.");
        return sb.ToString();
    }

    [McpServerPrompt(Name = "audit-tree")]
    [Description("Check the tree or one person's branch for impossible dates, duplicates, unsourced facts, unlinked records and missing data, without changing anything")]
    public static string AuditTree(
        [Description("Audit only this person's ancestors and descendants: name, handle, or Gramps ID (optional; the whole tree when omitted)")]
        string? person = null,
        [Description("Checks to run, any of: dates, duplicates, sources, links, gaps (optional; all when omitted)")]
        string? checks = null)
    {
        var branch = !string.IsNullOrWhiteSpace(person);
        var sb = new StringBuilder();
        sb.AppendLine(branch
            ? $"Audit the branch of \"{person}\" in the Gramps tree: their ancestors, their descendants and the descendants' spouses."
            : "Audit the Gramps tree for likely errors and gaps.");
        if (!string.IsNullOrWhiteSpace(checks))
            sb.AppendLine($"Run only these checks: {checks}.");
        sb.AppendLine("This is a report: change nothing in the tree.");
        sb.AppendLine();
        if (branch)
        {
            sb.AppendLine("Scope: resolve the person to a Gramps ID with search or get_records, and confirm an ambiguous name. BRANCH below is the filter " +
                          "{\"function\": \"or\", \"rules\": [{\"name\": \"IsAncestorOf\", \"values\": [ID, true]}, {\"name\": \"IsDescendantFamilyOf\", \"values\": [ID, true]}]}: " +
                          "list_records(objectType: \"people\", rules: BRANCH) lists the branch, and BRANCH as one more item of a check's rules keeps the check inside it. " +
                          "Checks marked whole tree run only without a person.");
        }
        else
        {
            sb.AppendLine("Scope: the whole tree; leave BRANCH out of the rules below.");
        }

        sb.AppendLine("Lists come from list_records with pagesize 100; rules are Gramps filter rules, explained by get_reference(topic: \"filter-rules\", section: type).");
        sb.AppendLine();
        sb.AppendLine("Checks:");
        sb.AppendLine("1. dates: impossible or unlikely dates.");
        sb.AppendLine("   - People: each line of a people list shows birth and death. Flag a death before the birth, and people who died older than 105: " +
                      "rules [{\"name\": \"AgeAtDeath\", \"values\": [\"greater than\", \"105\"]}].");
        sb.AppendLine("   - Parents and children: get_timeline(objectType: \"family\") shows a family's events in order with the father's age, and get_person_tree(direction: \"ancestors\") " +
                      "labels each ancestor as father or mother. Flag a parent born after a child, a mother under 13 or over 55 or a father under 15 at a birth, a child born after " +
                      "the mother's death or more than 10 months after the father's, and a marriage before 13 or after a death. Check the families of people already flagged first, " +
                      (branch
                          ? "then the person's ancestors, "
                          : "then the ancestors of the home person if the tree has one (rules [{\"name\": \"IsDefaultPerson\"}]), ") +
                      "then other families as far as the work allows, and say how many families were checked.");
        sb.AppendLine("   - get_timeline(objectType: \"person\") for each flagged person: events after the death other than burial, cremation or probate, and baptism long before birth.");
        if (branch)
            sb.AppendLine("   - Loop in the pedigree: rules [{\"name\": \"HasIdOf\", \"values\": [ID]}, {\"name\": \"IsAncestorOf\", \"values\": [ID, false]}] finds the person only when they are their own ancestor.");
        sb.AppendLine("   - A date marked about, before, after or a range conflicts only when no reading of it fits. Julian and Gregorian dates of one day differ by 10 to 13 days.");
        sb.AppendLine("2. duplicates: find_duplicates lists people who may be entered twice, a page at a time with offset; with a person, keep the pairs with someone in the branch.");
        sb.AppendLine("3. sources: people with an event that has no citation, rules [BRANCH, {\"namespace\": \"Event\", \"rules\": [{\"name\": \"HasSourceCount\", \"values\": [\"0\", \"equal to\"]}]}]; " +
                      "people with no citation at all, on themselves or any event, rules [BRANCH, {\"name\": \"HasSourceCount\", \"values\": [\"0\", \"equal to\"]}, " +
                      "{\"invert\": true, \"rules\": [{\"namespace\": \"Event\", \"rules\": [{\"name\": \"HasSourceCount\", \"values\": [\"0\", \"greater than\"]}]}]}]. " +
                      "Whole tree: the number of events without a citation, list_records(objectType: \"events\", gql: \"citation_list.length = 0\").");
        sb.AppendLine("4. links: people with no family links, rules [BRANCH, {\"name\": \"Disconnected\"}]. Whole tree: records that nothing links to, " +
                      "rules [{\"name\": \"HasReferenceCountOf\", \"values\": [\"equal to\", \"0\"]}] for events, families, places, citations, sources, notes and media. " +
                      "An unlinked event, family or citation is usually left over from an edit; a source, note or media file can stand alone on purpose, so list those as information.");
        sb.AppendLine("5. gaps: people with a given name or surname missing (IncompleteNames), unknown gender (HasUnknownGender), or an event without a date or place " +
                      "(PersonWithIncompleteEvent), each with BRANCH. Whole tree: places without coordinates, places rules [{\"name\": \"HasNoLatOrLon\"}].");
        sb.AppendLine();
        sb.AppendLine("Report:");
        sb.AppendLine("- Group the findings by check, most serious first: impossible dates and loops, likely duplicates, unsourced facts, unlinked records, missing data.");
        sb.AppendLine("- One line per finding: the person's name and dates with the Gramps ID, and what is wrong. For a long list give the count and the first ten.");
        sb.AppendLine("- Say which checks ran, how many records each covered, and what was skipped.");
        sb.AppendLine("- Offer fixes, such as update_person, update_family or merge_records, and make each only after the user confirms it.");
        return sb.ToString();
    }
}
