using GrampsWeb.Mcp.Logging;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsLogLevelTests
{
    [Theory]
    [InlineData(null, LogLevel.Information)]
    [InlineData("", LogLevel.Information)]
    [InlineData("debug", LogLevel.Debug)]
    [InlineData(" Trace ", LogLevel.Trace)]
    [InlineData("Warning", LogLevel.Warning)]
    [InlineData("verbose", LogLevel.Information)]
    [InlineData("-1", LogLevel.Information)]
    [InlineData("1", LogLevel.Information)]
    [InlineData("${user_config.gramps_log_level}", LogLevel.Information)]
    public void Parse_Resolves_Level_Names_And_Defaults_To_Information(string? value, LogLevel expected)
    {
        Assert.Equal(expected, GrampsLogLevel.Parse(value));
    }
}
