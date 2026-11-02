using System.Text.Json;
using GrampsWeb.Mcp.Exceptions;
using GrampsWeb.Mcp.Input;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class FlexibleAlternateNameListTests
{
    private static FlexibleAlternateNameList? Deserialize(string json)
        => JsonSerializer.Deserialize<FlexibleAlternateNameList?>(json);

    [Fact]
    public void Null_Yields_Null() => Assert.Null(Deserialize("null"));

    [Fact]
    public void Array_Of_Strings()
    {
        var v = Deserialize("""["Nick:: Red","Also Known As:: Pat|Lee"]""");
        Assert.NotNull(v);
        Assert.Equal(2, v!.Items.Length);
        Assert.Equal("Red", v.Items[0].FirstName);
        Assert.Equal("Pat", v.Items[1].FirstName);
        Assert.Equal("Lee", v.Items[1].SurnameList![0].Surname);
    }

    [Fact]
    public void Multiline_String_Two_Names()
    {
        var json = JsonSerializer.Serialize("Also Known As:: A|B\nNickname:: C");
        var v = Deserialize(json);
        Assert.Equal(2, v!.Items.Length);
        Assert.Equal("Nickname", v.Items[1].Type);
        Assert.Equal("C", v.Items[1].FirstName);
    }

    [Fact]
    public void Empty_Array()
    {
        var v = Deserialize("[]");
        Assert.NotNull(v);
        Assert.Empty(v!.Items);
    }

    [Fact]
    public void Array_Of_CamelCase_Objects()
    {
        var v = Deserialize("""[{"firstName":"Anna","surnameList":[{"surname":"Holm"}],"type":"Married Name"}]""");

        var name = Assert.Single(v!.Items);
        Assert.Equal("Anna", name.FirstName);
        Assert.Equal("Holm", Assert.Single(name.SurnameList!).Surname);
        Assert.Equal("Married Name", name.Type);
    }

    [Theory]
    // Each of these used to be dropped without a word: the object, the whole stringified array, the number.
    [InlineData("""[{"given":"Anna","maidenName":"Holm"}]""", "Unknown field \"maidenName\" in name object.")]
    [InlineData("""[{"title":"Dr."}]""", "Name object has no name in it")]
    [InlineData("\"[{\\\"given\\\":\\\"Anna\\\",\\\"maidenName\\\":\\\"Holm\\\"}]\"", "Unknown field \"maidenName\" in name object.")]
    [InlineData("\"[{\\\"given\\\":\\\"Anna\\\"\"", "Invalid JSON for alternate name list")]
    [InlineData("""["Anna Holm", 42]""", "Unexpected token Number in alternate name list")]
    [InlineData("\"[\\\"Anna Holm\\\", 42]\"", "Unexpected Number in alternate name list")]
    public void Unusable_Entry_Throws(string json, string start)
    {
        var ex = Assert.Throws<JsonInputException>(() => Deserialize(json));

        Assert.StartsWith(start, ex.Message);
    }
}
