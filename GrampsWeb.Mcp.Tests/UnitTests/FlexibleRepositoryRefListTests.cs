using System.Text.Json;
using GrampsWeb.Mcp.Input;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class FlexibleRepositoryRefListTests
{
    private static FlexibleRepositoryRefList? Deserialize(string json)
        => JsonSerializer.Deserialize<FlexibleRepositoryRefList?>(json);

    [Fact]
    public void Null_Yields_Null() => Assert.Null(Deserialize("null"));

    [Fact]
    public void String_Ref_Only()
    {
        var v = Deserialize("\"REPO1\"");
        Assert.NotNull(v);
        Assert.Single(v!.Items);
        Assert.Equal("REPO1", v.Items[0].Ref);
        Assert.Null(v.Items[0].CallNumber);
        Assert.Null(v.Items[0].MediaType);
    }

    [Fact]
    public void String_Ref_CallNumber_And_MediaType()
    {
        var v = Deserialize("\"REPO1 : A-1 : Book\"");
        Assert.NotNull(v);
        Assert.Equal("REPO1", v!.Items[0].Ref);
        Assert.Equal("A-1", v.Items[0].CallNumber);
        Assert.Equal("Book", v.Items[0].MediaType);
    }

    [Fact]
    public void String_Ref_DoubleColon_MediaType_Only()
    {
        var v = Deserialize("\"REPO1 :: Microfilm\"");
        Assert.NotNull(v);
        Assert.Equal("REPO1", v!.Items[0].Ref);
        Assert.Null(v.Items[0].CallNumber);
        Assert.Equal("Microfilm", v.Items[0].MediaType);
    }

    [Fact]
    public void String_Ref_CallNumber_Only()
    {
        var v = Deserialize("\"REPO1 : A-1\"");
        Assert.NotNull(v);
        Assert.Equal("REPO1", v!.Items[0].Ref);
        Assert.Equal("A-1", v.Items[0].CallNumber);
        Assert.Null(v.Items[0].MediaType);
    }

    [Fact]
    public void Json_Array_Of_Objects_And_Strings()
    {
        var v = Deserialize("""[{"ref":"R1","call_number":"C1"},"R2 :: Journal"]""");
        Assert.NotNull(v);
        Assert.Equal(2, v!.Items.Length);
        Assert.Equal("R1", v.Items[0].Ref);
        Assert.Equal("C1", v.Items[0].CallNumber);
        Assert.Equal("R2", v.Items[1].Ref);
        Assert.Equal("Journal", v.Items[1].MediaType);
    }
}
