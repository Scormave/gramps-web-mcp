using System.Text.Json;
using GrampsWeb.Mcp.Input;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class FlexiblePersonRefListTests
{
    private static FlexiblePersonRefList? Deserialize(string json)
        => JsonSerializer.Deserialize<FlexiblePersonRefList?>(json);

    [Fact]
    public void Null_Yields_Null() => Assert.Null(Deserialize("null"));

    [Fact]
    public void String_Double_Colon()
    {
        var v = Deserialize("\"abc123def:: Godfather\"");
        Assert.NotNull(v);
        Assert.Single(v!.Items);
        Assert.Equal("abc123def", v.Items[0].Ref);
        Assert.Equal("Godfather", v.Items[0].Relationship);
    }

    [Fact]
    public void Relationship_With_Spaces_And_Words()
    {
        var v = Deserialize("\"H:: Best friend of the family\"");
        Assert.Equal("Best friend of the family", v!.Items[0].Relationship);
    }

    [Fact]
    public void Json_Objects()
    {
        var v = Deserialize("""[{"ref":"r1","rel":"Witness"}]""");
        Assert.Equal("r1", v!.Items[0].Ref);
        Assert.Equal("Witness", v.Items[0].Relationship);
    }

    [Fact]
    public void Missing_Double_Colon_Throws()
    {
        Assert.Throws<JsonException>(() => Deserialize("\"handle: only single colon\""));
    }
}
