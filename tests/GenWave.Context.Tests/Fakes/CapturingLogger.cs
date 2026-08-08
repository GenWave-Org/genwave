namespace GenWave.Context.Tests.Fakes;

using Microsoft.Extensions.Logging;

/// <summary>
/// Minimal <see cref="ILogger{T}"/> double that collects every logged message for assertion, tagged
/// with its <see cref="LogLevel"/>. Mirrors <c>GenWave.Orchestration.Tests.Fakes.CapturingLogger&lt;T&gt;</c>
/// / <c>GenWave.Tts.Tests.Fakes.CapturingLogger&lt;T&gt;</c>. Test-scope only.
/// </summary>
sealed class CapturingLogger<T> : ILogger<T>
{
    /// <summary>Every logged message, in call order, tagged with the level it was logged at.</summary>
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, formatter(state, exception)));
    }
}
