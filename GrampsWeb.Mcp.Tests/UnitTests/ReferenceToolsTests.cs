using System.Net;
using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Resources;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class ReferenceToolsTests
{
    [Theory]
    [InlineData("dates", "\"dates\"", "structured_fields")]
    [InlineData("structured_fields", "\"structured_fields\"", "\"name_schema\"")]
    [InlineData("name_schema", "\"name_schema\"", "\"dates\"")]
    public async Task GetReference_InputGuideSection_ReturnsOnlyRequestedSection(
        string section,
        string expected,
        string absent)
    {
        var result = await ReferenceTools.GetReference("input-guide", section);

        Assert.Contains(expected, result);
        Assert.DoesNotContain(absent, result);
    }

    [Fact]
    public async Task GetReference_RejectsUnknownTopic()
    {
        var ex = await Assert.ThrowsAsync<McpException>(
            () => ReferenceTools.GetReference("unknown"));

        Assert.Equal("Invalid topic. Must be one of: input-guide, types, metadata, name-settings.", ex.Message);
    }

    [Fact]
    public async Task GetReference_RejectsUnknownInputGuideSection()
    {
        var ex = await Assert.ThrowsAsync<McpException>(
            () => ReferenceTools.GetReference("input-guide", "unknown"));

        Assert.Contains("Unknown input-guide section 'unknown'", ex.Message);
    }

    [Fact]
    public async Task GetReference_RejectsSectionsForMetadata()
    {
        var ex = await Assert.ThrowsAsync<McpException>(
            () => ReferenceTools.GetReference("metadata", "anything"));

        Assert.Equal("section is not supported for topic metadata.", ex.Message);
    }

    [Fact]
    public async Task GetReference_RejectsUnknownNameSettingsSection()
    {
        var ex = await Assert.ThrowsAsync<McpException>(
            () => ReferenceTools.GetReference("name-settings", "anything"));

        Assert.Contains("Unknown name-settings section 'anything'", ex.Message);
    }

    [Fact]
    public void BuildInputGuideText_WithoutSection_PreservesAllSections()
    {
        var result = GrampsResources.BuildInputGuideText();

        Assert.Contains("\"dates\"", result);
        Assert.Contains("\"structured_fields\"", result);
        Assert.Contains("\"name_schema\"", result);
    }

    [Fact]
    public async Task GetReference_StructuredFieldsSubsection_ReturnsOnlyRequestedGuide()
    {
        var result = await ReferenceTools.GetReference("input-guide", "structured_fields.addresses");

        Assert.Contains("\"addresses\"", result);
        Assert.DoesNotContain("\"repository_refs\"", result);
        Assert.DoesNotContain("\"person_associations\"", result);
    }

    [Fact]
    public async Task GetReference_TypesSection_ReturnsOnlyRequestedCategory()
    {
        using var handler = new TypesHandler();
        using var http = new HttpClient(handler);
        var config = new GrampsConfig("https://gramps-web.test", "user", "pass", "tree");
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        var client = new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);

        var result = await ReferenceTools.GetReference("types", "EVENT_TYPES", client);

        Assert.Contains("Event types:", result);
        Assert.Contains("Birth", result);
        Assert.Contains("Custom Event", result);
        Assert.DoesNotContain("Place types:", result);
    }

    [Fact]
    public async Task GetReference_RejectsUnknownTypesSection()
    {
        using var handler = new TypesHandler();
        using var http = new HttpClient(handler);
        var config = new GrampsConfig("https://gramps-web.test", "user", "pass", "tree");
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        var client = new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);

        var ex = await Assert.ThrowsAsync<McpException>(
            () => ReferenceTools.GetReference("types", "unknown_types", client));

        Assert.Contains("Unknown types section 'unknown_types'", ex.Message);
        Assert.Contains("event_types", ex.Message);
    }

    private sealed class TypesHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            return path switch
            {
                "/api/token/" => Task.FromResult(JsonResponse("""{"access_token":"token","refresh_token":"refresh","expires_in":900}""")),
                "/api/types/default/" => Task.FromResult(JsonResponse("""{"event_types":["Birth"],"place_types":["City"]}""")),
                "/api/types/custom/" => Task.FromResult(JsonResponse("""{"event_types":["Custom Event"]}""")),
                _ => throw new Xunit.Sdk.XunitException($"Unexpected request: {path}")
            };
        }

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
