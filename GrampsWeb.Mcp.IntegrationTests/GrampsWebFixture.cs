using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using GrampsWeb.Mcp.Config;
using Xunit;

namespace GrampsWeb.Mcp.IntegrationTests;

/// <summary>Every integration test shares one Gramps Web container and runs one at a time.</summary>
[CollectionDefinition(Name)]
public sealed class GrampsWebCollection : ICollectionFixture<GrampsWebFixture>
{
    public const string Name = "Gramps Web";
}

/// <summary>
/// One Gramps Web container for the whole run, with an empty tree, an owner account and an MCP
/// server connected to it as that owner. Tests create the records they need and leave them; each
/// uses its own unique names, so the records of other tests don't get in the way.
/// </summary>
public sealed class GrampsWebFixture : IAsyncLifetime
{
    public const string OwnerName = "owner";
    private const int GrampsWebPort = 5000;

    private IContainer? _container;
    private McpSession? _session;
    private DirectoryInfo? _uploadDir;

    /// <summary>Base URL of the container's API, as the server's <c>GRAMPS_API_URL</c>.</summary>
    public string ApiUrl { get; private set; } = "";

    public string OwnerPassword { get; private set; } = "";

    /// <summary>The container's <c>GRAMPSWEB_SECRET_KEY</c>, which signs its tokens.</summary>
    public string SecretKey { get; private set; } = "";

    /// <summary>A refresh token of the owner, for a server that signs in with one.</summary>
    public string RefreshToken { get; private set; } = "";

    public string TreeId { get; private set; } = "";

    /// <summary>The folder the shared server may upload media files from.</summary>
    public string UploadDir => _uploadDir?.FullName ?? throw NotStarted();

    /// <summary>Configuration of the shared server: the owner with media access and uploads on.</summary>
    public GrampsConfig Config { get; private set; } = null!;

    public McpSession Session => _session ?? throw NotStarted();

    public async Task InitializeAsync()
    {
        // xunit builds collection fixtures even when every test in the collection is skipped.
        if (!IntegrationTestGate.Enabled)
            return;

        SecretKey = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        OwnerPassword = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

        _container = new ContainerBuilder(GrampsWebImage.Name)
            .WithPortBinding(GrampsWebPort, assignRandomHostPort: true)
            .WithEnvironment("GRAMPSWEB_TREE", "Integration tests")
            .WithEnvironment("GRAMPSWEB_SECRET_KEY", SecretKey)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(
                request => request.ForPort(GrampsWebPort)
                    .ForPath("/api/metadata/")
                    .ForStatusCode(HttpStatusCode.Unauthorized),
                strategy => strategy.WithTimeout(TimeSpan.FromMinutes(4))))
            .Build();
        await _container.StartAsync();
        ApiUrl = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(GrampsWebPort)}";

        using var http = new HttpClient(new TokenRoutePacer(new TokenRequestLog(), new HttpClientHandler()))
        {
            BaseAddress = new Uri(ApiUrl)
        };
        await CreateOwnerAsync(http);
        var accessToken = await SignInAsync(http);
        TreeId = await ReadTreeIdAsync(http, accessToken);

        _uploadDir = Directory.CreateTempSubdirectory("gramps-mcp-it-");
        Config = new GrampsConfig(
            ApiUrl,
            OwnerName,
            OwnerPassword,
            TreeId,
            MediaResourcesEnabled: true,
            MediaUploadDirs: [_uploadDir.FullName]);
        _session = await McpSession.StartAsync(Config);
    }

    /// <summary>Starts another server against the container, for tests of other configurations.</summary>
    public Task<McpSession> StartSessionAsync(GrampsConfig config, Func<DelegatingHandler>? tokenHandler = null) =>
        McpSession.StartAsync(config, tokenHandler);

    /// <summary>Writes <paramref name="bytes"/> to a new file in the upload folder and returns its name.</summary>
    public string WriteUpload(string extension, byte[] bytes)
    {
        var name = $"{TestNames.Unique("upload")}{extension}";
        File.WriteAllBytes(Path.Combine(UploadDir, name), bytes);
        return name;
    }

    public async Task DisposeAsync()
    {
        if (_session is not null)
            await _session.DisposeAsync();
        if (_container is not null)
            await _container.DisposeAsync();
        _uploadDir?.Delete(recursive: true);
    }

    /// <summary>
    /// The first-run flow of Gramps Web: a one-time token from <c>create_owner</c>, then the owner account.
    /// </summary>
    private async Task CreateOwnerAsync(HttpClient http)
    {
        var setup = await ReadJsonAsync(await http.GetAsync("/api/token/create_owner/"));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/users/{OwnerName}/create_owner/")
        {
            Content = JsonContent.Create(new
            {
                password = OwnerPassword,
                email = "owner@example.com",
                full_name = "Integration Owner"
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", setup.GetProperty("access_token").GetString());
        await ReadJsonAsync(await http.SendAsync(request));
    }

    private async Task<string> SignInAsync(HttpClient http)
    {
        var tokens = await ReadJsonAsync(await http.PostAsJsonAsync(
            "/api/token/",
            new { username = OwnerName, password = OwnerPassword }));
        RefreshToken = tokens.GetProperty("refresh_token").GetString()!;
        return tokens.GetProperty("access_token").GetString()!;
    }

    private static async Task<string> ReadTreeIdAsync(HttpClient http, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/metadata/");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var metadata = await ReadJsonAsync(await http.SendAsync(request));
        return metadata.GetProperty("database").GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.AbsolutePath} " +
                    $"returned {(int)response.StatusCode}: {body}");
            return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
        }
    }

    private static InvalidOperationException NotStarted() =>
        new($"The Gramps Web fixture is not running; set {IntegrationTestGate.Variable}=true.");
}
