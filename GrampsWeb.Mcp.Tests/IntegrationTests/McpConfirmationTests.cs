using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.IntegrationTests;

/// <summary>
/// delete_object over a stdio-like session with the SDK's own client: the confirmation reaches the
/// client's elicitation handler, and only a ticked accept deletes.
/// </summary>
public class McpConfirmationTests
{
    [Fact]
    public async Task DeleteObject_AsksTheClient_AndDeletesOnATickedAccept()
    {
        var call = await DeleteNoteAsync(ConfirmationMode.WhenSupported, Answer("accept", ticked: true));

        var question = Assert.Single(call.Questions);
        Assert.StartsWith("Delete Note N0001: [General] A new note?", question.Message);
        var field = Assert.IsType<ElicitRequestParams.BooleanSchema>(
            Assert.Single(question.RequestedSchema!.Properties, p => p.Key == "confirm").Value);
        Assert.False(field.Default);
        Assert.Equal(["confirm"], question.RequestedSchema.Required);
        Assert.Contains("action: deleted", call.Text);
        Assert.True(call.Deleted);
    }

    [Theory]
    [InlineData("accept", false, "declined")]
    [InlineData("decline", null, "declined")]
    [InlineData("cancel", null, "cancelled")]
    public async Task DeleteObject_DeletesNothing_WithoutATickedAccept(string action, bool? ticked, string reply)
    {
        var call = await DeleteNoteAsync(ConfirmationMode.WhenSupported, Answer(action, ticked));

        Assert.Single(call.Questions);
        Assert.Contains($"action: {reply}", call.Text);
        Assert.False(call.Deleted);
    }

    [Fact]
    public async Task DeleteObject_WithoutElicitation_DeletesAsBefore()
    {
        var call = await DeleteNoteAsync(ConfirmationMode.WhenSupported, answer: null);

        Assert.Contains("action: deleted", call.Text);
        Assert.True(call.Deleted);
    }

    [Fact]
    public async Task DeleteObject_WhenConfirmationIsRequired_RefusesAClientWithoutElicitation()
    {
        var call = await DeleteNoteAsync(ConfirmationMode.Required, answer: null);

        Assert.Contains("action: not_confirmed", call.Text);
        Assert.Contains("GRAMPS_CONFIRMATION=required", call.Text);
        Assert.False(call.Deleted);
    }

    [Fact]
    public async Task DeleteObject_WhenConfirmationIsOff_DoesNotAsk()
    {
        var call = await DeleteNoteAsync(ConfirmationMode.Off, Answer("decline", ticked: null));

        Assert.Empty(call.Questions);
        Assert.Contains("action: deleted", call.Text);
        Assert.True(call.Deleted);
    }

    private sealed record DeleteCall(string Text, List<ElicitRequestParams> Questions, bool Deleted);

    private static Func<ElicitRequestParams, ElicitResult> Answer(string action, bool? ticked) => _ => new ElicitResult
    {
        Action = action,
        Content = ticked is { } value
            ? new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.SerializeToElement(value) }
            : null
    };

    /// <summary>
    /// Deletes note N0001 through a client that answers confirmations with <paramref name="answer"/>,
    /// or that does not support them when it is null.
    /// </summary>
    private static async Task<DeleteCall> DeleteNoteAsync(ConfirmationMode mode, Func<ElicitRequestParams, ElicitResult>? answer)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var gramps = new NoteServer();
        var config = new GrampsConfig("https://gramps.test", "user", "pass", "tree", Confirmation: mode);
        var toServer = new Pipe();
        var toClient = new Pipe();

        var builder = Host.CreateEmptyApplicationBuilder(settings: null);
        builder.Services.AddGrampsMcpCore(config);
        builder.Services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => gramps));
        builder.Services.AddMcpServer()
            .WithStreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream())
            .WithGrampsToolProfile(config);
        using var host = builder.Build();
        await host.StartAsync(timeout.Token);

        var questions = new List<ElicitRequestParams>();
        var options = new McpClientOptions();
        if (answer is not null)
        {
            options.Handlers.ElicitationHandler = (request, _) =>
            {
                questions.Add(request!);
                return ValueTask.FromResult(answer(request!));
            };
        }

        string text;
        await using (var client = await McpClient.CreateAsync(
                         new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()),
                         options, cancellationToken: timeout.Token))
        {
            var result = await client.CallToolAsync("delete_object",
                new Dictionary<string, object?> { ["objectType"] = "note", ["handle"] = "n1" },
                cancellationToken: timeout.Token);
            text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
            Assert.False(result.IsError == true, text);
        }

        await toServer.Writer.CompleteAsync();
        await host.StopAsync(timeout.Token);
        return new DeleteCall(text, questions, gramps.Deleted);
    }

    /// <summary>Gramps Web with note N0001 and no backlinks to it.</summary>
    private sealed class NoteServer : HttpMessageHandler
    {
        public bool Deleted { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (request.Method == HttpMethod.Delete && path == "/api/notes/n1")
            {
                Deleted = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            return Task.FromResult(path switch
            {
                "/api/token/" => Json("""{"access_token":"token","refresh_token":"refresh","expires_in":900}"""),
                "/api/notes/n1?backlinks=true" => Json("""{"handle": "n1", "gramps_id": "N0001", "change": 1, "backlinks": {}}"""),
                "/api/notes/n1" => Json("""{"handle": "n1", "gramps_id": "N0001", "text": "A new note", "type": "General"}"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("""{"error": {"code": 404, "message": "Not found"}}""", Encoding.UTF8, "application/json")
                }
            });
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
