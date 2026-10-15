using System.Security.Cryptography;

namespace GrampsWeb.Mcp.IntegrationTests;

/// <summary>
/// Names no other test uses, so a search or filter finds only the records of the test that made them.
/// Letters only, so each name stays one word for Gramps Web's full-text search.
/// </summary>
internal static class TestNames
{
    private const string Letters = "abcdefghijklmnopqrstuvwxyz";

    /// <summary>A capitalised word of random letters, such as a surname: <c>Qvhtrmzkpa</c>.</summary>
    public static string Surname() => char.ToUpperInvariant(RandomLetters(1)[0]) + RandomLetters(9);

    /// <summary><paramref name="prefix"/> followed by random letters.</summary>
    public static string Unique(string prefix) => prefix + RandomLetters(10);

    private static string RandomLetters(int count) =>
        RandomNumberGenerator.GetString(Letters, count);
}
