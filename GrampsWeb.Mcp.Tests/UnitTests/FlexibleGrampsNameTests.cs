using System.Text.Json;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Serialization;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class FlexibleGrampsNameTests
{
    private static FlexibleGrampsName? Deserialize(string json)
        => JsonSerializer.Deserialize<FlexibleGrampsName?>(json);

    [Fact]
    public void Json_Object_RoundTrip()
    {
        const string json = """{"first_name":"A","surname_list":[{"surname":"B","primary":true}]}""";
        var v = Deserialize(json);
        Assert.NotNull(v);
        Assert.Equal("A", v!.Name.FirstName);
        Assert.Single(v.Name.SurnameList!);
        Assert.Equal("B", v.Name.SurnameList![0].Surname);
    }

    [Fact]
    public void String_Pipe_Given_Surname()
    {
        var v = Deserialize("\"Jane|Smith\"");
        Assert.NotNull(v);
        Assert.Equal("Jane", v!.Name.FirstName);
        Assert.Equal("Smith", v.Name.SurnameList![0].Surname);
    }

    [Fact]
    public void String_Last_Space()
    {
        var v = Deserialize("\"Mary Ann Jones\"");
        Assert.Equal("Mary Ann", v!.Name.FirstName);
        Assert.Equal("Jones", v.Name.SurnameList![0].Surname);
    }

    [Fact]
    public void String_Type_Double_Colon()
    {
        var v = Deserialize("\"Married Name:: Anna|Kovacs\"");
        Assert.Equal("Married Name", v!.Name.Type);
        Assert.Equal("Anna", v.Name.FirstName);
        Assert.Equal("Kovacs", v.Name.SurnameList![0].Surname);
    }

    [Fact]
    public void Single_Word_Surname_Empty()
    {
        var v = Deserialize("\"Madonna\"");
        Assert.Equal("Madonna", v!.Name.FirstName);
        Assert.Equal("", v.Name.SurnameList![0].Surname);
    }

    [Fact]
    public void Null_Json()
    {
        Assert.Null(Deserialize("null"));
    }

    [Fact]
    public void ParseSimpleLine_Empty_Throws()
    {
        Assert.Throws<JsonException>(() => FlexibleGrampsNameParsing.ParseSimpleLine("   "));
    }
}
