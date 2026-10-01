using System.Net;
using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class ContextualNextStepsTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task QuickAddPerson_OnlySuggestsMissingVitalEvents_WithoutExtraReads(bool birth, bool death)
    {
        var handler = new CreationHandler();
        var config = new GrampsConfig("https://gramps.test", "user", "pass", "tree");
        var http = new HttpClient(handler);
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        var client = new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);

        var result = await CompositeTools.QuickAddPerson("Test Person",
            birthDate: birth ? "1900" : null, deathDate: death ? "1980" : null, client: client);

        Assert.Equal(!birth, result.Contains("eventType: \"Birth\""));
        Assert.Equal(!death, result.Contains("eventType: \"Death\""));
        Assert.Equal(1 + (birth ? 1 : 0) + (death ? 1 : 0), handler.Writes);
    }

    [Fact]
    public void ExistingPersonLinks_AreNotSuggestedAgain_UnknownEventsAreChecked()
    {
        var hints = ResponseEnvelope.PersonCreateNextSteps("person", hasEvents: true, hasFamily: true, hasNotes: true);
        var hint = Assert.Single(hints);
        Assert.Contains("Check linked events", hint);
        Assert.Contains("get_object", hint);
        Assert.DoesNotContain("add_event_to_person", hint);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FamilyHints_OnlySuggestMissingLinks(bool children, bool events)
    {
        var hints = ResponseEnvelope.FamilyCreateNextSteps("family", children, events);
        var text = string.Join('\n', hints);
        Assert.Equal(!children, text.Contains("childRefs:"));
        Assert.Equal(!events, text.Contains("eventRefs:"));
        Assert.Equal(!children || !events, text.Contains("update replaces the entire list"));
        if (children && events)
            Assert.DoesNotContain("Next steps:", ResponseEnvelope.CreateSuccess("Family", "family", null, null, hints));
    }

    [Fact]
    public void MissingHandles_DoNotProduceUnusableWriteExamples()
    {
        foreach (var hints in new[] { ResponseEnvelope.PersonCreateNextSteps(null), ResponseEnvelope.FamilyCreateNextSteps("") })
        {
            Assert.Contains("do not create it again", Assert.Single(hints));
            Assert.DoesNotContain("handle:", hints[0]);
        }
    }

    [Fact]
    public void StandaloneEventHint_AttachesExistingEventInsteadOfCreatingAnother()
    {
        var text = string.Join('\n', ResponseEnvelope.EventCreateNextSteps("event"));
        Assert.Contains("ref: \"event\"", text);
        Assert.Contains("preserve existing eventRefs", text);
        Assert.DoesNotContain("add_event_to_person", text);
    }

    private sealed class CreationHandler : HttpMessageHandler
    {
        public int Writes { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            var path = request.RequestUri!.AbsolutePath;
            var json = "{\"access_token\":\"token\",\"refresh_token\":\"refresh\",\"expires_in\":900}";
            if (path != "/api/token/")
            {
                Assert.Contains(path, new[] { "/api/people/", "/api/events/" });
                Writes++;
                json = $"{{\"handle\":\"created-{Writes}\",\"gramps_id\":\"I0001\"}}";
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
