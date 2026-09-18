namespace GenWave.TestSupport.Fakes;

using Microsoft.Extensions.Logging;

/// <summary>
/// Adapts any <see cref="ILogger"/> into an <see cref="ILogger{T}"/> for a different category by
/// forwarding every call verbatim — mirrors how the real <c>Microsoft.Extensions.Logging.Logger&lt;T&gt;</c>
/// wraps one <see cref="ILoggerFactory"/>-resolved sink under many typed facades, so every category an
/// app logs under still lands in the same configured providers. PLAN T522: lets
/// <see cref="OrchestratorBuilder"/> route <c>BreakPlanner</c>'s own WARN/INFO into the SAME
/// <see cref="CapturingLogger{T}"/> a spec already asserts on via <c>WithLogger</c>/
/// <c>OrchestratorChain.Logger</c> — reproducing production's single-sink-many-categories reality that
/// two independently-<c>new</c>'d <see cref="CapturingLogger{T}"/> instances would not. Test-scope only.
/// </summary>
public sealed class ForwardingLogger<T>(ILogger inner) : ILogger<T>
{
    /// <summary>Forwards to <paramref name="inner"/>'s own scope tracking.</summary>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

    /// <summary>Forwards to <paramref name="inner"/>'s own level gate.</summary>
    public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

    /// <summary>Forwards the entry to <paramref name="inner"/> verbatim — same level, same message.</summary>
    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        inner.Log(logLevel, eventId, state, exception, formatter);
}
