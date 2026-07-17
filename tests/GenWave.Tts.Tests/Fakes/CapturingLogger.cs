namespace GenWave.Tts.Tests.Fakes;

using Microsoft.Extensions.Logging;

/// <summary>Minimal <see cref="ILogger{T}"/> that collects Warning-and-above messages for
/// assertion (SPEC F34.4's "exactly one WARN" contract). Test-scope only.</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Warnings { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel >= LogLevel.Warning)
            Warnings.Add(formatter(state, exception));
    }
}
