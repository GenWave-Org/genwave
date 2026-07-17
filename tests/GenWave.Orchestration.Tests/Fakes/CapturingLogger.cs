namespace GenWave.Orchestration.Tests.Fakes;

using Microsoft.Extensions.Logging;

/// <summary>
/// Minimal <see cref="ILogger{T}"/> that collects Warning-and-above messages for assertion (SPEC
/// F41.5's "WARN naming the relaxed constraint" contract). Mirrors
/// <c>GenWave.Tts.Tests.Fakes.CapturingLogger&lt;T&gt;</c>. Test-scope only.
/// </summary>
sealed class CapturingLogger<T> : ILogger<T>
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
