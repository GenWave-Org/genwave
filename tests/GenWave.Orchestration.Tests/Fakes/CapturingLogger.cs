namespace GenWave.Orchestration.Tests.Fakes;

using Microsoft.Extensions.Logging;

/// <summary>
/// Minimal <see cref="ILogger{T}"/> that collects every logged message for assertion, tagged with
/// its <see cref="LogLevel"/> (<see cref="Entries"/>) — <see cref="Warnings"/> narrows that to
/// Warning-and-above (SPEC F41.5's "WARN naming the relaxed constraint" contract), unchanged from
/// this type's original shape so every existing <c>logger.Warnings</c> assertion keeps compiling and
/// passing byte-for-byte. <see cref="Entries"/> itself (STORY-213, PLAN T64) exists for the SPEC
/// F82.6 per-pick Debug line, which no pre-T64 spec needed to inspect. Mirrors
/// <c>GenWave.Tts.Tests.Fakes.CapturingLogger&lt;T&gt;</c>. Test-scope only.
/// </summary>
sealed class CapturingLogger<T> : ILogger<T>
{
    /// <summary>Every logged message, in call order, tagged with the level it was logged at.</summary>
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IEnumerable<string> Warnings => Entries.Where(e => e.Level >= LogLevel.Warning).Select(e => e.Message);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, formatter(state, exception)));
    }
}
