using System.Net;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Input;
using Microsoft.Extensions.Logging.Abstractions;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Requests;
using GrampsWeb.Mcp.Tools;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class LinkUpdatesTests
{
    [Theory]
    [InlineData("add", 2, 1)]
    [InlineData("remove", 0, 0)]
    [InlineData("replace", 2, 0)]
    public async Task UpdatePerson_AppliesLinksToActualPutBody(string mode, int notes, int media)
    {
        var handler = new PersonHandler();
        var config = new GrampsConfig("https://link-update.test", "user", "pass", "tree");
        var http = new HttpClient(handler);
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        var client = new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);
        await PersonTools.UpdatePerson("person-handle", noteHandles: new FlexibleHandleList { Handles = ["old", "new"] },
            mediaHandles: new FlexibleHandleList { Handles = mode == "remove" ? ["media"] : [] }, client: client, linkMode: mode);
        var body = handler.Body!.Value;
        Assert.Equal(notes, body.GetProperty("note_list").GetArrayLength());
        Assert.Equal(media, body.GetProperty("media_list").GetArrayLength());
        Assert.Equal("citation", body.GetProperty("citation_list")[0].GetString());
        if (mode == "add")
            Assert.Equal(4, body.GetProperty("media_list")[0].GetProperty("rect").GetArrayLength());
    }

    private sealed class PersonHandler : HttpMessageHandler
    {
        public JsonElement? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string json;
            if (request.RequestUri!.AbsolutePath.StartsWith("/api/token/"))
                json = """{"access_token":"token","refresh_token":"refresh","expires_in":900}""";
            else if (request.Method == HttpMethod.Get)
                json = """{"handle":"person-handle","note_list":["old"],"citation_list":["citation"],"media_list":[{"ref":"media","rect":[1,2,3,4]}]}""";
            else
            {
                Assert.Equal(HttpMethod.Put, request.Method);
                Body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
                json = "{}";
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public void Add_PreservesMetadataAndOrder_AndDeduplicatesNewHandles()
    {
        var original = new MediaRefRequest { Ref = "a", Rect = [1, 2, 3, 4], NoteList = ["note"] };
        var result = LinkUpdates.Apply([original],
            [new MediaRefRequest { Ref = "a" }, new() { Ref = "b" }, new() { Ref = "b" }], "add", x => x.Ref)!;
        Assert.Equal(new[] { "a", "b" }, result.Select(x => x.Ref));
        Assert.Same(original, result[0]);
    }

    [Fact]
    public void Remove_RemovesAllMatchingReferences_AndKeepsOthers()
    {
        Assert.Equal(new[] { "b" }, LinkUpdates.Apply(["a", "b", "a"], ["a", "missing"], "remove", x => x));
    }

    [Theory]
    [InlineData("replace")]
    [InlineData("add")]
    [InlineData("remove")]
    public void OmittedAndEmptyLists_HaveDistinctSemantics(string mode)
    {
        string[] original = ["a"];
        Assert.Same(original, LinkUpdates.Apply(original, null, mode, x => x));
        Assert.Equal(mode == "replace" ? [] : original, LinkUpdates.Apply(original, [], mode, x => x));
    }

    [Fact]
    public void InvalidModeAndEmptyHandles_AreRejected()
    {
        Assert.Throws<ModelContextProtocol.McpException>(() => LinkUpdates.Validate("merge"));
        Assert.Throws<ModelContextProtocol.McpException>(() => LinkUpdates.Apply(["a"], [""], "remove", x => x));
    }

    [Fact]
    public async Task UpdateLease_SerializesReadModifyWrite_WithoutBlockingNestedHttpGate()
    {
        var gate = new MutationGate(true, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        var first = await gate.BeginUpdateAsync();
        var second = gate.BeginUpdateAsync();
        Assert.False(second.IsCompleted);
        await gate.RunAsync(() => Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(1));
        first.Dispose();
        first.Dispose(); // Disposing twice must not release another owner's lease.
        using var next = await second;
    }

    [Fact]
    public async Task UpdateLease_ReleasesAfterFailure_AndDisabledGateDoesNotSerialize()
    {
        var gate = new MutationGate(true, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using var lease = await gate.BeginUpdateAsync();
            throw new InvalidOperationException();
        });
        using var recovered = await gate.BeginUpdateAsync();
        using var one = await MutationGate.Disabled.BeginUpdateAsync();
        using var two = await MutationGate.Disabled.BeginUpdateAsync();
    }
}
