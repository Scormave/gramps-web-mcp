using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace GrampsWeb.Mcp.Logging;

/// <summary>
/// Writes one-line logs as <c>info: message</c> without category or event id prefixes.
/// </summary>
public sealed class MinimalConsoleFormatter : ConsoleFormatter
{
    public const string FormatterName = "minimal";

    public MinimalConsoleFormatter()
        : base(FormatterName)
    {
    }

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        textWriter.Write(GetLevelPrefix(logEntry.LogLevel));
        textWriter.Write(": ");
        textWriter.WriteLine(message);

        if (logEntry.Exception is not null)
        {
            textWriter.WriteLine(logEntry.Exception);
        }
    }

    private static string GetLevelPrefix(LogLevel level) => level switch
    {
        LogLevel.Trace => "trace",
        LogLevel.Debug => "dbug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "fail",
        LogLevel.Critical => "crit",
        _ => level.ToString().ToLowerInvariant()
    };
}
