using Microsoft.Extensions.Logging;

namespace GenWave.Ads.Tests.Fakes;

/// <summary>
/// <see cref="ILogger{T}"/> double that discards every entry — enough for a spec that constructs a
/// real <see cref="GenWave.Tts.AdScriptWriter"/> and does not itself care what it logs (PLAN T400
/// review F2's real-Tts-meets-real-Ads crossing fact). Use this when a test does not read log
/// lines; use the local <see cref="CapturingLogger{T}"/> (same namespace) when it does.
/// </summary>
public sealed class NoOpLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => false;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
    }
}
