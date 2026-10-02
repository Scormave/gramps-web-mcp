using System.Text.Json;
using GrampsWeb.Mcp.Hosting;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class ToolArgumentValidatorTests
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "identifier": { "type": "string" },
            "objectType": { "type": ["string", "null"], "default": null },
            "extended": { "type": "boolean", "default": false },
            "pagesize": { "type": "integer", "default": 20 },
            "relatives": { "type": ["array", "null"], "items": { "type": ["string", "null"] }, "default": null },
            "primaryName": {}
          },
          "required": ["identifier"]
        }
        """).RootElement;

    private const string Parameters =
        "Parameters: identifier (string, required), objectType (string), extended (boolean), " +
        "pagesize (integer), relatives (array of strings), primaryName.";

    [Fact]
    public void CheckNames_Accepts_Known_Names_And_Null_For_Optional_Ones()
    {
        Assert.Null(ToolArgumentValidator.CheckNames(Schema, Args("""{"identifier":"I0001","objectType":null}""")));
        Assert.Null(ToolArgumentValidator.CheckNames(Schema, Args("""{"identifier":"I0001","primaryName":null}""")));
    }

    [Fact]
    public void CheckNames_Reports_Unknown_And_Missing_Names_With_Parameters()
    {
        Assert.Equal(
            $"Unknown arguments: id, person2. Missing required argument: identifier. {Parameters}",
            ToolArgumentValidator.CheckNames(Schema, Args("""{"id":"I0001","person2":"I0002"}""")));
        Assert.Equal(
            $"Missing required argument: identifier. {Parameters}",
            ToolArgumentValidator.CheckNames(Schema, null));
        Assert.Equal(
            $"Missing required argument: identifier. {Parameters}",
            ToolArgumentValidator.CheckNames(Schema, Args("""{"identifier":null}""")));
    }

    [Theory]
    [InlineData("object_type", "objectType")]
    [InlineData("ObjectType", "objectType")]
    [InlineData("extend", "extended")]
    [InlineData("identifer", "identifier")]
    [InlineData("page_size", "pagesize")]
    public void CheckNames_Suggests_The_Likely_Parameter(string name, string suggestion)
    {
        var error = ToolArgumentValidator.CheckNames(Schema, Args($$"""{"identifier":"I0001","{{name}}":1}"""));

        Assert.StartsWith($"Unknown argument: {name} (did you mean {suggestion}?). Parameters:", error);
    }

    [Fact]
    public void CheckNames_Says_When_A_Tool_Takes_No_Arguments()
    {
        var schema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement;

        Assert.Null(ToolArgumentValidator.CheckNames(schema, Args("{}")));
        Assert.Equal("Unknown argument: x. This tool takes no arguments.",
            ToolArgumentValidator.CheckNames(schema, Args("""{"x":1}""")));
    }

    [Theory]
    [InlineData("""{"extended":"yes"}""", "Argument extended must be a boolean, not the string \"yes\".")]
    [InlineData("""{"relatives":"parents"}""", "Argument relatives must be an array of strings, not the string \"parents\".")]
    [InlineData("""{"pagesize":"2x"}""", "Argument pagesize must be an integer, not the string \"2x\".")]
    [InlineData("""{"pagesize":2.5}""", "Argument pagesize must be an integer, not the number 2.5.")]
    [InlineData("""{"identifier":{"id":1}}""", "Argument identifier must be a string, not an object.")]
    [InlineData("""{"objectType":true}""", "Argument objectType must be a string, not the boolean true.")]
    public void FindTypeMismatch_Names_The_Argument_And_Expected_Type(string arguments, string expected)
    {
        Assert.Equal($"{expected} {Parameters}", ToolArgumentValidator.FindTypeMismatch(Schema, Args(arguments)));
    }

    [Fact]
    public void FindTypeMismatch_Accepts_Values_The_Sdk_Converts()
    {
        var arguments = Args("""
            {"identifier":"I0001","objectType":null,"extended":true,"pagesize":"5",
             "relatives":["parents"],"primaryName":"Anna|Petrova","unknown":1}
            """);

        Assert.Null(ToolArgumentValidator.FindTypeMismatch(Schema, arguments));
    }

    private static Dictionary<string, JsonElement> Args(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
}
