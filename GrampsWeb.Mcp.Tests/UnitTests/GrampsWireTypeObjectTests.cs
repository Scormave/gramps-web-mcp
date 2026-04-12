using System.Text.Json;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsWireTypeObjectTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void Deserialize_WireTypeObject_PopulatesClassStringValue()
    {
        var json = """{"_class":"RepositoryType","string":"","value":1}""";
        var o = JsonSerializer.Deserialize<GrampsWireTypeObject>(json, GrampsJson.Options);
        Assert.NotNull(o);
        Assert.Equal("RepositoryType", o!.Class);
        Assert.Equal("", o.String);
        Assert.Equal(1, o.Value);
        Assert.Equal("1", o.ToPreferredString());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TryReadPreferredString_PrefersNonEmptyStringOverValue()
    {
        using var doc = JsonDocument.Parse("""{"_class":"EventType","string":"Birth","value":12}""");
        Assert.Equal("Birth", GrampsWireTypeObject.TryReadPreferredString(doc.RootElement));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WireTypeStringConverter_OnEvent_ParsesObject()
    {
        var json =
            """{"handle":"h","gramps_id":"E1","type":{"_class":"EventType","string":"","value":12},"private":false}""";
        var e = JsonSerializer.Deserialize<GrampsEvent>(json, GrampsJson.Options);
        Assert.NotNull(e);
        Assert.Equal("12", e!.Type);
    }
}
