namespace GenWave.Ads.Tests.Fakes;

using Microsoft.Extensions.Logging;

/// <summary>Minimal <see cref="ILogger{T}"/> that collects every logged message for assertion (the
/// <c>GenWave.Tts.Tests.Fakes.CapturingLogger</c> precedent, copied verbatim into this project's own
/// Fakes — PLAN T415, STORY-402 AC7's "one INFO line names the empty pool" fact needs a level-scoped
/// capture <see cref="NoOpLogger{T}"/> can't provide). Test-scope only.</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Warnings { get; } = [];

    /// <summary>Every message logged at any level, in order.</summary>
    public List<string> Messages { get; } = [];

    /// <summary>Every entry logged, level and message together, in order — lets a spec assert a line
    /// was logged at an EXACT level, not merely at some level at-or-above a floor.</summary>
    public List<LogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        Messages.Add(message);
        Entries.Add(new LogEntry(logLevel, message));
        if (logLevel >= LogLevel.Warning)
            Warnings.Add(message);
    }

    /// <summary>One <see cref="Log{TState}"/> call's level and formatted message, paired together.
    /// Nested rather than a second top-level type in this file: it exists only to give
    /// <see cref="Entries"/> a shape, never constructed anywhere else.</summary>
    public sealed record LogEntry(LogLevel Level, string Message);
}
