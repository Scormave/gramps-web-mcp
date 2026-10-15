using Xunit;

namespace GrampsWeb.Mcp.IntegrationTests;

/// <summary>
/// The tests start a Gramps Web container, which needs Docker and pulls a 2 GB image, so they run only
/// when <c>GRAMPS_INTEGRATION_TESTS</c> is <c>true</c> or <c>1</c>. A plain <c>dotnet test</c> skips them.
/// </summary>
internal static class IntegrationTestGate
{
    public const string Variable = "GRAMPS_INTEGRATION_TESTS";

    public static bool Enabled =>
        Environment.GetEnvironmentVariable(Variable)?.Trim().ToLowerInvariant() is "true" or "1";

    public const string SkipReason =
        $"Set {Variable}=true to run tests against a Gramps Web container (needs Docker).";
}

/// <summary>A test that runs against the Gramps Web container; skipped unless <see cref="IntegrationTestGate"/> is on.</summary>
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (!IntegrationTestGate.Enabled)
            Skip = IntegrationTestGate.SkipReason;
    }
}

/// <summary>A theory that runs against the Gramps Web container; skipped unless <see cref="IntegrationTestGate"/> is on.</summary>
public sealed class IntegrationTheoryAttribute : TheoryAttribute
{
    public IntegrationTheoryAttribute()
    {
        if (!IntegrationTestGate.Enabled)
            Skip = IntegrationTestGate.SkipReason;
    }
}
