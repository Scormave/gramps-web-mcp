using Xunit;

namespace GrampsWeb.Mcp.Tests.Contract;

/// <summary>
/// Layer-1 contract tests: each <c>[JsonPropertyName]</c> on mapped MCP DTOs must match a key on the
/// corresponding Swagger 2 definition in repo-root <c>apispec.yaml</c> (copied next to test output).
/// Mapping: <c>Contract/swagger-dto-map.json</c> in this project.
/// </summary>
[Trait("Category", "Contract")]
public class DtoSwaggerSyncTests
{
    [Fact]
    public void Dto_JsonPropertyNames_AgreeWith_Apispec_Definitions()
    {
        var baseDir = AppContext.BaseDirectory;
        var specPath = Path.Combine(baseDir, "apispec.yaml");
        var mapPath = Path.Combine(baseDir, "Contract", "swagger-dto-map.json");
        Assert.True(File.Exists(specPath), $"Expected apispec at '{specPath}' (see GrampsWeb.Mcp.Tests.csproj CopyToOutputDirectory).");
        Assert.True(File.Exists(mapPath), $"Expected map at '{mapPath}'.");

        var errors = DtoSwaggerVerifier.VerifyAll(specPath, mapPath).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }
}
