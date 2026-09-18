using System.Net.Http.Headers;
using System.Reflection;

namespace GrampsWeb.Mcp.Client;

internal static class GrampsUserAgent
{
    private static readonly string Version = GetProductVersion(
        typeof(GrampsUserAgent).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? typeof(GrampsUserAgent).Assembly.GetName().Version!.ToString());

    internal static string GetProductVersion(string informationalVersion) =>
        informationalVersion.Split('+', 2)[0];

    public static void Configure(HttpClient client)
    {
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("gramps-web-mcp", Version));
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("(+https://github.com/Scormave/gramps-web-mcp)"));
    }
}
