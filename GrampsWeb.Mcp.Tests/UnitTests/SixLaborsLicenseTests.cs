using System.Globalization;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

/// <summary>
/// Fails before the committed Six Labors license key expires: ImageSharp 4 refuses to
/// compile the server in Release without a valid key.
/// </summary>
public class SixLaborsLicenseTests
{
    private const int RenewalMarginDays = 30;

    [Fact]
    public void License_Key_Is_Not_About_To_Expire()
    {
        var path = Path.Combine(FindRepositoryRoot(), "GrampsWeb.Mcp", "sixlabors.lic");
        Assert.True(File.Exists(path), $"{path} is missing; Release builds need the Six Labors license key.");

        // The key is "Name=value" fields separated by ";"; signatures may end in "=" padding.
        var fields = File.ReadAllText(path).Trim().Split(';')
            .Select(field => field.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0].Trim(), pair => pair[1].Trim(), StringComparer.OrdinalIgnoreCase);
        Assert.True(fields.TryGetValue("ExpiryDateUtc", out var expiryText), $"{path} has no ExpiryDateUtc.");

        var expiry = DateTimeOffset.Parse(expiryText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        var daysLeft = (int)Math.Floor((expiry - DateTimeOffset.UtcNow).TotalDays);
        Assert.True(daysLeft > RenewalMarginDays,
            $"The Six Labors license key expires on {expiry:yyyy-MM-dd} ({daysLeft} days left), and Release builds "
            + "fail after that. Renew it as GrampsWeb.Mcp/docs/RELEASING.md describes.");
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
