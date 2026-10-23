using System.Collections.Concurrent;
using System.Text.Json;
using GrampsWeb.Mcp.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Tools;

/// <summary>The user's answer to a confirmation question.</summary>
internal enum ConfirmationAnswer
{
    /// <summary>The user ticked the box and accepted.</summary>
    Confirmed,

    /// <summary>The user declined, or accepted without ticking the box.</summary>
    Declined,

    /// <summary>The user closed the question without answering.</summary>
    Cancelled,

    /// <summary><c>GRAMPS_CONFIRMATION=required</c>, but the client cannot show the question.</summary>
    Unavailable,

    /// <summary>The client answered the question with an error.</summary>
    Failed
}

/// <summary>
/// Asks the user, not the assistant, to confirm a change before a tool makes it.
/// </summary>
internal interface IUserConfirmation
{
    /// <summary>True when the tool must get an answer before it changes the tree.</summary>
    bool Asks { get; }

    Task<ConfirmationAnswer> AskAsync(string message, CancellationToken cancellationToken);
}

/// <summary>
/// Confirmation through MCP elicitation for delete_object and undo_change. The client shows the
/// question to the user and the tool waits for the answer; <c>GRAMPS_CONFIRMATION</c> chooses
/// whether to ask when the client supports it (default), always, or never.
/// </summary>
internal static class UserConfirmation
{
    /// <summary>The boolean field the user ticks to confirm.</summary>
    internal const string ConfirmField = "confirm";

    /// <summary>Never asks: the tool changes the tree as before.</summary>
    public static IUserConfirmation None { get; } = new NotAsking();

    /// <summary>Clients whose elicitation support was already logged, so each is logged once.</summary>
    private static readonly ConcurrentDictionary<string, byte> LoggedClients = new();

    /// <summary>
    /// The confirmation for one tool call on <paramref name="server"/>, the request-scoped server the SDK
    /// passes to the tool. Without a server (direct calls in tests) nothing is asked.
    /// </summary>
    public static IUserConfirmation For(McpServer? server, string toolName)
    {
        if (server is null)
            return None;

        var mode = server.Services?.GetService<GrampsConfig>()?.Confirmation ?? ConfirmationMode.WhenSupported;
        var supported = server.ClientCapabilities?.Elicitation?.Form is not null;
        var logger = server.Services?.GetService<ILoggerFactory>()?.CreateLogger(typeof(UserConfirmation).FullName!)
            ?? NullLogger.Instance;
        var client = $"{server.ClientInfo?.Name ?? "unknown client"} {server.ClientInfo?.Version}".Trim();
        var asks = mode == ConfirmationMode.Required || (mode == ConfirmationMode.WhenSupported && supported);

        if (LoggedClients.TryAdd($"{client}|{mode}|{supported}", 0))
        {
            logger.LogInformation(
                "Client {Client} {Support} elicitation; delete_object and undo_change {Behaviour} (GRAMPS_CONFIRMATION={Mode})",
                client, supported ? "supports" : "does not support",
                !asks ? "run without asking" : supported ? "ask the user to confirm" : "refuse to change the tree",
                ModeName(mode));
        }

        return asks ? new ElicitationConfirmation(server, supported, toolName, client, logger) : None;
    }

    /// <summary>
    /// Explains an answer other than <see cref="ConfirmationAnswer.Confirmed"/>; nothing was changed.
    /// </summary>
    public static string Explain(ConfirmationAnswer answer) => answer switch
    {
        ConfirmationAnswer.Declined =>
            "The user declined in the confirmation dialog, so nothing was changed. Do not call again unless the user asks again.",
        ConfirmationAnswer.Cancelled =>
            "The user closed the confirmation dialog without answering, so nothing was changed. Ask the user in chat before calling again.",
        ConfirmationAnswer.Unavailable =>
            "The server requires the user to confirm this change (GRAMPS_CONFIRMATION=required), but this client cannot " +
            "show confirmation dialogs (MCP elicitation), so nothing was changed. Tell the user; the server administrator " +
            "can set GRAMPS_CONFIRMATION=when-supported to rely on the confirmation in chat.",
        ConfirmationAnswer.Failed =>
            "The client could not show the confirmation dialog, so nothing was changed. Call again only if the user asks.",
        _ => throw new ArgumentOutOfRangeException(nameof(answer), answer, "Confirmed needs no explanation.")
    };

    /// <summary>The header action of a reply for an answer other than <see cref="ConfirmationAnswer.Confirmed"/>.</summary>
    public static string ActionName(ConfirmationAnswer answer) => answer switch
    {
        ConfirmationAnswer.Declined => "declined",
        ConfirmationAnswer.Cancelled => "cancelled",
        _ => "not_confirmed"
    };

    private static string ModeName(ConfirmationMode mode) => mode switch
    {
        ConfirmationMode.Required => "required",
        ConfirmationMode.Off => "off",
        _ => "when-supported"
    };

    private sealed class NotAsking : IUserConfirmation
    {
        public bool Asks => false;

        public Task<ConfirmationAnswer> AskAsync(string message, CancellationToken cancellationToken) =>
            Task.FromResult(ConfirmationAnswer.Confirmed);
    }

    private sealed class ElicitationConfirmation(
        McpServer server, bool supported, string toolName, string client, ILogger logger) : IUserConfirmation
    {
        public bool Asks => true;

        public async Task<ConfirmationAnswer> AskAsync(string message, CancellationToken cancellationToken)
        {
            var answer = supported ? await RequestAsync(message, cancellationToken) : ConfirmationAnswer.Unavailable;
            logger.LogInformation("Confirmation for {Tool} from {Client}: {Answer}", toolName, client, answer);
            return answer;
        }

        private async Task<ConfirmationAnswer> RequestAsync(string message, CancellationToken cancellationToken)
        {
            ElicitResult result;
            try
            {
                result = await server.ElicitAsync(new ElicitRequestParams
                {
                    Message = message,
                    RequestedSchema = new ElicitRequestParams.RequestSchema
                    {
                        Properties =
                        {
                            [ConfirmField] = new ElicitRequestParams.BooleanSchema
                            {
                                Title = "Yes, make this change",
                                Description = "Tick to confirm, then accept. Declining changes nothing.",
                                Default = false
                            }
                        },
                        Required = [ConfirmField]
                    }
                }, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                // The client stopped advertising elicitation (or the transport cannot send requests).
                logger.LogWarning(ex, "Confirmation for {Tool} could not be sent to {Client}", toolName, client);
                return ConfirmationAnswer.Unavailable;
            }
            catch (McpException ex)
            {
                logger.LogWarning(ex, "Confirmation for {Tool} failed in {Client}", toolName, client);
                return ConfirmationAnswer.Failed;
            }

            return result.Action switch
            {
                "accept" when IsTicked(result) => ConfirmationAnswer.Confirmed,
                "accept" or "decline" => ConfirmationAnswer.Declined,
                _ => ConfirmationAnswer.Cancelled
            };
        }

        private static bool IsTicked(ElicitResult result) =>
            result.Content is { } content
            && content.TryGetValue(ConfirmField, out var value)
            && (value.ValueKind == JsonValueKind.True
                || (value.ValueKind == JsonValueKind.String
                    && string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase)));
    }
}
