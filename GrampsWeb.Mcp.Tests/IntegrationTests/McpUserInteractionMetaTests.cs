using System.IO.Pipelines;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.IntegrationTests;

/// <summary>
/// tools/list as the SDK's own client reads it over a stdio-like session: delete_object, merge_objects and
/// undo_change carry the <c>anthropic/requiresUserInteraction</c> metadata unless
/// <c>GRAMPS_REQUIRE_USER_INTERACTION=false</c> took it off.
/// </summary>
public class McpUserInteractionMetaTests
{
    private static readonly string[] ConfirmedTools = ["delete_object", "merge_objects", "undo_change"];

    [Fact]
    public async Task ListTools_MarksDeleteMergeAndUndo_ByDefault()
    {
        var tools = await ListToolsAsync(new GrampsConfig("https://gramps.test", "user", "pass", "tree"));

        Assert.Equal(ConfirmedTools, tools.Where(tool => tool.Marked).Select(tool => tool.Name));
    }

    [Fact]
    public async Task ListTools_MarksNoTool_WhenUserInteractionIsNotRequired()
    {
        var tools = await ListToolsAsync(
            new GrampsConfig("https://gramps.test", "user", "pass", "tree", RequireUserInteraction: false));

        Assert.DoesNotContain(tools, tool => tool.Marked);
        Assert.Equal(ConfirmedTools, tools.Where(tool => !tool.HasMeta).Select(tool => tool.Name).Intersect(ConfirmedTools));
    }

    private sealed record ListedTool(string Name, bool Marked, bool HasMeta);

    /// <summary>The published tools in name order, each with whether the client received the metadata.</summary>
    private static async Task<List<ListedTool>> ListToolsAsync(GrampsConfig config)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var toServer = new Pipe();
        var toClient = new Pipe();

        var builder = Host.CreateEmptyApplicationBuilder(settings: null);
        builder.Services.AddGrampsMcpCore(config);
        builder.Services.AddMcpServer()
            .WithStreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream())
            .WithGrampsToolProfile(config);
        using var host = builder.Build();
        await host.StartAsync(timeout.Token);

        List<ListedTool> tools;
        await using (var client = await McpClient.CreateAsync(
                         new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()),
                         cancellationToken: timeout.Token))
        {
            tools = (await client.ListToolsAsync(cancellationToken: timeout.Token))
                .Select(tool => new ListedTool(
                    tool.Name,
                    tool.ProtocolTool.Meta?[McpToolProfileExtensions.RequiresUserInteractionMeta]?.GetValue<bool>() == true,
                    tool.ProtocolTool.Meta is not null))
                .OrderBy(tool => tool.Name, StringComparer.Ordinal)
                .ToList();
        }

        await toServer.Writer.CompleteAsync();
        await host.StopAsync(timeout.Token);
        return tools;
    }
}
