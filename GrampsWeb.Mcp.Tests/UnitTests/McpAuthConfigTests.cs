using GrampsWeb.Mcp.Config;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class McpAuthConfigTests
{
    private const string ValidKey = "0123456789abcdef";

    [Fact]
    public void Create_Stdio_Ignores_Keys()
    {
        var config = McpAuthConfig.Create(ValidKey, "http://0.0.0.0:8080", McpListenMode.Stdio);

        Assert.False(config.Enabled);
        Assert.False(config.WarnAboutAnonymousAccess);
        Assert.Empty(config.ApiKeys);
    }

    [Fact]
    public void Create_Http_Without_Key_On_Loopback_Does_Not_Warn()
    {
        var config = McpAuthConfig.Create(null, "http://127.0.0.1:8080", McpListenMode.Http);

        Assert.False(config.Enabled);
        Assert.False(config.WarnAboutAnonymousAccess);
    }

    [Fact]
    public void Create_Http_Without_Key_On_Public_Bind_Warns()
    {
        var config = McpAuthConfig.Create(null, "http://0.0.0.0:8080", McpListenMode.Http);

        Assert.False(config.Enabled);
        Assert.True(config.WarnAboutAnonymousAccess);
    }

    [Fact]
    public void Create_Http_Without_Key_On_Specific_Ip_Warns()
    {
        var config = McpAuthConfig.Create(null, "http://192.168.1.10:8080", McpListenMode.Http);

        Assert.True(config.WarnAboutAnonymousAccess);
    }

    [Fact]
    public void Create_Http_With_Key_Enables_Auth()
    {
        var config = McpAuthConfig.Create(ValidKey, "http://0.0.0.0:8080", McpListenMode.Http);

        Assert.True(config.Enabled);
        Assert.False(config.WarnAboutAnonymousAccess);
        Assert.Single(config.ApiKeys);
    }

    [Fact]
    public void Create_Http_With_Comma_Separated_Keys_Parses_All()
    {
        var secondKey = "fedcba9876543210";
        var config = McpAuthConfig.Create($"{ValidKey},{secondKey}", "http://127.0.0.1:8080", McpListenMode.Http);

        Assert.True(config.Enabled);
        Assert.Equal(2, config.ApiKeys.Length);
        Assert.Contains(ValidKey, config.ApiKeys);
        Assert.Contains(secondKey, config.ApiKeys);
    }

    [Fact]
    public void Create_Rejects_Short_Key_Without_Leaking_Value()
    {
        const string shortKey = "too-short";

        var ex = Assert.Throws<InvalidOperationException>(
            () => McpAuthConfig.Create(shortKey, "http://127.0.0.1:8080", McpListenMode.Http));

        Assert.StartsWith("Configuration validation failed:", ex.Message);
        Assert.DoesNotContain(shortKey, ex.Message);
    }

    [Fact]
    public void Create_Rejects_Empty_Key_List_From_Non_Empty_Raw()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => McpAuthConfig.Create(",", "http://127.0.0.1:8080", McpListenMode.Http));

        Assert.StartsWith("Configuration validation failed:", ex.Message);
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://[::1]:8080")]
    [InlineData("http://localhost:8080")]
    public void IsLoopbackOnly_Returns_True_For_Loopback_Urls(string urls)
    {
        Assert.True(McpAuthConfig.IsLoopbackOnly(urls));
    }

    [Fact]
    public void IsLoopbackOnly_Returns_False_For_Mixed_Bind()
    {
        Assert.False(McpAuthConfig.IsLoopbackOnly("http://127.0.0.1:8080;http://0.0.0.0:9090"));
    }

    [Theory]
    [InlineData("http://0.0.0.0:8080")]
    [InlineData("http://*:8080")]
    [InlineData("http://+:8080")]
    public void IsLoopbackOnly_Returns_False_For_Public_Bind(string urls)
    {
        Assert.False(McpAuthConfig.IsLoopbackOnly(urls));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    public void IsLoopbackOnly_Returns_False_For_Empty_Or_Invalid(string? urls)
    {
        Assert.False(McpAuthConfig.IsLoopbackOnly(urls));
    }
}
