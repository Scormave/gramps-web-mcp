using GrampsWeb.Mcp.Config;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class McpOriginConfigTests
{
    [Fact]
    public void Create_Without_Value_Allows_No_Origin()
    {
        var config = McpOriginConfig.Create(null, McpListenMode.Http);

        Assert.Empty(config.AllowedOrigins);
        Assert.False(config.IsAllowed("http://localhost:8080"));
    }

    [Fact]
    public void Create_Stdio_Ignores_Value()
    {
        var config = McpOriginConfig.Create("not an origin", McpListenMode.Stdio);

        Assert.Empty(config.AllowedOrigins);
    }

    [Fact]
    public void Create_Parses_Comma_Separated_Origins()
    {
        var config = McpOriginConfig.Create(
            " https://app.example.com , http://localhost:6274,,",
            McpListenMode.Http);

        Assert.Equal(2, config.AllowedOrigins.Count);
        Assert.True(config.IsAllowed("https://app.example.com"));
        Assert.True(config.IsAllowed("http://localhost:6274"));
    }

    [Theory]
    [InlineData("https://app.example.com", "https://app.example.com")]
    [InlineData("https://app.example.com", "HTTPS://App.Example.COM")]
    [InlineData("https://app.example.com/", "https://app.example.com")]
    [InlineData("https://app.example.com:443", "https://app.example.com")]
    [InlineData("http://localhost", "http://localhost:80")]
    [InlineData("https://пример.рф", "https://xn--e1afmkfd.xn--p1ai")]
    [InlineData("http://[::1]:6274", "http://[0:0:0:0:0:0:0:1]:6274")]
    [InlineData("http://127.0.0.1:6274", "http://127.0.0.1:6274")]
    public void IsAllowed_Matches_Equivalent_Spelling(string allowed, string origin)
    {
        var config = McpOriginConfig.Create(allowed, McpListenMode.Http);

        Assert.True(config.IsAllowed(origin));
    }

    [Theory]
    [InlineData("http://app.example.com")]
    [InlineData("https://app.example.com:8443")]
    [InlineData("https://evil.app.example.com")]
    [InlineData("https://app.example.com.evil.example")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("https://app.example.com, https://evil.example")]
    public void IsAllowed_Refuses_Other_Origins(string origin)
    {
        var config = McpOriginConfig.Create("https://app.example.com", McpListenMode.Http);

        Assert.False(config.IsAllowed(origin));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("null")]
    [InlineData("localhost:6274")]
    [InlineData("https://app.example.com/mcp")]
    [InlineData("https://app.example.com/?x=1")]
    [InlineData("https://user@app.example.com")]
    [InlineData("ftp://app.example.com")]
    [InlineData("file:///etc/passwd")]
    public void Create_Rejects_Values_That_Are_Not_Origins(string value)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            McpOriginConfig.Create($"https://app.example.com,{value}", McpListenMode.Http));

        Assert.StartsWith("Configuration validation failed:", ex.Message);
        Assert.Contains("MCP_ALLOWED_ORIGINS", ex.Message);
        Assert.EndsWith(value, ex.Message);
    }
}
