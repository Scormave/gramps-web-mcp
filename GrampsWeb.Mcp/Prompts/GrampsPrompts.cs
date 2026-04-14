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
        if (birthDate != null)
        {
            sb.Append($"Birth: {birthDate}");
            if (birthPlace != null)
                sb.Append($" at {birthPlace}");
            sb.AppendLine();
        }

        if (deathDate != null)
        {
            sb.Append($"Death: {deathDate}");
            if (deathPlace != null)
                sb.Append($" at {deathPlace}");
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("Use quick_add_person to create the person with all provided details in a single call.");
        sb.AppendLine("After creation, confirm the result by showing the handle and Gramps ID.");
        return new ChatMessage(ChatRole.User, sb.ToString());
    }

    [McpServerPrompt(Name = "research-person")]
    [Description("Gather comprehensive information about a person")]
    public static ChatMessage ResearchPerson(
        [Description("Person handle, Gramps ID (e.g. I0001), or name to search")] string person)
    {
        var text =
            $"Research person \"{person}\" in the Gramps database. Follow these steps:\n" +
            "1. Find the person: if \"" + person +
            "\" looks like a Gramps ID (e.g. I0001), use find_by_gramps_id. " +
            "Otherwise, use search(\"" + person + "\") to locate them.\n" +
            "2. Get full details: call get_person with extended=true.\n" +
            "3. Get their timeline: call get_person_timeline for a chronological view of life events.\n" +
            "4. Get ancestors: call get_ancestors with 3 generations.\n" +
            "5. Get descendants: call get_descendants with 3 generations.\n" +
            "Present the findings as a structured biographical summary including:\n" +
            "- Full name(s) and vital dates\n" +
            "- Family connections (parents, spouses, children)\n" +
            "- Key life events in chronological order\n" +
            "- Ancestor and descendant overview";
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
        sb.AppendLine("   then link it to the family using update_family with eventRefs.");
        sb.AppendLine("4. Show the created family details with get_family.");
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
            "   If given Gramps IDs (like I0001), use find_by_gramps_id().\n" +
            "2. Call get_relations(handle1, handle2) to find their relationship.\n" +
            "3. If related, explain the connection in plain language (e.g. \"3rd cousin once removed\").\n" +
            "4. If no direct relationship found, try showing both their ancestor trees\n" +
            "   with get_ancestors (3 generations each) to see if there's a common ancestor.";
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
        sb.AppendLine("3. Create missing people using quick_add_person with any available birth/death info.");
        sb.AppendLine("4. Create families to connect parents and children using create_family.");
        sb.AppendLine("5. Add additional events (marriages, baptisms, etc.) using add_event_to_person.");
        sb.AppendLine("6. Add sources and citations if the text mentions them.");
        sb.AppendLine("After importing, provide a summary:");
        sb.AppendLine("- How many people were created vs. already existed");
        sb.AppendLine("- Families created");
        sb.AppendLine("- Events added");
        sb.AppendLine("- Any information that could not be imported and why");
        return new ChatMessage(ChatRole.User, sb.ToString());
    }
}
