using System.Text.Json;
using GrampsWeb.Mcp.Health;
using GrampsWeb.Mcp.Hosting;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class HealthEndpointExtensionsTests
{
    [Fact]
    public void CreateHealthPayload_Returns_Minimal_Payload()
    {
        var status = CreateStatus();

        using var doc = SerializePayload(status);
        var root = doc.RootElement;

        Assert.Equal("healthy", root.GetProperty("status").GetString());
        Assert.Single(root.EnumerateObject());
    }

    private static JsonDocument SerializePayload(GrampsConnectivityStatus status)
    {
        var payload = HealthEndpointExtensions.CreateHealthPayload(status);
        return JsonDocument.Parse(JsonSerializer.Serialize(payload));
    }

    private static GrampsConnectivityStatus CreateStatus()
    {
        return new GrampsConnectivityStatus(
            IsHealthy: true,
            ApiUrl: "https://gramps.example",
            ConfiguredTreeId: "configured-tree",
            TreeName: "Example Tree",
            TreeDatabaseId: "5f850009",
            GrampsVersion: "6.0.0");
    }
}
