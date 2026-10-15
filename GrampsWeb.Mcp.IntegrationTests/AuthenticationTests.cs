using System.Net;
using Xunit;

namespace GrampsWeb.Mcp.IntegrationTests;

/// <summary>
/// Servers with their own configuration: how they sign in to Gramps Web and what they do when it
/// rejects their credentials or tokens.
/// </summary>
[Collection(GrampsWebCollection.Name)]
public sealed class AuthenticationTests(GrampsWebFixture gramps)
{
    [IntegrationFact]
    public async Task SharedServer_SignedInOnce()
    {
        await gramps.Session.CallAsync("get_bookmarks");

        Assert.Equal(["/api/token/"], gramps.Session.TokenRequests.Paths);
    }

    [IntegrationFact]
    public async Task RefreshTokenOnly_SignsInWithoutPassword()
    {
        await using var session = await gramps.StartSessionAsync(
            gramps.Config with { Password = "", RefreshToken = gramps.RefreshToken });

        var found = await session.CallAsync("get_object", new { identifier = "I9999" });

        Assert.Contains("Person not found", found);
        Assert.Equal(["/api/token/refresh/"], session.TokenRequests.Paths);
    }

    [IntegrationFact]
    public async Task WrongPassword_FailsToolCallsAndHealth()
    {
        await using var session = await gramps.StartSessionAsync(gramps.Config with { Password = "wrong-password" });

        var error = await session.CallErrorAsync("search", new { query = "anything" });
        Assert.NotEmpty(error);

        using var health = await session.Http.GetAsync("/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, health.StatusCode);
        Assert.Contains("unhealthy", await health.Content.ReadAsStringAsync());
    }

    [IntegrationFact]
    public async Task Health_IsHealthy()
    {
        using var health = await gramps.Session.Http.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Contains("\"healthy\"", await health.Content.ReadAsStringAsync());
    }

    public static TheoryData<string> RejectedTokens => ["expired", "foreign key"];

    /// <summary>
    /// Gramps Web answers 401 for an expired access token and 422 for one signed with another key,
    /// e.g. after its SECRET_KEY changed. The server refreshes the token and repeats the call once.
    /// </summary>
    [IntegrationTheory]
    [MemberData(nameof(RejectedTokens))]
    public async Task RejectedAccessToken_IsRefreshedAndTheCallRepeated(string kind)
    {
        await using var session = await StartWithRejectedTokenAsync(kind);

        var person = await session.CreateAsync("create_person", new { primaryName = new { first = "Ivan", surname = TestNames.Surname() } });

        Assert.Equal("created", person.Action);
        Assert.Equal(["/api/token/", "/api/token/refresh/"], session.TokenRequests.Paths);
    }

    [IntegrationTheory]
    [MemberData(nameof(RejectedTokens))]
    public async Task RejectedAccessToken_IsRefreshedByHealthCheck(string kind)
    {
        await using var session = await StartWithRejectedTokenAsync(kind);

        using var health = await session.Http.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(["/api/token/", "/api/token/refresh/"], session.TokenRequests.Paths);
    }

    [IntegrationFact]
    public async Task ReadOnly_OffersNoWriteTools()
    {
        await using var session = await gramps.StartSessionAsync(gramps.Config with { ReadOnly = true });

        var tools = (await session.Client.ListToolsAsync()).Select(tool => tool.Name).ToArray();

        Assert.Contains("get_object", tools);
        Assert.Contains("read_media", tools);
        Assert.DoesNotContain(tools, name => name.StartsWith("create_", StringComparison.Ordinal)
                                             || name.StartsWith("update_", StringComparison.Ordinal)
                                             || name.StartsWith("delete_", StringComparison.Ordinal));
        Assert.Contains("Person not found", await session.CallAsync("get_object", new { identifier = "I9999" }));
    }

    private Task<McpSession> StartWithRejectedTokenAsync(string kind)
    {
        var token = kind switch
        {
            "expired" => TestTokens.Expired(gramps.SecretKey),
            "foreign key" => TestTokens.ForeignKey(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
        return gramps.StartSessionAsync(gramps.Config, () => new AccessTokenSwap(token));
    }
}
