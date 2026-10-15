namespace GrampsWeb.Mcp.IntegrationTests;

/// <summary>
/// The Gramps Web image to test against: <c>GRAMPS_WEB_IMAGE</c> when set, else the FROM line of
/// <c>grampsweb.Dockerfile</c>, which Dependabot keeps current.
/// </summary>
internal static class GrampsWebImage
{
    public const string Variable = "GRAMPS_WEB_IMAGE";

    public static string Name
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable(Variable);
            if (!string.IsNullOrWhiteSpace(overridden))
                return overridden.Trim();

            var dockerfile = Path.Combine(AppContext.BaseDirectory, "grampsweb.Dockerfile");
            return File.ReadLines(dockerfile)
                       .Select(line => line.Trim())
                       .Where(line => line.StartsWith("FROM ", StringComparison.OrdinalIgnoreCase))
                       .Select(line => line[5..].Trim())
                       .FirstOrDefault()
                   ?? throw new InvalidOperationException($"{dockerfile} has no FROM line.");
        }
    }
}
