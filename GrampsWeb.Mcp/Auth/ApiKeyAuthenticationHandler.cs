using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GrampsWeb.Mcp.Auth;

internal sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly ApiKeyValidator _validator;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ApiKeyValidator validator)
        : base(options, logger, encoder)
    {
        _validator = validator;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var apiKey = ExtractApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!_validator.IsValid(apiKey))
        {
            Logger.LogWarning("MCP API key authentication failed for {Path}", Context.Request.Path);
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "mcp-client")],
            Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer realm=\"gramps-web-mcp\"";
        return Task.CompletedTask;
    }

    private string? ExtractApiKey()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(authorization))
        {
            var spaceIndex = authorization.IndexOf(' ');
            if (spaceIndex > 0)
            {
                var scheme = authorization[..spaceIndex];
                if (scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
                {
                    return authorization[(spaceIndex + 1)..].Trim();
                }
            }
        }

        if (Request.Headers.TryGetValue(ApiKeyAuthenticationDefaults.HeaderName, out var headerValue))
        {
            var value = headerValue.ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        return null;
    }
}
