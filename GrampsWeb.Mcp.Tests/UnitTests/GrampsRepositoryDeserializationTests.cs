using System.Text.Json;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsRepositoryDeserializationTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void DeserializeRepository_TypeAsRepositoryTypeObject_UsesNumericValueWhenStringEmpty()
    {
        var json =
            """{"_class":"Repository","handle":"1026ee42b0f7333f4b6d8361c60e","gramps_id":"R0001","name":"Test Repository","type":{"_class":"RepositoryType","string":"","value":1},"address_list":[],"note_list":[],"private":false,"tag_list":[],"urls":[]}""";

        var r = JsonSerializer.Deserialize<GrampsRepository>(json, GrampsJson.Options);
        Assert.NotNull(r);
        Assert.Equal("1", r!.Type);
        Assert.Equal("Test Repository", r.Name);
    }
}
