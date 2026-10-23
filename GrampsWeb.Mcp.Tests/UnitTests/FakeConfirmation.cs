using GrampsWeb.Mcp.Tools;

namespace GrampsWeb.Mcp.Tests.UnitTests;

/// <summary>
/// A user who answers every confirmation question with <paramref name="answer"/>. Records the questions;
/// <paramref name="whileDeciding"/> runs before the answer, to change the server as another user would.
/// </summary>
internal sealed class FakeConfirmation(ConfirmationAnswer answer, Action? whileDeciding = null) : IUserConfirmation
{
    public List<string> Questions { get; } = [];

    public bool Asks => true;

    public Task<ConfirmationAnswer> AskAsync(string message, CancellationToken cancellationToken)
    {
        Questions.Add(message);
        whileDeciding?.Invoke();
        return Task.FromResult(answer);
    }
}
