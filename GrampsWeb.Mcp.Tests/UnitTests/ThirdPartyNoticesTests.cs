using System.Text.Json;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

/// <summary>
/// Keeps THIRD-PARTY-NOTICES.txt in step with the NuGet packages the server ships.
/// </summary>
public class ThirdPartyNoticesTests
{
    [Fact]
    public void Notices_List_Exactly_The_Restored_Server_Packages()
    {
        var root = FindRepositoryRoot();
        var assetsPath = Path.Combine(root, "GrampsWeb.Mcp", "obj", "project.assets.json");
        Assert.True(File.Exists(assetsPath), $"{assetsPath} is missing; run dotnet restore.");

        using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var restored = assets.RootElement.GetProperty("libraries").EnumerateObject()
            .Where(library => library.Value.GetProperty("type").GetString() == "package")
            .Select(library => library.Name.Split('/')[0])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Package names are the only lines of the file that start with "- ".
        var listed = File.ReadLines(Path.Combine(root, "THIRD-PARTY-NOTICES.txt"))
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("- ", StringComparison.Ordinal))
            .Select(line => line[2..])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = restored.Except(listed, StringComparer.OrdinalIgnoreCase).Order().ToArray();
        var stale = listed.Except(restored, StringComparer.OrdinalIgnoreCase).Order().ToArray();
        Assert.True(missing.Length == 0, "Add to THIRD-PARTY-NOTICES.txt with its license: " + string.Join(", ", missing));
        Assert.True(stale.Length == 0, "Remove from THIRD-PARTY-NOTICES.txt: " + string.Join(", ", stale));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "gramps-web-mcp.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Repository root with gramps-web-mcp.sln not found.");
    }
}
