namespace GenWave.Tts.Tests.Fakes;

using Microsoft.Extensions.Logging;

/// <summary>Minimal <see cref="ILogger{T}"/> that collects Warning-and-above messages for
/// assertion (SPEC F34.4's "exactly one WARN" contract). Test-scope only.</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Warnings { get; } = [];

    /// <summary>Every message logged at any level, in order (SPEC F69.5, STORY-188) — a mode
    /// transition logs at Information, below <see cref="Warnings"/>' Warning-and-above floor.</summary>
    public List<string> Messages { get; } = [];

    /// <summary>
    /// Every entry logged, level and message together, in order (PLAN T142, SPEC F97.5) — a level-
    /// scoped collection <see cref="Warnings"/>/<see cref="Messages"/> alone can't answer "was THIS
    /// specific line logged at Information, not merely at some level at-or-above Warning": a spec
    /// proving a debug-to-Information amendment actually happened needs to assert both that an
    /// Information entry carries the line AND that no Debug/Warning entry also does (Debug never
    /// reaching the fleet log store is the whole point of the amendment — the T263 precedent
    /// proves absence via <see cref="Warnings"/>' floor; this proves absence at an EXACT level).
    /// </summary>
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

    /// <summary>One <see cref="Log{TState}"/> call's level and formatted message, paired together —
    /// see <see cref="Entries"/> for why the pairing itself is the point. Nested (mirrors
    /// <c>TestOptionsMonitor{T}.NoopDisposable</c>) rather than a second top-level type in this
    /// file: it exists only to give <see cref="Entries"/> a shape, never constructed anywhere
    /// else.</summary>
    public sealed record LogEntry(LogLevel Level, string Message);
}
