using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;
using Microsoft.Extensions.Logging.Abstractions;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Requests;
using GrampsWeb.Mcp.Serialization;
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
        await PersonTools.UpdatePerson("person-handle", noteHandles: new FlexibleHandleList { Handles = ["old", "new"] },
            mediaHandles: new FlexibleMediaLinkList { Items = mode == "remove" ? [new MediaLink("media")] : [] },
            client: Client(handler), linkMode: mode);
        var body = handler.Body!.Value;
        Assert.Equal(notes, body.GetProperty("note_list").GetArrayLength());
        Assert.Equal(media, body.GetProperty("media_list").GetArrayLength());
        Assert.Equal("citation", body.GetProperty("citation_list")[0].GetString());
        if (mode == "add")
            Assert.Equal(4, body.GetProperty("media_list")[0].GetProperty("rect").GetArrayLength());
    }

    [Fact]
    public async Task UpdatePerson_Adds_Links_By_Default()
    {
        var handler = new PersonHandler();

        await PersonTools.UpdatePerson("person-handle", noteHandles: new FlexibleHandleList { Handles = ["new"] }, client: Client(handler));

        Assert.Equal(["old", "new"], handler.Body!.Value.GetProperty("note_list").EnumerateArray().Select(n => n.GetString()));
    }

    private const string StoredPerson = """{"handle":"person-handle","note_list":["old"],"citation_list":["citation"],"media_list":[{"ref":"media","rect":[1,2,3,4]}]}""";

    private static GrampsApiClient Client(HttpMessageHandler handler)
    {
        var config = new GrampsConfig("https://link-update.test", "user", "pass", "tree");
        var http = new HttpClient(handler);
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);
    }

    /// <summary>The stored object <paramref name="stored"/>, loaded as an update tool loads it.</summary>
    private static async Task<GrampsObjectPatch> Load(string stored) =>
        (await GrampsObjectPatch.LoadAsync(Client(new PersonHandler(stored)), "/api/people/person-handle"))!;

    private static void AssertJsonEqual(string expected, JsonNode? actual)
    {
        var parsed = JsonNode.Parse(expected);
        Assert.True(JsonNode.DeepEquals(parsed, actual), $"Expected:\n{parsed}\nActual:\n{actual}");
    }

    private sealed class PersonHandler(string stored = StoredPerson) : HttpMessageHandler
    {
        public JsonElement? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string json;
            if (request.RequestUri!.AbsolutePath.StartsWith("/api/token/"))
                json = """{"access_token":"token","refresh_token":"refresh","expires_in":900}""";
            else if (request.Method == HttpMethod.Get)
                json = stored;
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
    public async Task Add_Keeps_Stored_Links_And_Adds_A_New_Handle_Once()
    {
        var patch = await Load("""{"note_list":["a"]}""");

        patch.ApplyHandles("note_list", ["a", "b", "b"], "add");

        AssertJsonEqual("""["a","b"]""", patch.Root["note_list"]);
    }

    [Fact]
    public async Task Remove_Removes_Every_Link_To_A_Supplied_Handle_And_Keeps_Others()
    {
        var patch = await Load("""{"note_list":["a","b","a"]}""");

        patch.ApplyHandles("note_list", ["a", "missing"], "remove");

        AssertJsonEqual("""["b"]""", patch.Root["note_list"]);
    }

    [Theory]
    [InlineData("replace")]
    [InlineData("add")]
    [InlineData("remove")]
    public async Task Omitted_And_Empty_Lists_Have_Distinct_Semantics(string mode)
    {
        var patch = await Load("""{"note_list":["a"]}""");

        patch.ApplyHandles("note_list", null, mode);
        AssertJsonEqual("""["a"]""", patch.Root["note_list"]);

        patch.ApplyHandles("note_list", [], mode);
        AssertJsonEqual(mode == "replace" ? "[]" : """["a"]""", patch.Root["note_list"]);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("remove")]
    public async Task Invalid_Mode_And_Empty_Handles_Are_Rejected(string mode)
    {
        var patch = await Load("""{"note_list":["a"]}""");

        Assert.Throws<ModelContextProtocol.McpException>(() => LinkUpdates.Validate("merge"));
        Assert.Throws<ModelContextProtocol.McpException>(() => patch.ApplyHandles("note_list", ["b", " "], mode));
    }

    [Fact]
    public async Task Add_Updates_The_Fields_Sent_For_A_Stored_Link_And_Keeps_The_Rest()
    {
        var patch = await Load("""
            {"event_ref_list":[{"_class":"EventRef","ref":"e1","role":"Primary","private":true,"citation_list":["c1"],
                                "note_list":["n1"],"attribute_list":[{"type":"Age","value":"65"}]}]}
            """);

        patch.ApplyRefs("event_ref_list", [new EventRefRequest { Ref = "e1", Role = "Witness", NoteList = ["n1", "n2"] }], "add");

        AssertJsonEqual("""
            [{"_class":"EventRef","ref":"e1","role":"Witness","private":true,"citation_list":["c1"],
              "note_list":["n1","n2"],"attribute_list":[{"type":"Age","value":"65"}]}]
            """, patch.Root["event_ref_list"]);
    }

    [Fact]
    public async Task Add_Leaves_A_Stored_Link_Given_As_A_Bare_Handle_And_Gives_A_New_One_The_Default()
    {
        var patch = await Load("""{"event_ref_list":[{"_class":"EventRef","ref":"e1","role":"Witness"}]}""");

        // EventRoles.CheckAsync gives a bare handle the default role before the patch is applied.
        patch.ApplyRefs("event_ref_list",
            [new EventRefRequest { Ref = "e1", Role = "Primary", BareHandle = true }, new EventRefRequest { Ref = "e2", Role = "Primary", BareHandle = true }],
            "add", r => r.BareHandle);

        AssertJsonEqual("""
            [{"_class":"EventRef","ref":"e1","role":"Witness"},{"ref":"e2","role":"Primary"}]
            """, patch.Root["event_ref_list"]);
    }

    [Fact]
    public async Task Add_Changes_The_Role_Of_The_First_Link_When_No_Link_Has_The_Role_Given()
    {
        var patch = await Load("""
            {"event_ref_list":[{"_class":"EventRef","ref":"e1","role":"Primary"},{"_class":"EventRef","ref":"e1","role":"Witness"}]}
            """);

        patch.ApplyRefs("event_ref_list",
            [new EventRefRequest { Ref = "e1", Role = "Witness", NoteList = ["n1"] }, new EventRefRequest { Ref = "e1", Role = "Informant" }],
            "add");

        AssertJsonEqual("""
            [{"_class":"EventRef","ref":"e1","role":"Informant"},{"_class":"EventRef","ref":"e1","role":"Witness","note_list":["n1"]}]
            """, patch.Root["event_ref_list"]);
    }

    [Fact]
    public async Task Add_Ignores_A_Repeated_Link_With_No_Stored_Link_Left()
    {
        var patch = await Load("""{"event_ref_list":[{"_class":"EventRef","ref":"e1","role":"Primary"}]}""");

        patch.ApplyRefs("event_ref_list",
            [new EventRefRequest { Ref = "e1", Role = "Primary" }, new EventRefRequest { Ref = "e1", Role = "Witness" }],
            "add");

        AssertJsonEqual("""[{"_class":"EventRef","ref":"e1","role":"Primary"}]""", patch.Root["event_ref_list"]);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("replace")]
    public async Task A_Child_Given_As_A_Bare_Handle_Keeps_Its_Stored_Relations(string mode)
    {
        const string adopted = """{"_class":"ChildRef","ref":"i-child","frel":"Adopted","mrel":"Birth","citation_list":["c1"]}""";
        var patch = await Load($$"""{"child_ref_list":[{{adopted}}]}""");
        var child = FlexibleChildRefListJsonConverter.ParseChildRefLine("i-child")!;

        patch.ApplyRefs("child_ref_list", [child], mode, r => r.BareHandle);

        Assert.Equal("Birth", child.FatherRelType);
        AssertJsonEqual($"[{adopted}]", patch.Root["child_ref_list"]);
    }

    [Fact]
    public async Task Add_Sets_The_Relation_Given_For_A_Stored_Child()
    {
        var patch = await Load("""{"child_ref_list":[{"_class":"ChildRef","ref":"i-child","frel":"Birth","mrel":"Birth","private":true}]}""");

        patch.ApplyRefs("child_ref_list", [new GrampsChildRef { Ref = "i-child", FatherRelType = "Stepchild" }], "add", r => r.BareHandle);

        AssertJsonEqual("""[{"_class":"ChildRef","ref":"i-child","frel":"Stepchild","mrel":"Birth","private":true}]""",
            patch.Root["child_ref_list"]);
    }

    [Fact]
    public async Task Add_Sets_The_Call_Number_Of_A_Stored_Repository_Link()
    {
        var patch = await Load("""
            {"reporef_list":[{"_class":"RepoRef","ref":"r1","call_number":"f. 1","media_type":"Book","private":true,"note_list":["n1"]}]}
            """);

        patch.ApplyRefs("reporef_list", [new GrampsRepositoryRef { Ref = "r1", CallNumber = "f. 1, inv. 2, file 3" }], "add");

        AssertJsonEqual("""
            [{"_class":"RepoRef","ref":"r1","call_number":"f. 1, inv. 2, file 3","media_type":"Book","private":true,"note_list":["n1"]}]
            """, patch.Root["reporef_list"]);
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
