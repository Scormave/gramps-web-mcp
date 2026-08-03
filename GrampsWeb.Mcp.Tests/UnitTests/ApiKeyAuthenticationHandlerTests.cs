using System.Text.Encodings.Web;
using GrampsWeb.Mcp.Auth;
using GrampsWeb.Mcp.Config;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class ApiKeyAuthenticationHandlerTests
{
    private const string PrimaryKey = "0123456789abcdef";
    private const string SecondaryKey = "fedcba9876543210";

    [Fact]
    public async Task AuthenticateAsync_No_Header_Returns_NoResult()
    {
        var handler = CreateHandler([PrimaryKey]);
        var context = new DefaultHttpContext();

        await handler.InitializeAsync(new AuthenticationScheme(
            ApiKeyAuthenticationDefaults.Scheme,
            null,
            typeof(ApiKeyAuthenticationHandler)), context);

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.Null(result.Failure);
        Assert.Null(result.Ticket);
    }

    [Fact]
    public async Task AuthenticateAsync_Invalid_Key_Returns_Fail()
    {
        var handler = CreateHandler([PrimaryKey]);
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer wrong-key-value!!";

        await handler.InitializeAsync(new AuthenticationScheme(
            ApiKeyAuthenticationDefaults.Scheme,
            null,
            typeof(ApiKeyAuthenticationHandler)), context);

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
    }

    [Fact]
    public async Task AuthenticateAsync_Valid_Bearer_Key_Succeeds()
    {
        var handler = CreateHandler([PrimaryKey]);
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {PrimaryKey}";

        await handler.InitializeAsync(new AuthenticationScheme(
            ApiKeyAuthenticationDefaults.Scheme,
            null,
            typeof(ApiKeyAuthenticationHandler)), context);

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.True(result.Principal?.Identity?.IsAuthenticated);
    }

    [Fact]
    public async Task AuthenticateAsync_Bearer_Scheme_Is_Case_Insensitive()
    {
        var handler = CreateHandler([PrimaryKey]);
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"bearer {PrimaryKey}";

        await handler.InitializeAsync(new AuthenticationScheme(
            ApiKeyAuthenticationDefaults.Scheme,
            null,
            typeof(ApiKeyAuthenticationHandler)), context);

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AuthenticateAsync_Valid_X_Api_Key_Header_Succeeds()
    {
        var handler = CreateHandler([PrimaryKey]);
        var context = new DefaultHttpContext();
        context.Request.Headers[ApiKeyAuthenticationDefaults.HeaderName] = PrimaryKey;

        await handler.InitializeAsync(new AuthenticationScheme(
            ApiKeyAuthenticationDefaults.Scheme,
            null,
            typeof(ApiKeyAuthenticationHandler)), context);

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AuthenticateAsync_Second_Rotation_Key_Succeeds()
    {
        var handler = CreateHandler([PrimaryKey, SecondaryKey]);
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {SecondaryKey}";

        await handler.InitializeAsync(new AuthenticationScheme(
            ApiKeyAuthenticationDefaults.Scheme,
            null,
            typeof(ApiKeyAuthenticationHandler)), context);

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
    }

    private static ApiKeyAuthenticationHandler CreateHandler(string[] keys)
    {
        var authConfig = new McpAuthConfig(keys, WarnAboutAnonymousAccess: false);
        var validator = new ApiKeyValidator(authConfig);
        var optionsMonitor = new TestOptionsMonitor(new AuthenticationSchemeOptions());
        return new ApiKeyAuthenticationHandler(
            optionsMonitor,
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            validator);
    }

    private sealed class TestOptionsMonitor(AuthenticationSchemeOptions currentValue) : IOptionsMonitor<AuthenticationSchemeOptions>
    {
        public AuthenticationSchemeOptions CurrentValue => currentValue;

        public AuthenticationSchemeOptions Get(string? name) => currentValue;

        public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener) => null;
    }
}
