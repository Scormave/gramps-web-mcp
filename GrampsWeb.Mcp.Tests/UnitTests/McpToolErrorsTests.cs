using GrampsWeb.Mcp.Exceptions;
using GrampsWeb.Mcp.Tools;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class McpToolErrorsTests
{
    [Fact]
    public void ToMcpException_Keeps_Gramps_Api_Message()
    {
        var inner = new GrampsApiException(
            System.Net.HttpStatusCode.InternalServerError,
            "sqlite3.OperationalError: database is locked",
            GrampsRetryableWriteErrors.DatabaseLocked());

        var mapped = McpToolErrors.ToMcpException(inner);

        var ex = Assert.IsType<McpException>(mapped);
        Assert.Equal(inner.Message, ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void ToMcpException_Wraps_Other_Exceptions_With_Their_Message()
    {
        var inner = new InvalidOperationException("Read-only mode is enabled");
        var mapped = McpToolErrors.ToMcpException(inner);

        var ex = Assert.IsType<McpException>(mapped);
        Assert.Equal("Read-only mode is enabled", ex.Message);
    }
}
