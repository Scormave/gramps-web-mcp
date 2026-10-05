using System.Net;
using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class RepositoryToolsTests
{
    [Fact]
    public async Task CreateRepository_Result_Shows_The_Name_Not_The_Type()
    {
        var result = await RepositoryTools.CreateRepository("City Archive", repoType: "Archive", client: CreateClient());

        Assert.Contains("name: \"City Archive\"", result);
        Assert.Contains("gramps_id: R0001", result);
        Assert.DoesNotContain("name: \"Archive\"", result);
    }

    private static GrampsApiClient CreateClient()
    {
        var config = new GrampsConfig("https://repository-tools.test", "user", "pass", "tree");
        var http = new HttpClient(new RepositoryHandler());
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);
    }

    /// <summary>Answers the token and type routes and the repository post.</summary>
    private sealed class RepositoryHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var json = path switch
            {
                "/api/token/" => """{"access_token":"token","refresh_token":"refresh","expires_in":900}""",
                "/api/types/default/" => """{"repository_types":["Archive","Library"]}""",
                "/api/repositories/" when request.Method == HttpMethod.Post =>
                    """[{"_class":"Repository","type":"add","old":null,"new":{"_class":"Repository","handle":"repo-h","gramps_id":"R0001"}}]""",
                _ when path.StartsWith("/api/types/") => "{}",
                _ => null
            };

            return Task.FromResult(json == null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
