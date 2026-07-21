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

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        Messages.Add(message);
        if (logLevel >= LogLevel.Warning)
            Warnings.Add(message);
    }
}
