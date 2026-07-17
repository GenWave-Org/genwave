using Microsoft.Extensions.Logging;

namespace GenWave.MediaLibrary.Tests.Fakes;

/// <summary>
/// Minimal <see cref="ILogger{T}"/> that collects log messages for assertion. <see cref="Warnings"/>
/// covers Warning-and-above (SPEC F48.5's "WARN once per tick" contract); <see cref="Informational"/>
/// adds Debug/Information (SPEC F58.4's "first deferred miss logs at debug/info" contract). Mirrors
/// <c>GenWave.Orchestration.Tests.Fakes.CapturingLogger&lt;T&gt;</c> /
/// <c>GenWave.Tts.Tests.Fakes.CapturingLogger&lt;T&gt;</c>. Test-scope only.
/// </summary>
sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Warnings { get; } = [];
    public List<string> Informational { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        if (logLevel >= LogLevel.Warning)
            Warnings.Add(message);
        else if (logLevel is LogLevel.Debug or LogLevel.Information)
            Informational.Add(message);
    }
}
