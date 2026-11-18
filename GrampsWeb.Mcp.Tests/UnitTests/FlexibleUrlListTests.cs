using System.Text.Json;
using GrampsWeb.Mcp.Input;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class FlexibleUrlListTests
{
    private static FlexibleUrlList? Deserialize(string json)
        => JsonSerializer.Deserialize<FlexibleUrlList?>(json);

    [Fact]
    public void Null_Yields_Null() => Assert.Null(Deserialize("null"));

    [Fact]
    public void String_Type_Path()
    {
        var v = Deserialize("\"Web Home: http://test.com\"");
        Assert.NotNull(v);
        Assert.Single(v!.Items);
        Assert.Equal("Web Home", v.Items[0].Type);
        Assert.Equal("http://test.com", v.Items[0].Path);
        Assert.Null(v.Items[0].Description);
    }

    [Fact]
    public void String_With_EmDash_Description()
    {
        var v = Deserialize("\"Web Home: https://x.org — my site\"");
        Assert.NotNull(v);
        Assert.Equal("https://x.org", v!.Items[0].Path);
        Assert.Equal("my site", v.Items[0].Description);
    }

    [Fact]
    public void String_With_Ascii_Dash_Description()
    {
        var v = Deserialize("\"H: https://a.com - note here\"");
        Assert.NotNull(v);
        Assert.Equal("https://a.com", v!.Items[0].Path);
        Assert.Equal("note here", v.Items[0].Description);
    }

    [Theory]
    [InlineData("https://archive.example.org", "https://archive.example.org", null)]
    [InlineData("http://archive.example.org/fonds — reading room", "http://archive.example.org/fonds", "reading room")]
    public void Bare_Link_Is_A_Web_Home_Link(string line, string path, string? description)
    {
        var v = Deserialize(JsonSerializer.Serialize(line));
        Assert.Equal("Web Home", v!.Items[0].Type);
        Assert.Equal(path, v.Items[0].Path);
        Assert.Equal(description, v.Items[0].Description);
    }

    [Fact]
    public void Bare_Link_Mixes_With_Typed_Lines()
    {
        var v = Deserialize("\"https://archive.example.org\\nE-mail: info@archive.example.org\"");
        Assert.Equal(["Web Home", "E-mail"], v!.Items.Select(u => u.Type));
        Assert.Equal("info@archive.example.org", v.Items[1].Path);
    }

    [Fact]
    public void Json_Objects()
    {
        var v = Deserialize("""[{"type":"T","path":"http://p","desc":"D"}]""");
        Assert.Equal("D", v!.Items[0].Description);
    }

    [Fact]
    public void Missing_Colon_Throws()
    {
        Assert.Throws<JsonException>(() => Deserialize("\"bad\""));
    }
}
