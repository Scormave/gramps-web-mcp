using GrampsWeb.Mcp.Config;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsConfigTests
{
    private static readonly object EnvironmentLock = new();

    [Fact]
    public void FromEnvironment_Defaults_To_ReadWrite()
    {
        var config = LoadConfig(readOnlyEnv: null);

        Assert.False(config.ReadOnly);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("1")]
    public void FromEnvironment_Enables_ReadOnly_From_Environment(string value)
    {
        var config = LoadConfig(readOnlyEnv: value);

        Assert.True(config.ReadOnly);
    }

    [Theory]
    [InlineData("--read-only")]
    [InlineData("--gramps-read-only")]
    public void FromEnvironment_Enables_ReadOnly_From_Cli_Flag(string arg)
    {
        var config = LoadConfig(readOnlyEnv: null, arg);

        Assert.True(config.ReadOnly);
    }

    [Theory]
    [InlineData("--read-only=false")]
    [InlineData("--gramps-read-only=false")]
    public void FromEnvironment_Cli_False_Overrides_Environment_True(string arg)
    {
        var config = LoadConfig(readOnlyEnv: "true", arg);

        Assert.False(config.ReadOnly);
    }

    private static GrampsConfig LoadConfig(string? readOnlyEnv, params string[] args)
    {
        lock (EnvironmentLock)
        {
            var previous = CaptureEnvironment();
            try
            {
                Environment.SetEnvironmentVariable("GRAMPS_API_URL", "https://gramps-web.test/");
                Environment.SetEnvironmentVariable("GRAMPS_USERNAME", "user");
                Environment.SetEnvironmentVariable("GRAMPS_PASSWORD", "pass");
                Environment.SetEnvironmentVariable("GRAMPS_TREE_ID", "tree");
                Environment.SetEnvironmentVariable("GRAMPS_READ_ONLY", readOnlyEnv);

                return GrampsConfig.FromEnvironment(args);
            }
            finally
            {
                RestoreEnvironment(previous);
            }
        }
    }

    private static Dictionary<string, string?> CaptureEnvironment()
    {
        return new Dictionary<string, string?>
        {
            ["GRAMPS_API_URL"] = Environment.GetEnvironmentVariable("GRAMPS_API_URL"),
            ["GRAMPS_USERNAME"] = Environment.GetEnvironmentVariable("GRAMPS_USERNAME"),
            ["GRAMPS_PASSWORD"] = Environment.GetEnvironmentVariable("GRAMPS_PASSWORD"),
            ["GRAMPS_TREE_ID"] = Environment.GetEnvironmentVariable("GRAMPS_TREE_ID"),
            ["GRAMPS_READ_ONLY"] = Environment.GetEnvironmentVariable("GRAMPS_READ_ONLY")
        };
    }

    private static void RestoreEnvironment(Dictionary<string, string?> previous)
    {
        foreach (var (name, value) in previous)
            Environment.SetEnvironmentVariable(name, value);
    }
}
