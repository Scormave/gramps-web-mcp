using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

/// <summary>
/// Keeps the release metadata in step with the <c>&lt;Version&gt;</c> of GrampsWeb.Mcp.csproj, so a release commit
/// that misses a step of RELEASING.md fails before it is tagged. On a tag, scripts/check-release-version.sh then
/// checks the tag against the same version.
/// </summary>
public partial class ReleaseVersionTests
{
    private const string ProjectPath = "GrampsWeb.Mcp/GrampsWeb.Mcp.csproj";
    private const string ImageName = "ghcr.io/scormave/gramps-web-mcp";
    private const string RepositoryUrl = "https://github.com/Scormave/gramps-web-mcp";

    /// <summary>Files whose <c>pack-mcpb.sh &lt;rid&gt; x.y.z</c> examples name the current version.</summary>
    private static readonly string[] PackExampleFiles = ["mcpb/README.md", "scripts/pack-mcpb.sh"];

    private static readonly string Root = FindRepositoryRoot();
    private static readonly string ProjectVersion = ReadProjectVersion();

    [Fact]
    public void Server_Json_Declares_The_Project_Version()
    {
        using var server = JsonDocument.Parse(ReadText("server.json"));
        var version = server.RootElement.GetProperty("version").GetString();
        Assert.True(version == ProjectVersion,
            $"server.json: \"version\" is \"{version}\"; set it to \"{ProjectVersion}\", the <Version> of {ProjectPath}.");

        var images = server.RootElement.GetProperty("packages").EnumerateArray()
            .Where(package => package.GetProperty("registryType").GetString() == "oci")
            .Select(package => package.GetProperty("identifier").GetString())
            .ToArray();
        Assert.True(images.Length == 1, $"server.json: expected one oci package, found {images.Length}.");
        Assert.True(images[0] == $"{ImageName}:{ProjectVersion}",
            $"server.json: the oci package identifier is \"{images[0]}\"; set it to \"{ImageName}:{ProjectVersion}\".");
    }

    [Fact]
    public void Changelog_Has_A_Dated_Section_For_The_Project_Version()
    {
        var sections = ChangelogSections();
        Assert.True(sections.Count > 1 && sections[0].Name == "Unreleased",
            "CHANGELOG.md: the first section must be ## [Unreleased], followed by the releases, newest first.");

        var release = sections[1];
        Assert.True(release.Name == ProjectVersion,
            $"CHANGELOG.md: the newest release section is [{release.Name}], but {ProjectPath} declares "
            + $"{ProjectVersion}; move the Unreleased entries into ## [{ProjectVersion}] - YYYY-MM-DD.");
        Assert.True(release.Dated,
            $"CHANGELOG.md: write the release date into the heading: ## [{ProjectVersion}] - YYYY-MM-DD.");
        Assert.True(release.HasEntries, $"CHANGELOG.md: ## [{ProjectVersion}] lists no changes.");
    }

    [Fact]
    public void Changelog_Links_Compare_Each_Version_With_The_One_Before()
    {
        var sections = ChangelogSections();
        var expected = new Dictionary<string, string>();
        for (var i = 0; i < sections.Count; i++)
        {
            var name = sections[i].Name;
            var to = i == 0 ? "HEAD" : $"v{name}";
            expected[name] = i + 1 < sections.Count
                ? $"{RepositoryUrl}/compare/v{sections[i + 1].Name}...{to}"
                : $"{RepositoryUrl}/releases/tag/{to}";
        }

        var links = LinkDefinition().Matches(ReadText("CHANGELOG.md"))
            .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value);

        var problems = expected
            .Where(pair => links.GetValueOrDefault(pair.Key) != pair.Value)
            .Select(pair => links.TryGetValue(pair.Key, out var actual)
                ? $"[{pair.Key}]: {actual} should be {pair.Value}"
                : $"[{pair.Key}] has no link; add [{pair.Key}]: {pair.Value}")
            .Concat(links.Keys.Except(expected.Keys).Select(name => $"[{name}] links a version with no section"))
            .ToArray();
        Assert.True(problems.Length == 0, "CHANGELOG.md link definitions:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void Pack_Mcpb_Examples_Name_The_Project_Version()
    {
        var stale = PackExampleFiles
            .SelectMany(path => PackExample().Matches(ReadText(path))
                .Where(match => match.Groups[1].Value != ProjectVersion)
                .Select(match => $"{path}: {match.Value}"))
            .ToArray();
        Assert.True(stale.Length == 0,
            $"Set the version of these examples to {ProjectVersion}, the <Version> of {ProjectPath}:\n"
            + string.Join("\n", stale));
    }

    private static List<ChangelogSection> ChangelogSections()
    {
        var sections = new List<ChangelogSection>();
        foreach (var line in ReadText("CHANGELOG.md").Split('\n'))
        {
            var heading = SectionHeading().Match(line);
            if (heading.Success)
            {
                var dated = DateOnly.TryParseExact(heading.Groups[2].Value, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
                sections.Add(new ChangelogSection(heading.Groups[1].Value, dated));
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Assert.Fail($"CHANGELOG.md: \"{line}\" is not ## [Unreleased] or ## [x.y.z] - YYYY-MM-DD.");
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal) && sections.Count > 0)
            {
                sections[^1].HasEntries = true;
            }
        }

        return sections;
    }

    private static string ReadProjectVersion()
    {
        var versions = XDocument.Load(Path.Combine(Root, ProjectPath)).Descendants("Version").ToArray();
        Assert.True(versions.Length == 1, $"{ProjectPath}: expected one <Version>, found {versions.Length}.");
        return versions[0].Value.Trim();
    }

    private static string ReadText(string relativePath) =>
        File.ReadAllText(Path.Combine(Root, relativePath)).Replace("\r\n", "\n");

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "gramps-web-mcp.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Repository root with gramps-web-mcp.sln not found.");
    }

    private sealed record ChangelogSection(string Name, bool Dated)
    {
        public bool HasEntries { get; set; }
    }

    [GeneratedRegex(@"^## \[([^\]]+)\](?: - (\S+))?$")]
    private static partial Regex SectionHeading();

    [GeneratedRegex(@"^\[([^\]]+)\]: (\S+)$", RegexOptions.Multiline)]
    private static partial Regex LinkDefinition();

    [GeneratedRegex(@"pack-mcpb\.sh [\w-]+ (\d+\.\d+\.\d+\S*)")]
    private static partial Regex PackExample();
}
