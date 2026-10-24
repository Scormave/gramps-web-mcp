using System.Reflection;
using System.Text.RegularExpressions;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Hosting;
using GrampsWeb.Mcp.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

/// <summary>
/// Keeps the tool catalog, the READMEs, the MCPB manifest and the site in step with the tools, resources and
/// prompts the server registers, so a tool added, renamed or reclassified fails here until the documentation
/// follows. Each failure names the file and the line to change.
/// </summary>
public partial class ToolSurfaceDocumentationTests
{
    private const string CatalogPath = "GrampsWeb.Mcp/docs/TOOL_CATALOG.md";

    /// <summary>Files that state how many tools, resources and prompts the server has.</summary>
    private static readonly string[] CountFiles =
    [
        "README.md", "GrampsWeb.Mcp/docs/README.md", CatalogPath, "mcpb/manifest.template.json", "site/index.html"
    ];

    /// <summary>
    /// Names in the catalog that are not tools: the media tools 2.0 replaced, named in the migration note, and
    /// the reply header field of undo_change.
    /// </summary>
    private static readonly string[] CatalogNonTools = ["get_media_file", "get_media_thumbnail", "undo_transaction"];

    private static readonly Dictionary<string, int> NumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6,
        ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12
    };

    private static readonly string Root = FindRepositoryRoot();

    private static readonly List<(MethodInfo Method, Tool Tool)> Tools = typeof(PersonTools).Assembly
        .GetTypes()
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
        .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
        .Select(m => (m, McpServerTool.Create(m).ProtocolTool))
        .ToList();

    /// <summary>
    /// Every registered tool has a catalog heading under its method name, every heading is a registered tool, and
    /// the heading's letter matches the annotations: R read-only, C create (not destructive), D delete_object,
    /// U every other destructive tool.
    /// </summary>
    [Fact]
    public void Catalog_Headings_Match_Registered_Tools()
    {
        var headings = ToolHeading().Matches(ReadText(CatalogPath))
            .Select(match => (Letter: match.Groups[1].Value, Name: match.Groups[2].Value))
            .ToList();
        var registered = Tools.ToDictionary(tool => tool.Method.Name, tool => Letter(tool.Tool));

        var duplicated = headings.GroupBy(heading => heading.Name).Where(group => group.Count() > 1).Select(group => group.Key);
        Assert.True(!duplicated.Any(), $"{CatalogPath} documents a tool twice: " + string.Join(", ", duplicated));
        var missing = registered.Keys.Except(headings.Select(heading => heading.Name)).Order().ToArray();
        Assert.True(missing.Length == 0, $"Add a '### R|C|U|D — `Name`' section to {CatalogPath} for: " + string.Join(", ", missing));
        var unknown = headings.Select(heading => heading.Name).Except(registered.Keys).Order().ToArray();
        Assert.True(unknown.Length == 0, $"{CatalogPath} documents tools the server does not register: " + string.Join(", ", unknown));
        var wrongLetter = headings
            .Where(heading => registered[heading.Name] != heading.Letter)
            .Select(heading => $"{heading.Name} is {registered[heading.Name]}, not {heading.Letter}")
            .ToArray();
        Assert.True(wrongLetter.Length == 0, $"Fix the letter in {CatalogPath}: " + string.Join("; ", wrongLetter));
    }

    /// <summary>
    /// Each catalog section names the class that declares its tools, holds exactly that class's tools, and gives
    /// their number in its heading.
    /// </summary>
    [Fact]
    public void Catalog_Sections_Hold_The_Tools_Of_Their_Class()
    {
        var sectionCounts = new Dictionary<string, int>();
        var toolSections = new Dictionary<string, string>();
        string? section = null;
        foreach (var line in ReadText(CatalogPath).Split('\n').Select(line => line.TrimEnd('\r')))
        {
            if (SectionHeading().Match(line) is { Success: true } sectionMatch)
            {
                section = sectionMatch.Groups[1].Value;
                sectionCounts[section] = int.Parse(sectionMatch.Groups[2].Value);
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                section = null;
            }
            else if (ToolHeading().Match(line) is { Success: true } toolMatch)
            {
                toolSections[toolMatch.Groups[2].Value] = section ?? "no tool section";
            }
        }

        var classes = Tools.GroupBy(tool => tool.Method.DeclaringType!.Name).ToDictionary(group => group.Key, group => group.Count());
        var noSection = classes.Keys.Except(sectionCounts.Keys).Order(StringComparer.Ordinal).ToArray();
        Assert.True(noSection.Length == 0, $"Add a '## Name (`Class.cs`) — N tools' section to {CatalogPath} for: " +
                                           string.Join(", ", noSection));
        var noClass = sectionCounts.Keys.Except(classes.Keys).Order(StringComparer.Ordinal).ToArray();
        Assert.True(noClass.Length == 0, $"{CatalogPath} has sections for classes without tools: " + string.Join(", ", noClass));
        foreach (var (className, count) in classes)
            Assert.True(sectionCounts[className] == count,
                $"The {className}.cs section heading in {CatalogPath} says {sectionCounts[className]} tools; the class has {count}.");
        foreach (var (method, _) in Tools)
            Assert.True(toolSections.GetValueOrDefault(method.Name) == method.DeclaringType!.Name,
                $"Move {method.Name} in {CatalogPath} to the {method.DeclaringType!.Name}.cs section.");
    }

    /// <summary>
    /// The tool count summary has one row per tool class with its R, C, U and D counts, and its total row adds them
    /// up to the full catalog published with media access and upload enabled.
    /// </summary>
    [Fact]
    public void Catalog_Summary_Table_Matches_Annotations()
    {
        var catalog = ReadText(CatalogPath);
        var rows = SummaryRow().Matches(catalog).ToDictionary(
            match => match.Groups[1].Value + "Tools",
            match => Enumerable.Range(2, 5).Select(group => int.Parse(match.Groups[group].Value)).ToArray());
        var total = SummaryTotalRow().Match(catalog);
        Assert.True(total.Success, $"{CatalogPath} has no '**Total …**' row in the tool count summary.");

        var expected = Tools.GroupBy(tool => tool.Method.DeclaringType!.Name).ToDictionary(group => group.Key, Counts);
        foreach (var (className, counts) in expected)
            Assert.True(rows.TryGetValue(className, out var row) && counts.SequenceEqual(row),
                $"Summary row {className[..^"Tools".Length]} in {CatalogPath} should read " + string.Join(" | ", counts));
        var extra = rows.Keys.Except(expected.Keys).Select(className => className[..^"Tools".Length]).ToArray();
        Assert.True(extra.Length == 0, $"Remove summary rows without a tool class from {CatalogPath}: " + string.Join(", ", extra));
        var expectedTotal = Counts(Tools);
        Assert.True(expectedTotal.SequenceEqual(Enumerable.Range(1, 5).Select(group => int.Parse(total.Groups[group].Value))),
            $"The summary total row in {CatalogPath} should read " + string.Join(" | ", expectedTotal));
        Assert.Equal(Available(Config(readOnly: false, mediaResourcesEnabled: true, uploadFolders: true)), expectedTotal[4]);
    }

    /// <summary>
    /// The catalog's numbers for the default and read-only profiles are what the tool profile publishes.
    /// </summary>
    [Fact]
    public void Catalog_Profile_Counts_Match_The_Tool_Profile()
    {
        var catalog = Regex.Replace(ReadText(CatalogPath), @"\s+", " ");

        var byDefault = DefaultCountSentence().Match(catalog);
        Assert.True(byDefault.Success, $"{CatalogPath} no longer says how many tools the default catalog contains.");
        AssertCount(Available(Config(readOnly: false, mediaResourcesEnabled: false)), byDefault, 1, "default catalog");

        var readOnly = ReadOnlyCountSentence().Match(catalog);
        Assert.True(readOnly.Success, $"{CatalogPath} no longer says how many tools read-only mode publishes.");
        AssertCount(Available(Config(readOnly: true, mediaResourcesEnabled: true, uploadFolders: true)), readOnly, 1,
            "read-only catalog with media access");
        AssertCount(Available(Config(readOnly: true, mediaResourcesEnabled: false)), readOnly, 2,
            "read-only catalog without media access");

        static void AssertCount(int expected, Match sentence, int group, string profile) =>
            Assert.True(int.Parse(sentence.Groups[group].Value) == expected,
                $"{CatalogPath} says the {profile} has {sentence.Groups[group].Value} tools; the tool profile publishes {expected}.");
    }

    [Fact]
    public void Catalog_Resources_Table_Lists_Registered_Resources()
    {
        var documented = ResourceRow().Matches(Section(ReadText(CatalogPath), "## Resources"))
            .Select(match => match.Groups[1].Value)
            .ToList();

        AssertSameNames(ResourceUris(), documented, "Resources table");
    }

    [Fact]
    public void Catalog_Prompts_Table_Lists_Registered_Prompts()
    {
        var documented = PromptRow().Matches(Section(ReadText(CatalogPath), "## Prompts"))
            .Select(match => match.Groups[1].Value)
            .ToList();

        AssertSameNames(PromptNames(), documented, "Prompts table");
    }

    /// <summary>
    /// "Up to N tools", "N resources" and "N guided prompts" in the READMEs, the catalog, the MCPB manifest and the
    /// site match the server. Each file must still state the tool count, so a reworded line cannot slip past.
    /// </summary>
    [Fact]
    public void Published_Counts_Match_The_Server()
    {
        var expected = new Dictionary<string, int>
        {
            ["tools"] = Tools.Count, ["resources"] = ResourceUris().Count, ["prompts"] = PromptNames().Count
        };
        var found = expected.Keys.ToDictionary(kind => kind, _ => 0);
        var wrong = new List<string>();

        foreach (var file in CountFiles)
        {
            var text = Regex.Replace(ReadText(file), @"\s+", " ");
            var toolMentions = 0;
            foreach (var (kind, pattern) in new[] { ("tools", ToolCount()), ("resources", ResourceCount()), ("prompts", PromptCount()) })
            {
                foreach (Match match in pattern.Matches(text))
                {
                    found[kind]++;
                    if (kind == "tools")
                        toolMentions++;
                    if (ParseCount(match.Groups[1].Value) != expected[kind])
                        wrong.Add($"{file}: '{match.Value}' should say {expected[kind]}");
                }
            }

            Assert.True(toolMentions > 0, $"{file} no longer says 'up to {expected["tools"]} tools'; update {nameof(CountFiles)}.");
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
        Assert.True(found["resources"] > 0, "No file states the number of resources any more.");
        Assert.True(found["prompts"] > 0, "No file states the number of prompts any more.");
    }

    /// <summary>
    /// The documentation names only registered tools. The changelog and the 2.0 migration guide keep old names on
    /// purpose and are not checked.
    /// </summary>
    [Fact]
    public void Documentation_Names_Only_Registered_Tools()
    {
        var files = new[]
            {
                "README.md", "CONTRIBUTING.md", "SECURITY.md", "PRIVACY.md", "mcpb/README.md",
                "mcpb/manifest.template.json", "server.json", "site/index.html"
            }
            .Concat(Directory.GetFiles(Path.Combine(Root, "GrampsWeb.Mcp", "docs"), "*.md")
                .Select(path => Path.GetRelativePath(Root, path).Replace('\\', '/'))
                .Where(path => path != "GrampsWeb.Mcp/docs/MIGRATING_TO_2.md"));

        var stale = files
            .SelectMany(file => UnknownToolNames(ReadText(file), file == CatalogPath ? CatalogNonTools : [])
                .Select(name => $"{file}: {name}"))
            .ToArray();

        Assert.True(stale.Length == 0, "Rename or remove tools that are not registered: " + string.Join(", ", stale));
    }

    /// <summary>
    /// What the server tells the agent names only tools that exist: tool and argument descriptions, prompts, and
    /// the server instructions, which in each profile name only the tools that profile publishes.
    /// </summary>
    [Fact]
    public void Agent_Facing_Texts_Name_Only_Published_Tools()
    {
        foreach (var (_, tool) in Tools)
        {
            var unknown = UnknownToolNames(tool.Description + tool.InputSchema.GetRawText(), []);
            Assert.True(unknown.Length == 0, $"The description of {tool.Name} names: " + string.Join(", ", unknown));
        }

        foreach (var prompt in PromptMethods())
        {
            var text = (string)prompt.Invoke(null, prompt.GetParameters().Select(_ => (object?)"x").ToArray())!;
            var unknown = UnknownToolNames(text, []);
            Assert.True(unknown.Length == 0, $"Prompt {prompt.Name} names: " + string.Join(", ", unknown));
        }

        foreach (var config in new[]
                 {
                     Config(readOnly: false, mediaResourcesEnabled: false),
                     Config(readOnly: false, mediaResourcesEnabled: true),
                     Config(readOnly: false, mediaResourcesEnabled: true, uploadFolders: true),
                     Config(readOnly: true, mediaResourcesEnabled: false),
                     Config(readOnly: true, mediaResourcesEnabled: true, uploadFolders: true)
                 })
        {
            var published = AvailableNames(config);
            var hidden = ToolName().Matches(GrampsServerInstructions.For(config))
                .Select(match => match.Value)
                .Where(name => !published.Contains(name))
                .Distinct()
                .ToArray();
            Assert.True(hidden.Length == 0,
                $"The server instructions for ReadOnly={config.ReadOnly}, MediaResourcesEnabled={config.MediaResourcesEnabled}, " +
                $"MediaUploadEnabled={config.MediaUploadEnabled} name tools that profile hides: " + string.Join(", ", hidden));
        }
    }

    private static void AssertSameNames(IEnumerable<string> registered, IEnumerable<string> documented, string table)
    {
        var missing = registered.Except(documented).Order(StringComparer.Ordinal).ToArray();
        Assert.True(missing.Length == 0, $"Add to the {table} in {CatalogPath}: " + string.Join(", ", missing));
        var unknown = documented.Except(registered).Order(StringComparer.Ordinal).ToArray();
        Assert.True(unknown.Length == 0, $"Remove from the {table} in {CatalogPath}, the server does not register: " +
                                         string.Join(", ", unknown));
    }

    private static string Letter(Tool tool) =>
        tool.Annotations?.ReadOnlyHint == true ? "R"
        : tool.Annotations?.DestructiveHint != true ? "C"
        : tool.Name.StartsWith("delete_", StringComparison.Ordinal) ? "D"
        : "U";

    /// <summary>R, C, U, D and total counts of <paramref name="tools"/>.</summary>
    private static int[] Counts(IEnumerable<(MethodInfo Method, Tool Tool)> tools)
    {
        var letters = tools.Select(tool => Letter(tool.Tool)).ToList();
        return [.. new[] { "R", "C", "U", "D" }.Select(letter => letters.Count(l => l == letter)), letters.Count];
    }

    private static int Available(GrampsConfig config) => AvailableNames(config).Count;

    private static HashSet<string> AvailableNames(GrampsConfig config) =>
        McpToolProfileExtensions.KeepAvailableTools(new ListToolsResult { Tools = Tools.Select(tool => tool.Tool).ToList() }, config)
            .Tools.Select(tool => tool.Name)
            .ToHashSet();

    private static string[] UnknownToolNames(string text, string[] allowed)
    {
        var registered = Tools.Select(tool => tool.Tool.Name).ToHashSet();
        return ToolName().Matches(text)
            .Select(match => match.Value)
            .Where(name => !registered.Contains(name) && !allowed.Contains(name))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static List<string> ResourceUris() => typeof(PersonTools).Assembly
        .GetTypes()
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
        .Select(m => m.GetCustomAttribute<McpServerResourceAttribute>()?.UriTemplate)
        .OfType<string>()
        .ToList();

    private static List<MethodInfo> PromptMethods() => typeof(PersonTools).Assembly
        .GetTypes()
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
        .Where(m => m.GetCustomAttribute<McpServerPromptAttribute>() is not null)
        .ToList();

    private static List<string> PromptNames() =>
        PromptMethods().Select(m => m.GetCustomAttribute<McpServerPromptAttribute>()!.Name!).ToList();

    private static int ParseCount(string value) => NumberWords.TryGetValue(value, out var number) ? number : int.Parse(value);

    /// <summary>The text of a <c>##</c> section, up to the next <c>##</c> heading.</summary>
    private static string Section(string markdown, string heading)
    {
        var start = markdown.IndexOf(heading + "\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{CatalogPath} has no '{heading}' section.");
        var end = markdown.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        return end < 0 ? markdown[start..] : markdown[start..end];
    }

    private static GrampsConfig Config(bool readOnly, bool mediaResourcesEnabled, bool uploadFolders = false) => new(
        ApiUrl: "https://gramps-web.test", Username: "user", Password: "pass", TreeId: "tree",
        ReadOnly: readOnly, MediaResourcesEnabled: mediaResourcesEnabled,
        MediaUploadDirs: uploadFolders ? ["/srv/uploads"] : null);

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

    [GeneratedRegex(@"^### ([RCUD]) — `(\w+)`$", RegexOptions.Multiline)]
    private static partial Regex ToolHeading();

    [GeneratedRegex(@"^## .+ \(`(\w+)\.cs`\) — (\d+) tools?$")]
    private static partial Regex SectionHeading();

    [GeneratedRegex(@"^\| (\w+) \| (\d+) \| (\d+) \| (\d+) \| (\d+) \| (\d+) \|$", RegexOptions.Multiline)]
    private static partial Regex SummaryRow();

    [GeneratedRegex(@"^\| \*\*Total[^|]*\*\* \| \*\*(\d+)\*\* \| \*\*(\d+)\*\* \| \*\*(\d+)\*\* \| \*\*(\d+)\*\* \| \*\*(\d+)\*\* \|$", RegexOptions.Multiline)]
    private static partial Regex SummaryTotalRow();

    [GeneratedRegex(@"the default catalog contains (\d+) tools")]
    private static partial Regex DefaultCountSentence();

    [GeneratedRegex(@"Read-only mode publishes (\d+) tools with media access enabled, or (\d+) with it disabled")]
    private static partial Regex ReadOnlyCountSentence();

    [GeneratedRegex(@"^\| `(gramps://[^`]+)` \|", RegexOptions.Multiline)]
    private static partial Regex ResourceRow();

    [GeneratedRegex(@"^\| `([a-z-]+)` \|", RegexOptions.Multiline)]
    private static partial Regex PromptRow();

    [GeneratedRegex(@"\bup to (\d+) (?:MCP )?tools\b", RegexOptions.IgnoreCase)]
    private static partial Regex ToolCount();

    [GeneratedRegex(@"\b(\d+|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve) (?:MCP )?resources\b", RegexOptions.IgnoreCase)]
    private static partial Regex ResourceCount();

    [GeneratedRegex(@"\b(\d+|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve) (?:guided |MCP )*prompts\b", RegexOptions.IgnoreCase)]
    private static partial Regex PromptCount();

    [GeneratedRegex(@"\b(?:get|list|read|create|update|delete|undo)_[a-z]+(?:_[a-z]+)*\b")]
    private static partial Regex ToolName();
}
