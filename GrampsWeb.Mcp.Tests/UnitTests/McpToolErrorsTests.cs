using GrampsWeb.Mcp.Exceptions;
using GrampsWeb.Mcp.Tools;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class McpToolErrorsTests
{
    [Fact]
    public void ToMcpException_With_Created_Objects_Prepends_Partial_State()
    {
        var inner = new GrampsApiException(
            System.Net.HttpStatusCode.InternalServerError,
            "sqlite3.OperationalError: database is locked",
            GrampsRetryableWriteErrors.DatabaseLocked());

        var mapped = McpToolErrors.ToMcpException(inner, ["Event (Birth): E0001 (handle: h1)"]);

        var ex = Assert.IsType<McpException>(mapped);
        Assert.Contains("created some objects before failing", ex.Message);
        Assert.Contains("Do not retry the whole tool", ex.Message);
        Assert.Contains("Event (Birth): E0001 (handle: h1)", ex.Message);
        Assert.Contains("database is locked", ex.Message);
    }

    [Fact]
    public void ToMcpException_With_Empty_Created_Objects_Preserves_Original_Mapping()
    {
        var inner = new InvalidOperationException("Read-only mode is enabled");
        var mapped = McpToolErrors.ToMcpException(inner, Array.Empty<string>());

        var ex = Assert.IsType<McpException>(mapped);
        Assert.Equal("Read-only mode is enabled", ex.Message);
        Assert.DoesNotContain("created some objects", ex.Message);
    }
}
