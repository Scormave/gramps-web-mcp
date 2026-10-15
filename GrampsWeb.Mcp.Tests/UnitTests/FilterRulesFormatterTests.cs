using System.Text.Json;
using GrampsWeb.Mcp.Formatters;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class FilterRulesFormatterTests
{
    // Rules as Gramps Web API 3.23 describes them, with a type for each label.
    private const string PeopleCatalog = """
        {"filters": [
           {"name": "Ancestors", "comment": "Direct line", "function": "or", "invert": false,
            "rules": [{"name": "IsAncestorOf", "regex": false, "values": ["I0001", "1"]}, {"name": "IsMale", "regex": false, "values": []}]},
           {"name": "Strangers", "comment": "", "function": "and", "invert": true,
            "rules": [{"name": "IsRelatedWith", "regex": false, "values": ["I0001"]}]}],
         "rules": [
           {"category": "General filters", "description": "Matches people that have no family relationships to any other person in the database",
            "labels": [], "name": "Disconnected people", "rule": "Disconnected", "types": []},
           {"category": "Ancestral filters", "description": "Matches people that are ancestors of a specified person",
            "labels": ["ID:", "Inclusive:"], "name": "Ancestors of <person>", "rule": "IsAncestorOf",
            "types": [{"namespace": "Person", "type": "id"}, {"type": "boolean"}]},
           {"category": "Ancestral filters", "description": "Matches people that are ancestors of a specified person not more than N generations away",
            "labels": ["ID:", "Number of generations:"], "name": "Ancestors not more than N generations away", "rule": "IsLessThanNthGenerationAncestorOf",
            "types": [{"namespace": "Person", "type": "id"}, {"max": 32, "min": 1, "type": "integer"}]},
           {"category": "Citation/source filters", "description": "Matches people with a citation  of a particular value",
            "labels": ["Volume/Page:", "Date:", "Confidence level:"], "name": "People with the <citation>", "rule": "HasCitation",
            "types": [{"type": "text"}, {"type": "date"},
                      {"options": [{"label": "Very Low", "value": "0"}, {"label": "Very High", "value": "4"}], "type": "select"}]},
           {"category": "Family filters", "description": "Matches people with a particular relationship",
            "labels": ["Number of relationships:", "Relationship type:"], "name": "People with the <relationships>", "rule": "HasRelationship",
            "types": [{"min": 0, "type": "integer"}, {"custom_types": "family_relation_types", "default_types": "family_relation_types", "type": "gramps_type"}]},
           {"category": "General filters", "description": "Matches people with the particular tag",
            "labels": ["Tag:"], "name": "People with the <tag>", "rule": "HasTag", "types": [{"type": "tag"}]},
           {"category": "General filters", "description": "Matches people matched by the specified filter name",
            "labels": ["Filter name:"], "name": "People matching the <filter>", "rule": "MatchesFilter",
            "types": [{"namespace": "Person", "type": "filter"}]}]}
        """;

    // Gramps Web API 3.22 gives the labels without types.
    private const string OldEventsCatalog = """
        {"filters": [],
         "rules": [
           {"category": "General filters", "description": "Matches events with the particular type",
            "labels": ["Event type:"], "name": "Events with the particular type", "rule": "HasType"},
           {"category": "General filters", "description": "Matches events with the particular tag",
            "labels": ["Tag:"], "name": "Events with the <tag>", "rule": "HasTag"}]}
        """;

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void FormatType_ListsTheRulesByCategoryWithTheKindOfEachValue()
    {
        var text = FilterRulesFormatter.FormatType("people", Parse(PeopleCatalog));

        Assert.StartsWith("FILTER RULES: people (7)\n", text.ReplaceLineEndings("\n"));
        Assert.Contains("a label without a kind takes text", text);
        Assert.Contains(
            """
            Ancestral filters
            - IsAncestorOf (ID: person ID; Inclusive: true/false) — Matches people that are ancestors of a specified person
            - IsLessThanNthGenerationAncestorOf (ID: person ID; Number of generations: number 1–32) — Matches people that are ancestors of a specified person not more than N generations away

            Citation/source filters
            - HasCitation (Volume/Page; Date: date; Confidence level: 0 (Very Low) | 4 (Very High)) — Matches people with a citation of a particular value

            Family filters
            - HasRelationship (Number of relationships: number from 0; Relationship type: family relation type) — Matches people with a particular relationship

            General filters
            - Disconnected — Matches people that have no family relationships to any other person in the database
            - HasTag (Tag: tag name) — Matches people with the particular tag
            - MatchesFilter (Filter name: saved person filter name) — Matches people matched by the specified filter name
            """.ReplaceLineEndings(Environment.NewLine),
            text);
    }

    [Fact]
    public void FormatType_ListsTheSavedFiltersForMatchesFilter()
    {
        var text = FilterRulesFormatter.FormatType("people", Parse(PeopleCatalog));

        Assert.EndsWith(
            """
            SAVED FILTERS (use with MatchesFilter)
            - Ancestors (or): IsAncestorOf [I0001, 1]; IsMale — Direct line
            - Strangers (inverted): IsRelatedWith [I0001]
            """.ReplaceLineEndings(Environment.NewLine).TrimEnd(),
            text);
    }

    [Fact]
    public void FormatType_ExplainsTheLabelsWhenTheServerGivesNoTypes()
    {
        var text = FilterRulesFormatter.FormatType("events", Parse(OldEventsCatalog));

        Assert.Contains("This Gramps Web version does not describe them: ID labels take a Gramps ID", text);
        Assert.Contains("- HasType (Event type) — Matches events with the particular type", text);
        Assert.Contains("- HasTag (Tag) — Matches events with the particular tag", text);
        Assert.EndsWith("No saved events filters.", text);
    }

    [Fact]
    public void FormatOverview_CountsTheRulesAndNamesTheSavedFiltersOfEachType()
    {
        var catalogs = Parse($$"""{"people": {{PeopleCatalog}}, "events": {{OldEventsCatalog}}}""");

        var text = FilterRulesFormatter.FormatOverview(catalogs);

        Assert.StartsWith("FILTER RULES", text);
        Assert.Contains("\"namespace\": \"Event\"", text);
        Assert.Contains("from people Event or Family", text);
        Assert.Contains("- people: 7 rules; saved filters: Ancestors, Strangers", text);
        Assert.EndsWith("- events: 2 rules", text);
        Assert.DoesNotContain("- families:", text);
    }
}
