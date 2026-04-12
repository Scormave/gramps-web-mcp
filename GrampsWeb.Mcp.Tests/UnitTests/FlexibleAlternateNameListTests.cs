using System.Text.Json;
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
}
