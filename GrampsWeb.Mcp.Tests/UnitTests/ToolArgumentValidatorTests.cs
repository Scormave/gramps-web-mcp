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

    [Theory]
    [InlineData("""{"handle":"E0001"}""")]
    [InlineData("""{"handle":"E0001","linkMode":"add","objectType":null}""")]
    public void CheckSomethingToUpdate_Refuses_An_Update_Without_A_Field(string arguments)
    {
        Assert.Equal(
            "Nothing to update: the call gives no field to change. If you passed one, check its name; " +
            $"clients drop arguments a tool does not have. {Parameters}",
            ToolArgumentValidator.CheckSomethingToUpdate("update_event", Schema, Args(arguments)));
    }

    [Theory]
    [InlineData("update_event", """{"handle":"E0001","extended":false}""")]
    [InlineData("update_event", """{"handle":"E0001","objectType":""}""")]
    [InlineData("get_object", """{"identifier":"E0001"}""")]
    public void CheckSomethingToUpdate_Accepts_A_Field_Or_Another_Tool(string tool, string arguments)
    {
        Assert.Null(ToolArgumentValidator.CheckSomethingToUpdate(tool, Schema, Args(arguments)));
    }

    [Fact]
    public void NumbersAsText_Turns_Numbers_For_String_Parameters_Into_Text()
    {
        var converted = ToolArgumentValidator.NumbersAsText(Schema, Args("""
            {"identifier":1877,"objectType":52.19,"extended":true,"pagesize":5,"primaryName":7,"unknown":1}
            """));

        Assert.NotNull(converted);
        Assert.Equal("1877", converted["identifier"].GetString());
        Assert.Equal("52.19", converted["objectType"].GetString());
        Assert.Equal(JsonValueKind.True, converted["extended"].ValueKind);
        Assert.Equal(JsonValueKind.Number, converted["pagesize"].ValueKind);
        Assert.Equal(JsonValueKind.Number, converted["primaryName"].ValueKind);
        Assert.Equal(JsonValueKind.Number, converted["unknown"].ValueKind);
    }

    [Fact]
    public void NumbersAsText_Returns_Null_When_Nothing_Changes()
    {
        Assert.Null(ToolArgumentValidator.NumbersAsText(Schema, Args("""{"identifier":"I0001","pagesize":5}""")));
        Assert.Null(ToolArgumentValidator.NumbersAsText(Schema, null));
    }

    /// <summary>An array argument whose items the schema describes, as the SDK writes it for create_records.</summary>
    private static readonly JsonElement BatchSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "events": {
              "type": ["array", "null"],
              "items": {
                "type": ["object", "null"],
                "properties": {
                  "key": { "type": ["string", "null"] },
                  "eventType": { "type": ["string", "null"] },
                  "date": { "type": ["string", "null"] },
                  "isPrivate": { "type": "boolean" }
                },
                "required": ["eventType"]
              },
              "default": null
            },
            "tags": { "type": ["array", "null"], "items": { "type": ["string", "null"] }, "default": null }
          }
        }
        """).RootElement;

    private const string EventFields = "Fields of events items: key (string), eventType (string, required), " +
                                       "date (string), isPrivate (boolean).";

    [Fact]
    public void CheckNames_Checks_The_Fields_Of_Array_Items()
    {
        Assert.Null(ToolArgumentValidator.CheckNames(BatchSchema,
            Args("""{"events":[{"eventType":"Birth","date":"1877"},null],"tags":["x"]}""")));
        Assert.Equal(
            $"Unknown argument: events[1].event_type (did you mean eventType?). Missing required arguments: events[0].eventType, " +
            $"events[1].eventType. {EventFields}",
            ToolArgumentValidator.CheckNames(BatchSchema,
                Args("""{"events":[{"date":"1877"},{"eventType":null,"event_type":"Birth"}]}""")));
    }

    [Fact]
    public void CheckNames_Describes_The_Tool_And_The_Items_When_Both_Have_Problems()
    {
        Assert.Equal(
            "Unknown arguments: evt, events[0].type. Missing required argument: events[0].eventType. " +
            "Parameters: events (array of objects), tags (array of strings). " + EventFields,
            ToolArgumentValidator.CheckNames(BatchSchema, Args("""{"evt":[],"events":[{"type":"Birth"}]}""")));
    }

    [Fact]
    public void FindTypeMismatch_Names_The_Array_Item_Field()
    {
        Assert.Equal(
            $"Argument events[1].isPrivate must be a boolean, not the string \"yes\". {EventFields}",
            ToolArgumentValidator.FindTypeMismatch(BatchSchema,
                Args("""{"events":[{"eventType":"Birth"},{"eventType":"Death","isPrivate":"yes"}]}""")));
        Assert.Null(ToolArgumentValidator.FindTypeMismatch(BatchSchema, Args("""{"events":[{"eventType":"Birth"}]}""")));
    }

    [Fact]
    public void NumbersAsText_Turns_Numbers_In_Array_Items_Into_Text()
    {
        var converted = ToolArgumentValidator.NumbersAsText(BatchSchema,
            Args("""{"events":[{"eventType":"Birth","date":"1877"},{"eventType":"Death","date":1901,"isPrivate":false}]}"""));

        Assert.NotNull(converted);
        var events = converted["events"].EnumerateArray().ToList();
        Assert.Equal("1877", events[0].GetProperty("date").GetString());
        Assert.Equal("1901", events[1].GetProperty("date").GetString());
        Assert.Equal("Death", events[1].GetProperty("eventType").GetString());
        Assert.Equal(JsonValueKind.False, events[1].GetProperty("isPrivate").ValueKind);
        Assert.Null(ToolArgumentValidator.NumbersAsText(BatchSchema, Args("""{"events":[{"eventType":"Birth"}]}""")));
    }

    private static Dictionary<string, JsonElement> Args(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
}
