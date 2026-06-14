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
        Assert.False(config.MediaResourcesEnabled);
        Assert.Equal(5 * 1024 * 1024, config.MediaMaxBytes);
        Assert.Equal(
            new[] { "image/jpeg", "image/png", "image/webp", "application/pdf" },
            config.EffectiveMediaAllowedMimeTypes);
        Assert.False(config.MediaAllowPrivate);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("1")]
    public void FromEnvironment_Enables_ReadOnly_From_Environment(string value)
    {
        var config = LoadConfig(readOnlyEnv: value);

        Assert.True(config.ReadOnly);
    }

    [Fact]
    public void FromEnvironment_Parses_Media_Resource_Settings()
    {
        var config = LoadConfig(
            readOnlyEnv: null,
            mediaResourcesEnabled: "true",
            mediaMaxBytes: "12345",
            mediaAllowedMimeTypes: " Image/JPEG,application/pdf,image/jpeg ",
            mediaAllowPrivate: "1");

        Assert.True(config.MediaResourcesEnabled);
        Assert.Equal(12345, config.MediaMaxBytes);
        Assert.Equal(new[] { "image/jpeg", "application/pdf" }, config.EffectiveMediaAllowedMimeTypes);
        Assert.True(config.MediaAllowPrivate);
    }

    [Fact]
    public void FromEnvironment_Rejects_Non_Positive_Media_Max_Bytes()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => LoadConfig(readOnlyEnv: null, mediaMaxBytes: "0"));

        Assert.Contains("must be a positive integer", ex.Message);
    }

    [Fact]
    public void FromEnvironment_Rejects_Unparseable_Media_Max_Bytes()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => LoadConfig(readOnlyEnv: null, mediaMaxBytes: "not-a-number"));

        Assert.Contains("must be a valid integer", ex.Message);
    }

    private static GrampsConfig LoadConfig(
        string? readOnlyEnv,
        string? mediaResourcesEnabled = null,
        string? mediaMaxBytes = null,
        string? mediaAllowedMimeTypes = null,
        string? mediaAllowPrivate = null)
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
                Environment.SetEnvironmentVariable("GRAMPS_MEDIA_RESOURCES_ENABLED", mediaResourcesEnabled);
                Environment.SetEnvironmentVariable("GRAMPS_MEDIA_MAX_BYTES", mediaMaxBytes);
                Environment.SetEnvironmentVariable("GRAMPS_MEDIA_ALLOWED_MIME_TYPES", mediaAllowedMimeTypes);
                Environment.SetEnvironmentVariable("GRAMPS_MEDIA_ALLOW_PRIVATE", mediaAllowPrivate);

                return GrampsConfig.FromEnvironment();
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
            ["GRAMPS_READ_ONLY"] = Environment.GetEnvironmentVariable("GRAMPS_READ_ONLY"),
            ["GRAMPS_MEDIA_RESOURCES_ENABLED"] = Environment.GetEnvironmentVariable("GRAMPS_MEDIA_RESOURCES_ENABLED"),
            ["GRAMPS_MEDIA_MAX_BYTES"] = Environment.GetEnvironmentVariable("GRAMPS_MEDIA_MAX_BYTES"),
            ["GRAMPS_MEDIA_ALLOWED_MIME_TYPES"] = Environment.GetEnvironmentVariable("GRAMPS_MEDIA_ALLOWED_MIME_TYPES"),
            ["GRAMPS_MEDIA_ALLOW_PRIVATE"] = Environment.GetEnvironmentVariable("GRAMPS_MEDIA_ALLOW_PRIVATE")
        };
    }

    private static void RestoreEnvironment(Dictionary<string, string?> previous)
    {
        foreach (var (name, value) in previous)
            Environment.SetEnvironmentVariable(name, value);
    }
}
