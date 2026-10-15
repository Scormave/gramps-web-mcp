using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GrampsWeb.Mcp.IntegrationTests;

/// <summary>Access tokens Gramps Web rejects, signed the way flask-jwt-extended signs its own (HS256).</summary>
internal static class TestTokens
{
    /// <summary>Signed with the container's key but expired: Gramps Web answers 401.</summary>
    public static string Expired(string secretKey) =>
        Access(secretKey, DateTimeOffset.UtcNow.AddMinutes(-30));

    /// <summary>
    /// Valid for 15 minutes but signed with another key, as after the server's SECRET_KEY changed:
    /// Gramps Web answers 422.
    /// </summary>
    public static string ForeignKey() =>
        Access(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), DateTimeOffset.UtcNow.AddMinutes(15));

    private static string Access(string key, DateTimeOffset expires)
    {
        var issued = expires.AddMinutes(-15).ToUnixTimeSeconds();
        var header = Encode(new { alg = "HS256", typ = "JWT" });
        var payload = Encode(new
        {
            fresh = false,
            iat = issued,
            jti = Guid.NewGuid().ToString(),
            type = "access",
            sub = GrampsWebFixture.OwnerName,
            nbf = issued,
            exp = expires.ToUnixTimeSeconds()
        });
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.ASCII.GetBytes($"{header}.{payload}"));
        return $"{header}.{payload}.{Base64Url.EncodeToString(signature)}";
    }

    private static string Encode(object value) => Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(value));
}

/// <summary>
/// Puts <paramref name="accessToken"/> in place of the access token of the first sign-in, as if
/// Gramps Web had issued one it rejects later. The refresh token of that sign-in stays valid.
/// </summary>
internal sealed class AccessTokenSwap(string accessToken) : DelegatingHandler
{
    private int _swapped;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (request.RequestUri?.AbsolutePath != "/api/token/"
            || !response.IsSuccessStatusCode
            || Interlocked.Exchange(ref _swapped, 1) == 1)
            return response;

        var tokens = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
        tokens["access_token"] = accessToken;
        response.Content = new StringContent(tokens.ToJsonString(), Encoding.UTF8, "application/json");
        return response;
    }
}
