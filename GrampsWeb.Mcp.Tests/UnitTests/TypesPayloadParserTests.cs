using System.Text.Json;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Serialization;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class TypesPayloadParserTests
{
    [Fact]
    public void ParseCategories_ArrayPerCategory_MatchesApispecDefaultTypes()
    {
        const string json = """{"gender_types":["Male","Female","Unknown"],"source_media_types":["Audio","Book"]}""";
        using var doc = JsonDocument.Parse(json);
        var d = TypesPayloadParser.ParseCategories(doc.RootElement);
        Assert.Equal(2, d.Count);
        Assert.Equal(new[] { "Male", "Female", "Unknown" }, d["gender_types"]);
        Assert.Equal(new[] { "Audio", "Book" }, d["source_media_types"]);
    }

    [Fact]
    public void ParseCategories_ChildReferenceTypes_AsArray_DoesNotRequireInnerDictionary()
    {
        const string json = """{"child_reference_types":["Birth","Adopted","Stepchild"]}""";
        using var doc = JsonDocument.Parse(json);
        var d = TypesPayloadParser.ParseCategories(doc.RootElement);
        Assert.Single(d);
        Assert.Equal(3, d["child_reference_types"].Count);
    }

    [Fact]
    public void ParseCategories_ObjectWithNumericStringKeys_OrderedValuesLikeTypeMap()
    {
        const string json = """{"source_media_types":{"2":"Book","0":"Custom","1":"Audio"}}""";
        using var doc = JsonDocument.Parse(json);
        var d = TypesPayloadParser.ParseCategories(doc.RootElement);
        Assert.Equal(new[] { "Custom", "Audio", "Book" }, d["source_media_types"]);
    }

    [Fact]
    public void FormatTypesResponse_IncludesHumanizedKeysAndBullets()
    {
        using var doc = JsonDocument.Parse("{\"event_types\":[\"Birth\",\"Death\"]}");
        var categories = TypesPayloadParser.ParseCategories(doc.RootElement);
        Assert.NotEmpty(categories);
        var text = TypesFormatter.FormatTypesResponse(categories);
        Assert.Contains("Event types:", text);
        Assert.Contains("• Birth", text);
        Assert.Contains("• Death", text);
    }
}
